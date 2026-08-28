using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;
using uTracerProManager.Core.Safety;

namespace uTracerProManager.Core.Services;

public sealed class ReferenceMeasurementController
{
    private sealed record Target(double X, double Step, double Va, double Vs, double Vg, double Vh);
    private sealed record Reading(double Ia, double Is, double Va, double Vs, string Status);

    private readonly SafetyValidator _safety = new();

    public async Task<ReferenceMeasurementResult> RunAsync(
        TubeProfile profile,
        ITracerTransport transport,
        CalibrationProfile calibration,
        HardwareCapabilities hardware,
        ReferenceMeasurementRequest request,
        IProgress<ReferenceMeasurementProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (!transport.IsConnected)
            throw new InvalidOperationException("Tester nie jest połączony.");
        if (!transport.IsEmulator && !hardware.SupportsCurrentProtocol)
            throw new NotSupportedException($"Sterownik protokołu {hardware.DisplayName} nie jest jeszcze aktywny.");
        HardwareCapabilityGuard.EnsureProfileFits(profile, hardware);
        var safety = _safety.ValidateProfile(profile);
        if (!safety.IsSafe)
            throw new InvalidOperationException("Profil nie przeszedł kontroli bezpieczeństwa:\n- " + string.Join("\n- ", safety.Errors));
        if (!transport.IsEmulator && !calibration.IsCompleteForTubeTesting)
            throw new InvalidOperationException("Rzeczywisty skan wymaga kompletnej kalibracji v2.");

        var calibrationErrors = calibration.Validate();
        if (calibrationErrors.Count > 0)
            throw new InvalidOperationException("Kalibracja jest nieprawidłowa:\n- " + string.Join("\n- ", calibrationErrors));

        var targets = BuildTargets(request).ToArray();
        ValidateTargets(profile, hardware, calibration, request, targets);
        var startedAt = DateTimeOffset.Now;
        var points = new List<ReferenceMeasurementPoint>(targets.Length);
        var configured = false;
        var highestVoltage = targets.Max(target => Math.Max(target.Va, target.Vs));

        try
        {
            Report(progress, "Kontrola zasilania i przygotowanie skanu.", 1, 0, targets.Length);
            var supplyReading = await transport.ReadAdcAsync(calibration, new AdcConversionOptions(), cancellationToken);
            var supply = transport.IsEmulator ? 19.2 :
                supplyReading.Engineering?.SupplyVoltage ?? throw new InvalidOperationException("Nie odczytano Vsu.");
            if (supply is < 10 or > 25)
                throw new InvalidOperationException($"Vsu={supply:F2} V jest poza zakresem 10–25 V.");

            await transport.SendFilamentCodeAsync(0, cancellationToken);
            await transport.SendStartMeasurementAsync(
                CurrentLimitCodes.ForMilliAmps(request.ComplianceMa),
                AverageCode(request.AveragingIndex), 8, 8, cancellationToken);
            configured = true;

            var firstHeater = request.ExternalHeater ? 0 : targets[0].Vh;
            if (!request.ExternalHeater)
                await RampHeaterAsync(firstHeater, supply, transport, request, progress, targets.Length, cancellationToken);
            await DelayAsync(request.WarmupSeconds * 1000, transport.IsEmulator, cancellationToken);

            double previousHeater = firstHeater;
            for (var index = 0; index < targets.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = targets[index];
                if (!request.ExternalHeater && Math.Abs(target.Vh - previousHeater) > 0.0001)
                {
                    await transport.SendFilamentCodeAsync(CommandCodeConverter.HeaterCode(target.Vh, supply), cancellationToken);
                    previousHeater = target.Vh;
                    await DelayAsync(Math.Max(1, request.DelaySeconds) * 1000, transport.IsEmulator, cancellationToken);
                }
                else if (request.DelaySeconds > 0)
                {
                    await DelayAsync(request.DelaySeconds * 1000, transport.IsEmulator, cancellationToken);
                }

                Reading reading = transport.IsEmulator
                    ? Emulate(profile, target)
                    : await MeasureCorrectedAsync(profile, transport, calibration, supply, target,
                        request.ExternalHeater ? (ushort)0 : CommandCodeConverter.HeaterCode(target.Vh, supply),
                        request.AveragingIndex, cancellationToken);

                ValidateMeasuredPower(profile, request, target, reading);
                points.Add(new ReferenceMeasurementPoint(
                    index + 1,
                    index / (request.Intervals + 1),
                    target.Step,
                    target.X,
                    target.Va,
                    reading.Va,
                    target.Vs,
                    reading.Vs,
                    target.Vg,
                    target.Vh,
                    reading.Ia,
                    reading.Is,
                    reading.Status));
                Report(progress,
                    $"Punkt {index + 1}/{targets.Length}: X={target.X:F3}, krok={target.Step:F3}.",
                    10 + 82.0 * (index + 1) / targets.Length, index + 1, targets.Length);
            }

            await SafeShutdownAsync(transport, highestVoltage, transport.IsEmulator, progress);
            configured = false;
            Report(progress, "Skan zakończony.", 100, targets.Length, targets.Length);
            return new ReferenceMeasurementResult(
                ReferenceMeasurementDefinition.For(request.Kind), request, profile, points,
                startedAt, DateTimeOffset.Now, transport.IsEmulator);
        }
        catch
        {
            if (configured)
                await SafeShutdownAsync(transport, highestVoltage, transport.IsEmulator, progress);
            throw;
        }
    }

