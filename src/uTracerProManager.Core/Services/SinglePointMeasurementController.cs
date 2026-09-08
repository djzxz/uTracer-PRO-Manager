using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;
using uTracerProManager.Core.Safety;

namespace uTracerProManager.Core.Services;

public sealed class SinglePointMeasurementController
{
    public const int HeaterRampMilliseconds = 10000;
    public const int HeaterRampStepMilliseconds = 500;

    private const double Guard = 0.95;
    private readonly SafetyValidator _safetyValidator;

    public SinglePointMeasurementController(SafetyValidator safetyValidator)
    {
        _safetyValidator = safetyValidator;
    }

    public async Task<MeasurementSessionResult> RunAsync(
        TubeProfile profile,
        ITracerTransport transport,
        CalibrationProfile calibration,
        SinglePointMeasurementOptions options,
        IProgress<MeasurementProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(options);
        if (!transport.IsConnected)
            throw new InvalidOperationException("Transport nie jest połączony.");

        options.Validate(transport.IsEmulator);
        var safety = _safetyValidator.ValidateProfile(profile);
        if (!safety.IsSafe)
            throw new InvalidOperationException("Profil nie przeszedł kontroli bezpieczeństwa:\n- " + string.Join("\n- ", safety.Errors));
        if (!profile.ApprovedForHardware)
            throw new InvalidOperationException("Quick Test wymaga profilu danych READY.");
        if (!transport.IsEmulator && string.Equals(profile.HardwareCompatibilityStatus, "READY_EXTERNAL_HEATER", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ten profil wymaga zewnętrznego żarzenia. Użyj toru pomiarowego z jawnym potwierdzeniem osobnego zasilacza.");
        if (!transport.IsEmulator && !calibration.IsCompleteForTubeTesting)
            throw new InvalidOperationException("Rzeczywisty Quick Test wymaga kompletnej kalibracji v2.");

        var calibrationErrors = calibration.Validate();
        if (calibrationErrors.Count > 0)
            throw new InvalidOperationException("Kalibracja jest nieprawidłowa:\n- " + string.Join("\n- ", calibrationErrors));

        var complianceMa = ResolveSafeCompliance(profile);
        var complianceCode = CurrentLimitCodes.ForMilliAmps(complianceMa);
        var averageCode = AverageCode(options.AveragingIndex);
        var startedAt = DateTimeOffset.Now;
        var measurementConfigured = false;

        try
        {
            Report(progress, MeasurementState.Preflight,
                $"Plan Quick Test OK: sprzętowy compliance {complianceMa} mA (zaokrąglenie w dół).", 2);
            cancellationToken.ThrowIfCancellationRequested();

            Report(progress, MeasurementState.ReadingSupply, "Odczyt napięcia zasilającego Vsu.", 5);
            var idle = await transport.ReadAdcAsync(calibration, new AdcConversionOptions(), cancellationToken);
            var supplyVoltage = transport.IsEmulator
                ? idle.Engineering?.SupplyVoltage ?? 19.0
                : (idle.Engineering ?? throw new InvalidOperationException("Nie udało się przeliczyć napięcia Vsu.")).SupplyVoltage;
            if (supplyVoltage is < 10 or > 25)
                throw new InvalidOperationException($"Vsu={supplyVoltage:F2} V jest poza zakresem 10–25 V.");

            var anodeCode = CommandCodeConverter.AnodeCode(profile.AnodeVoltage, supplyVoltage, calibration);
            var screenCode = profile.ScreenVoltage > 0
                ? CommandCodeConverter.ScreenCode(profile.ScreenVoltage, supplyVoltage, calibration)
                : (ushort)0;
            var gridCode = transport.IsEmulator ? (ushort)0 : CommandCodeConverter.GridCode(profile.GridVoltage, calibration);
            var fullHeaterCode = CommandCodeConverter.HeaterCode(profile.HeaterVoltage, supplyVoltage);

            Report(progress, MeasurementState.Configuring,
                $"Konfiguracja pomiaru; compliance {complianceMa} mA; PGA Ia/Is AUTO.", 8);
            await transport.SendFilamentCodeAsync(0, cancellationToken);
            await transport.SendStartMeasurementAsync(complianceCode, averageCode, 0x08, 0x08, cancellationToken);
            measurementConfigured = true;

            await RunHeaterRampAsync(profile, supplyVoltage, transport, options, progress, cancellationToken);
            await RunWarmupAsync(options, transport.IsEmulator, progress, cancellationToken);
            Report(progress, MeasurementState.Measuring, "Wykonywanie pojedynczego impulsu pomiarowego.", 85);
            if (options.HoldMilliseconds > 0)
                await DelayScaledAsync(options.HoldMilliseconds, transport.IsEmulator, options, cancellationToken);

            var finalMeasurement = transport.IsEmulator
                ? await transport.RunEmulatedMeasurementAsync(profile, cancellationToken)
                : await transport.ExecuteMeasurementAsync(
                    anodeCode,
                    screenCode,
                    gridCode,
                    fullHeaterCode,
                    calibration,
                    new AdcConversionOptions(options.AveragingIndex, profile.AnodeVoltage, profile.ScreenVoltage, profile.GridVoltage),
                    cancellationToken);

            if (finalMeasurement.CurrentLimitHit)
                throw new CurrentLimitException("uTracer zgłosił status 11 — zadziałało ograniczenie prądowe.");

            ValidateResultAgainstProfile(profile, finalMeasurement);
            var dischargeSeconds = DischargeTimeCalculator.CalculateSeconds(Math.Max(profile.AnodeVoltage, profile.ScreenVoltage));
            await SafeShutdownAsync(transport, dischargeSeconds, options, progress, CancellationToken.None);
            measurementConfigured = false;
            Report(progress, MeasurementState.Completed, "Quick Test zakończony bez zadziałania limitu.", 100);
            return new MeasurementSessionResult(
                profile, finalMeasurement, startedAt, DateTimeOffset.Now,
                options.WarmupSeconds, dischargeSeconds, transport.IsEmulator);
        }
        catch (OperationCanceledException)
        {
            Report(progress, MeasurementState.Aborted, "Pomiar przerwany przez operatora.", 0);
            if (measurementConfigured)
            {
                var dischargeSeconds = DischargeTimeCalculator.CalculateSeconds(Math.Max(profile.AnodeVoltage, profile.ScreenVoltage));
                await SafeShutdownAsync(transport, dischargeSeconds, options, progress, CancellationToken.None);
            }
            throw;
        }
        catch
        {
            Report(progress, MeasurementState.Faulted, "Błąd pomiaru — uruchamianie bezpiecznego wyłączenia.", 0);
            if (measurementConfigured)
            {
                var dischargeSeconds = DischargeTimeCalculator.CalculateSeconds(Math.Max(profile.AnodeVoltage, profile.ScreenVoltage));
                await SafeShutdownAsync(transport, dischargeSeconds, options, progress, CancellationToken.None);
            }
            throw;
        }
    }

    private static int ResolveSafeCompliance(TubeProfile profile)
    {
        if (profile.AnodeComplianceMa <= 0)
            throw new InvalidOperationException("Profil nie ma dodatniego limitu Ia.");

        var ceiling = Math.Min(200.0, profile.AnodeComplianceMa);
        if (profile.MaxAnodePowerW > 0 && profile.AnodeVoltage > 0)
            ceiling = Math.Min(ceiling, Guard * profile.MaxAnodePowerW * 1000.0 / profile.AnodeVoltage);

        var screenUsed = profile.ScreenVoltage > 0 &&
                         (profile.ScreenComplianceMa > 0 || profile.NominalScreenCurrentMa > 0 || profile.IsDualTriode);
        if (screenUsed)
        {
            var screenLimit = profile.ScreenComplianceMa > 0 ? profile.ScreenComplianceMa : profile.AnodeComplianceMa;
            ceiling = Math.Min(ceiling, screenLimit);
            var screenPower = profile.IsDualTriode ? profile.MaxAnodePowerW : profile.MaxScreenPowerW;
            if (screenPower > 0 && profile.ScreenVoltage > 0)
                ceiling = Math.Min(ceiling, Guard * screenPower * 1000.0 / profile.ScreenVoltage);
        }

        return CurrentLimitCodes.FloorMilliAmps(ceiling);
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

    private static async Task RunHeaterRampAsync(
        TubeProfile profile,
        double supplyVoltage,
        ITracerTransport transport,
        SinglePointMeasurementOptions options,
        IProgress<MeasurementProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int steps = 20;
        for (var step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetVoltage = profile.HeaterVoltage * step / steps;
            var filamentCode = CommandCodeConverter.HeaterCode(targetVoltage, supplyVoltage);
            await transport.SendFilamentCodeAsync(filamentCode, cancellationToken);
            var percent = 10.0 + 25.0 * step / steps;
            Report(progress, MeasurementState.HeaterRamp,
                $"Rampa żarzenia: {targetVoltage:F2}/{profile.HeaterVoltage:F2} V.",
                percent, Remaining(HeaterRampMilliseconds - step * HeaterRampStepMilliseconds, transport.IsEmulator, options));
            await DelayScaledAsync(HeaterRampStepMilliseconds, transport.IsEmulator, options, cancellationToken);
        }
    }

    private static async Task RunWarmupAsync(
        SinglePointMeasurementOptions options,
        bool emulator,
        IProgress<MeasurementProgress>? progress,
        CancellationToken cancellationToken)
    {
        var logicalSeconds = options.WarmupSeconds;
        for (var elapsed = 0; elapsed < logicalSeconds; elapsed++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = logicalSeconds - elapsed;
            var percent = 35.0 + 45.0 * elapsed / logicalSeconds;
            Report(progress, MeasurementState.Warmup,
                $"Rozgrzewanie lampy: pozostało {remaining} s.", percent, remaining);
            await DelayScaledAsync(1000, emulator, options, cancellationToken);
        }
    }

    private static async Task SafeShutdownAsync(
        ITracerTransport transport,
        int dischargeSeconds,
        SinglePointMeasurementOptions options,
        IProgress<MeasurementProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, MeasurementState.HeaterOff, "Wyłączanie żarzenia.", 90);
        try { await transport.SendFilamentCodeAsync(0, cancellationToken); } catch { }
        Report(progress, MeasurementState.EndingMeasurement, "Kończenie sekwencji pomiarowej.", 92);
        try { await transport.SendEndMeasurementAsync(cancellationToken); } catch { }

        for (var remaining = dischargeSeconds; remaining > 0; remaining--)
        {
            Report(progress, MeasurementState.Discharging,
                $"Rozładowanie wysokiego napięcia: {remaining} s.",
                92.0 + 8.0 * (dischargeSeconds - remaining) / Math.Max(1, dischargeSeconds), remaining);
            await DelayScaledAsync(1000, transport.IsEmulator, options, CancellationToken.None);
        }
    }

    private static void ValidateResultAgainstProfile(TubeProfile profile, MeasurementResult result)
    {
        var engineering = result.Engineering;
        if (engineering is null)
            throw new InvalidOperationException("Brak przeliczonych danych ADC po Quick Test.");

        var anodeLimit = profile.AnodeComplianceMa;
        if (anodeLimit > 0 && engineering.AnodeCurrentMa > anodeLimit * Guard)
            throw new InvalidOperationException("Zmierzony Ia osiągnął 95% limitu profilu.");

        var anodePower = engineering.EstimatedAnodeVoltage * engineering.AnodeCurrentMa / 1000.0;
        if (profile.MaxAnodePowerW > 0 && anodePower > profile.MaxAnodePowerW * Guard)
            throw new InvalidOperationException($"Moc anody {anodePower:F2} W osiągnęła 95% limitu {profile.MaxAnodePowerW:F2} W.");

        var screenUsed = profile.ScreenVoltage > 0 &&
                         (profile.ScreenComplianceMa > 0 || profile.NominalScreenCurrentMa > 0 || profile.IsDualTriode);
        if (!screenUsed)
            return;

        var screenLimit = profile.ScreenComplianceMa > 0 ? profile.ScreenComplianceMa : profile.AnodeComplianceMa;
        if (screenLimit > 0 && engineering.ScreenCurrentMa > screenLimit * Guard)
            throw new InvalidOperationException("Zmierzony Is osiągnął 95% limitu profilu.");

        var screenPowerLimit = profile.IsDualTriode ? profile.MaxAnodePowerW : profile.MaxScreenPowerW;
        var screenPower = engineering.MeasuredScreenVoltage * engineering.ScreenCurrentMa / 1000.0;
        if (screenPowerLimit > 0 && screenPower > screenPowerLimit * Guard)
            throw new InvalidOperationException(profile.IsDualTriode
                ? "Moc sekcji B osiągnęła 95% limitu katalogowego."
                : "Moc siatki ekranowej osiągnęła 95% limitu katalogowego.");
    }

    private static int? Remaining(int milliseconds, bool emulator, SinglePointMeasurementOptions options)
    {
        var value = emulator && options.AccelerateEmulator
            ? milliseconds / options.EmulatorSpeedMultiplier
            : milliseconds;
        return Math.Max(0, (int)Math.Ceiling(value / 1000.0));
    }

    private static Task DelayScaledAsync(
        int milliseconds,
        bool emulator,
        SinglePointMeasurementOptions options,
        CancellationToken cancellationToken) =>
        Task.Delay(emulator && options.AccelerateEmulator
            ? Math.Max(10, milliseconds / options.EmulatorSpeedMultiplier)
            : milliseconds, cancellationToken);

    private static void Report(
        IProgress<MeasurementProgress>? progress,
        MeasurementState state,
        string message,
        double percent,
        int? remainingSeconds = null) =>
        progress?.Report(new MeasurementProgress(state, message, Math.Clamp(percent, 0, 100), remainingSeconds));
}
