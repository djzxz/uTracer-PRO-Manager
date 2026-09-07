using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;
using uTracerProManager.Core.Safety;

namespace uTracerProManager.Core.Services;

public sealed class ReferenceMeasurementController
{
    private sealed record Reading(double Ia, double Is, double Va, double Vs, string Status);

    private readonly SafetyValidator _safety = new();

    public async Task<ReferenceMeasurementResult> RunAsync(
        TubeProfile profile,
        ITracerTransport transport,
        CalibrationProfile calibration,
        HardwareCapabilities hardware,
        ReferenceMeasurementRequest request,
        IProgress<ReferenceMeasurementProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IProgress<ReferenceMeasurementPoint>? sampleProgress = null)
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

        var plan = ReferenceMeasurementPlanValidator.BuildAndValidate(profile, hardware, calibration, request);
        var targets = plan.Targets;
        var startedAt = DateTimeOffset.Now;
        var points = new List<ReferenceMeasurementPoint>(targets.Count);
        var configured = false;
        var highestVoltage = targets.Max(target => Math.Max(target.Va, target.Vs));
        ReferenceMeasurementSampleBus.Start();

        try
        {
            Report(progress, plan.Summary, 0.5, 0, targets.Count);
            Report(progress, "Kontrola zasilania i przygotowanie skanu.", 1, 0, targets.Count);
            var supplyReading = await transport.ReadAdcAsync(calibration, new AdcConversionOptions(), cancellationToken);
            var supply = transport.IsEmulator ? 19.2 :
                supplyReading.Engineering?.SupplyVoltage ?? throw new InvalidOperationException("Nie odczytano Vsu.");
            if (supply is < 10 or > 25)
                throw new InvalidOperationException($"Vsu={supply:F2} V jest poza zakresem 10–25 V.");

            await transport.SendFilamentCodeAsync(0, cancellationToken);
            await transport.SendStartMeasurementAsync(
                plan.HardwareComplianceCode,
                AverageCode(request.AveragingIndex),
                plan.ScreenGainCode,
                plan.AnodeGainCode,
                cancellationToken);
            configured = true;

            var firstHeater = request.ExternalHeater ? 0 : targets[0].Vh;
            if (!request.ExternalHeater)
                await RampHeaterAsync(firstHeater, supply, transport, request, progress, targets.Count, cancellationToken);
            await DelayAsync(request.WarmupSeconds * 1000, transport.IsEmulator, cancellationToken);

            double previousHeater = firstHeater;
            for (var index = 0; index < targets.Count; index++)
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

                ReferenceMeasurementPlanValidator.ValidateMeasuredPoint(
                    profile, plan, target, reading.Va, reading.Vs, reading.Ia, reading.Is);

                var point = new ReferenceMeasurementPoint(
                    target.Sequence,
                    target.CurveIndex,
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
                    reading.Status);

                points.Add(point);
                sampleProgress?.Report(point);
                ReferenceMeasurementSampleBus.Publish(point);
                Report(progress,
                    $"Punkt {index + 1}/{targets.Count}: X={target.X:F3}, krok={target.Step:F3}.",
                    10 + 82.0 * (index + 1) / targets.Count, index + 1, targets.Count);
            }

            await SafeShutdownAsync(transport, highestVoltage, transport.IsEmulator, progress);
            configured = false;
            Report(progress, "Skan zakończony.", 100, targets.Count, targets.Count);
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
        finally
        {
            ReferenceMeasurementSampleBus.Complete();
        }
    }

    private static async Task<Reading> MeasureCorrectedAsync(
        TubeProfile profile,
        ITracerTransport transport,
        CalibrationProfile calibration,
        double supply,
        ReferenceMeasurementTarget target,
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
            var vaProfileLimit = profile.MaxAnodeVoltage > 0 ? profile.MaxAnodeVoltage : calibration.MaxAnodeVoltage;
            var vaCorrection = correction.Correct(target.Va, commandVa, engineering.EstimatedAnodeVoltage,
                Math.Min(vaProfileLimit, calibration.MaxAnodeVoltage));

            var vsLimit = profile.IsDualTriode
                ? Math.Min(profile.MaxAnodeVoltage > 0 ? profile.MaxAnodeVoltage : calibration.MaxAnodeVoltage, calibration.MaxAnodeVoltage)
                : profile.MaxScreenVoltage > 0
                    ? Math.Min(profile.MaxScreenVoltage, calibration.MaxAnodeVoltage)
                    : calibration.MaxAnodeVoltage;
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

    private static Reading Emulate(TubeProfile profile, ReferenceMeasurementTarget target)
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
        0 => 0x40,
        1 => 1,
        2 => 2,
        3 => 4,
        4 => 8,
        5 => 16,
        6 => 32,
        7 => 0x40,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static Task DelayAsync(int milliseconds, bool emulator, CancellationToken cancellationToken) =>
        Task.Delay(emulator ? Math.Max(5, milliseconds / 100) : milliseconds, cancellationToken);

    private static void Report(IProgress<ReferenceMeasurementProgress>? progress, string message,
        double percent, int current, int total) =>
        progress?.Report(new ReferenceMeasurementProgress(message, Math.Clamp(percent, 0, 100), current, total));
}