    private static IEnumerable<Target> BuildTargets(ReferenceMeasurementRequest request)
    {
        var xs = BuildAxis(request.XStart, request.XStop, request.Intervals, request.LogarithmicX);
        var maxVa = request.Kind switch
        {
            ReferenceMeasurementKind.GridSweepSteppedAnodeUltraLinear => request.SteppingValues.Max(),
            ReferenceMeasurementKind.AnodeSweepSteppedGridUltraLinear => xs.Max(),
            _ => Math.Max(request.ConstantVa, xs.Max())
        };
        foreach (var step in request.SteppingValues)
        foreach (var x in xs)
        {
            yield return request.Kind switch
            {
                ReferenceMeasurementKind.GridSweepSteppedAnode => new(x, step, step, request.ConstantVs, x, request.ConstantVh),
                ReferenceMeasurementKind.GridSweepSteppedTiedAnodeScreen => new(x, step, step, step, x, request.ConstantVh),
                ReferenceMeasurementKind.AnodeSweepSteppedGrid => new(x, step, x, request.ConstantVs, step, request.ConstantVh),
                ReferenceMeasurementKind.AnodeSweepSteppedScreen => new(x, step, x, step, request.ConstantVg, request.ConstantVh),
                ReferenceMeasurementKind.TiedAnodeScreenSweepSteppedGrid => new(x, step, x, x, step, request.ConstantVh),
                ReferenceMeasurementKind.ScreenSweepSteppedGrid => new(x, step, request.ConstantVa, x, step, request.ConstantVh),
                ReferenceMeasurementKind.PositiveGridSweepSteppedAnode => new(x, step, step, x, 0, request.ConstantVh),
                ReferenceMeasurementKind.AnodeSweepSteppedPositiveGrid => new(x, step, x, step, 0, request.ConstantVh),
                ReferenceMeasurementKind.HeaterSweepSteppedGrid => new(x, step, request.ConstantVa, request.ConstantVs, step, x),
                ReferenceMeasurementKind.HeaterSweepSteppedAnode => new(x, step, step, request.ConstantVs, request.ConstantVg, x),
                ReferenceMeasurementKind.GridSweepSteppedAnodeUltraLinear =>
                    new(x, step, step, UltraLinearScreen(step, maxVa, request.UltraLinearKPercent), x, request.ConstantVh),
                ReferenceMeasurementKind.AnodeSweepSteppedGridUltraLinear =>
                    new(x, step, x, UltraLinearScreen(x, maxVa, request.UltraLinearKPercent), step, request.ConstantVh),
                ReferenceMeasurementKind.AnodeSweepSteppedGridSchadeFeedback =>
                    new(x, step, x, request.ConstantVs, SchadeGrid(step, x, request.SchadeFeedbackPercent), request.ConstantVh),
                _ => throw new ArgumentOutOfRangeException(nameof(request.Kind))
            };
        }
    }

    private static double[] BuildAxis(double start, double stop, int intervals, bool logarithmic)
    {
        var values = new double[intervals + 1];
        for (var i = 0; i <= intervals; i++)
        {
            var fraction = (double)i / intervals;
            values[i] = logarithmic
                ? Math.Exp(Math.Log(start) + (Math.Log(stop) - Math.Log(start)) * fraction)
                : start + (stop - start) * fraction;
        }
        return values;
    }

    private static double UltraLinearScreen(double va, double vaMax, double kPercent) =>
        va + (1.0 - kPercent / 100.0) * (vaMax - va);

    private static double SchadeGrid(double vgSet, double va, double feedbackPercent) =>
        vgSet + (va - vgSet) * feedbackPercent / 100.0;

    private static void ValidateTargets(
        TubeProfile profile,
        HardwareCapabilities hardware,
        CalibrationProfile calibration,
        ReferenceMeasurementRequest request,
        IReadOnlyList<Target> targets)
    {
        var maxVa = Math.Min(hardware.MaxAnodeVoltage, calibration.MaxAnodeVoltage);
        var maxVs = Math.Min(hardware.MaxScreenVoltage, calibration.MaxAnodeVoltage);
        foreach (var target in targets)
        {
            if (target.Va is < 0 || target.Va > maxVa)
                throw new InvalidOperationException($"Va={target.Va:F2} V przekracza zakres wybranego sprzętu/kalibracji.");
            if (target.Vs is < 0 || target.Vs > maxVs)
                throw new InvalidOperationException($"Vs={target.Vs:F2} V przekracza zakres wybranego sprzętu/kalibracji.");
            if (target.Vg < hardware.MinGridVoltage || target.Vg > 0)
                throw new InvalidOperationException($"Wyliczone Vg={target.Vg:F3} V jest poza zakresem {hardware.MinGridVoltage:F0}…0 V.");
            if (!request.ExternalHeater && (target.Vh <= 0 || target.Vh > 24))
                throw new InvalidOperationException($"Vh={target.Vh:F2} V jest poza bezpiecznym zakresem wewnętrznego sterownika.");
            if (profile.MaxAnodeVoltage > 0 && target.Va > profile.MaxAnodeVoltage)
                throw new InvalidOperationException($"Va={target.Va:F1} V przekracza katalogowe Va max profilu.");
            if (profile.MaxScreenVoltage > 0 && target.Vs > profile.MaxScreenVoltage && !profile.IsDualTriode)
                throw new InvalidOperationException($"Vs={target.Vs:F1} V przekracza katalogowe Vs max profilu.");
        }
        if (request.ComplianceMa > hardware.MaxPulseCurrentMa)
            throw new InvalidOperationException("Compliance przekracza limit wybranego wariantu sprzętu.");
        if (request.ComplianceMa > Math.Max(profile.AnodeComplianceMa, profile.ScreenComplianceMa) + 0.1)
            throw new InvalidOperationException("Compliance jest większe niż zatwierdzony limit profilu.");
    }

    private static async Task<Reading> MeasureCorrectedAsync(
        TubeProfile profile,
        ITracerTransport transport,
        CalibrationProfile calibration,
        double supply,
        Target target,
        ushort heaterCode,
        int averagingIndex,
        CancellationToken cancellationToken)
    {
        var correction = new VoltageSetpointController();
        var commandVa = target.Va;
        var commandVs = target.Vs;
        MeasurementResult? result = null;
        for (var iteration = 0; iteration < 5; iteration++)
        {
            var vaCode = CommandCodeConverter.AnodeCode(commandVa, supply, calibration);
            var vsCode = target.Vs > 0 ? CommandCodeConverter.ScreenCode(commandVs, supply, calibration) : (ushort)0;
            var vgCode = CommandCodeConverter.GridCode(target.Vg, calibration);
            result = await transport.ExecuteMeasurementAsync(
                vaCode, vsCode, vgCode, heaterCode, calibration,
                new AdcConversionOptions(averagingIndex, commandVa, commandVs, target.Vg), cancellationToken);
            if (result.CurrentLimitHit)
                throw new CurrentLimitException("Status 11 — zadziałało ograniczenie prądowe.");
            var engineering = result.Engineering ?? throw new InvalidOperationException("Brak przeliczonych danych ADC.");
            var vaCorrection = correction.Correct(target.Va, commandVa, engineering.EstimatedAnodeVoltage,
                Math.Min(profile.MaxAnodeVoltage, calibration.MaxAnodeVoltage));
            var vsLimit = profile.IsDualTriode
                ? Math.Min(profile.MaxAnodeVoltage, calibration.MaxAnodeVoltage)
                : profile.MaxScreenVoltage > 0 ? Math.Min(profile.MaxScreenVoltage, calibration.MaxAnodeVoltage) : calibration.MaxAnodeVoltage;
            var vsCorrection = target.Vs > 0
                ? correction.Correct(target.Vs, commandVs, engineering.MeasuredScreenVoltage, vsLimit)
                : new VoltageCorrection(0, 0, 0, 0, 0, true, false);
            commandVa = vaCorrection.NewCommandVoltage;
            commandVs = vsCorrection.NewCommandVoltage;
            if (vaCorrection.InTolerance && vsCorrection.InTolerance)
                break;
        }
        var reading = result?.Engineering ?? throw new InvalidOperationException("Nie wykonano punktu pomiarowego.");
        return new Reading(reading.AnodeCurrentMa, reading.ScreenCurrentMa,
            reading.EstimatedAnodeVoltage, reading.MeasuredScreenVoltage, result!.StatusCode);
    }

    private static Reading Emulate(TubeProfile profile, Target target)
    {
        var vaFactor = profile.AnodeVoltage > 0 ? Math.Pow(Math.Max(0.001, target.Va / profile.AnodeVoltage), 0.72) : 1;
        var gm = Math.Max(0.05, profile.NominalGmMaV);
        var iaNominal = Math.Max(0.01, profile.NominalAnodeCurrentMa);
        var gridFactor = Math.Exp(Math.Clamp((target.Vg - profile.GridVoltage) * gm / iaNominal, -8, 3));
        var heaterFactor = profile.HeaterVoltage > 0
            ? Math.Pow(Math.Clamp(target.Vh / profile.HeaterVoltage, 0, 1.15), 4)
            : 1;
        var screenFactor = profile.ScreenVoltage > 0
            ? Math.Pow(Math.Max(0.05, target.Vs / profile.ScreenVoltage), 0.28)
            : 1;
        var ia = iaNominal * vaFactor * gridFactor * heaterFactor * screenFactor;
        var isCurrent = Math.Max(0, profile.NominalScreenCurrentMa * vaFactor * gridFactor * heaterFactor * screenFactor);
        return new Reading(ia, isCurrent, target.Va, target.Vs, "EMULATOR — DANE SYNTETYCZNE");
    }

    private static void ValidateMeasuredPower(TubeProfile profile, ReferenceMeasurementRequest request, Target target, Reading reading)
    {
        if (reading.Ia > request.ComplianceMa * 0.95 || reading.Is > request.ComplianceMa * 0.95)
            throw new InvalidOperationException("Prąd osiągnął 95% ustawionego compliance; skan przerwany.");
        if (profile.MaxAnodePowerW > 0 && reading.Va * reading.Ia / 1000.0 > profile.MaxAnodePowerW * 0.95)
            throw new InvalidOperationException("Moc anody osiągnęła 95% limitu katalogowego; skan przerwany.");
        if (profile.MaxScreenPowerW > 0 && reading.Vs * reading.Is / 1000.0 > profile.MaxScreenPowerW * 0.95)
            throw new InvalidOperationException("Moc siatki ekranowej osiągnęła 95% limitu katalogowego; skan przerwany.");
    }

    private static async Task RampHeaterAsync(
        double target,
        double supply,
        ITracerTransport transport,
        ReferenceMeasurementRequest request,
        IProgress<ReferenceMeasurementProgress>? progress,
        int total,
        CancellationToken cancellationToken)
    {
        for (var step = 1; step <= 20; step++)
        {
            var voltage = target * step / 20.0;
            await transport.SendFilamentCodeAsync(CommandCodeConverter.HeaterCode(voltage, supply), cancellationToken);
            Report(progress, $"Rampa żarzenia {voltage:F2}/{target:F2} V.", 2 + step * 0.35, 0, total);
            await DelayAsync(500, transport.IsEmulator, cancellationToken);
        }
    }

    private static async Task SafeShutdownAsync(
        ITracerTransport transport,
        double highestVoltage,
        bool emulator,
        IProgress<ReferenceMeasurementProgress>? progress)
    {
        try { await transport.SendFilamentCodeAsync(0, CancellationToken.None); } catch { }
        try { await transport.SendEndMeasurementAsync(CancellationToken.None); } catch { }
        var seconds = DischargeTimeCalculator.CalculateSeconds(highestVoltage);
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            Report(progress, $"Rozładowanie: {remaining} s.", 93 + 6.0 * (seconds - remaining) / Math.Max(1, seconds), 0, 0);
            await DelayAsync(1000, emulator, CancellationToken.None);
        }
    }

    private static byte AverageCode(int index) => index switch
    {
        0 => 64,
        1 => 1,
        2 => 2,
        3 => 4,
        4 => 8,
        5 => 16,
        6 => 32,
        7 => 64,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static Task DelayAsync(int milliseconds, bool emulator, CancellationToken cancellationToken) =>
        Task.Delay(emulator ? Math.Max(5, milliseconds / 100) : milliseconds, cancellationToken);

    private static void Report(IProgress<ReferenceMeasurementProgress>? progress, string message,
        double percent, int current, int total) =>
        progress?.Report(new ReferenceMeasurementProgress(message, Math.Clamp(percent, 0, 100), current, total));
}
