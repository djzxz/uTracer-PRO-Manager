using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;

namespace uTracerProManager.Core.Services;

/// <summary>
/// Rozwija pełny plan skanu i zatwierdza go ZANIM do sprzętu zostanie wysłana
/// komenda rozpoczęcia pomiaru. Walidacja jest wykonywana na każdym punkcie,
/// a sprzętowy compliance jest zawsze dobierany w dół do najbezpieczniejszego
/// dopuszczalnego progu całej sesji.
/// </summary>
public static class ReferenceMeasurementPlanValidator
{
    private const double Guard = 0.95;

    public static ReferenceMeasurementPlan BuildAndValidate(
        TubeProfile profile,
        HardwareCapabilities hardware,
        CalibrationProfile calibration,
        ReferenceMeasurementRequest request)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (!profile.ApprovedForHardware)
            throw new InvalidOperationException("Dane profilu nie mają stanu READY. Najpierw zakończ weryfikację profilu.");
        if (!hardware.SupportsCurrentProtocol)
            throw new NotSupportedException($"{hardware.DisplayName}: dostępny jest filtr katalogu, ale adapter rzeczywistego pomiaru nie jest zatwierdzony.");
        if (profile.IsBlockedForSelectedHardware)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(profile.HardwareCompatibilityReason)
                ? "Profil nie jest zgodny z wybranym wariantem sprzętu."
                : profile.HardwareCompatibilityReason);

        if (profile.RequiresExternalHeater && !request.ExternalHeater)
            throw new InvalidOperationException("Ten profil wymaga zewnętrznego zasilacza żarzenia. Wewnętrzne żarzenie jest zablokowane.");
        if (request.ExternalHeater && !request.ExternalHeaterSupplyConfirmed)
            throw new InvalidOperationException("Nie potwierdzono podłączenia i ustawienia zewnętrznego zasilacza żarzenia.");

        var definition = ReferenceMeasurementDefinition.For(request.Kind);
        if (definition.PositiveGridMode && !request.SpecialWiringConfirmed)
            throw new InvalidOperationException("Tryb +Vg wymaga połączenia siatki lampy z wyjściem SCREEN i potwierdzenia okablowania.");

        var targets = BuildTargets(request).ToArray();
        if (targets.Length == 0)
            throw new InvalidOperationException("Plan skanu nie zawiera punktów.");

        var anodeLimit = ValidateRequestedLimit(
            "anody", request.EffectiveAnodeComplianceMa, profile.AnodeComplianceMa, hardware.MaxPulseCurrentMa);
        var screenProfileLimit = profile.IsDualTriode && profile.ScreenComplianceMa <= 0
            ? profile.AnodeComplianceMa
            : profile.ScreenComplianceMa;
        var screenLimit = ValidateRequestedLimit(
            definition.PositiveGridMode ? "prądu siatki (+Vg)" : "ekranu",
            request.EffectiveScreenComplianceMa,
            screenProfileLimit > 0 ? screenProfileLimit : request.EffectiveScreenComplianceMa,
            hardware.MaxPulseCurrentMa);

        var maxVa = Math.Min(hardware.MaxAnodeVoltage, calibration.MaxAnodeVoltage);
        var maxVs = Math.Min(hardware.MaxScreenVoltage, calibration.MaxAnodeVoltage);
        var safeHardwareCeiling = Math.Min(anodeLimit, hardware.MaxPulseCurrentMa);

        foreach (var target in targets)
        {
            ValidateVoltageTarget(profile, hardware, request, target, maxVa, maxVs);

            var anodePowerLimit = profile.MaxAnodePowerW;
            var safeIaAtPoint = CurrentAllowedByPower(anodeLimit, anodePowerLimit, target.Va);
            safeHardwareCeiling = Math.Min(safeHardwareCeiling, safeIaAtPoint);

            if (UsesScreenCurrent(profile, target))
            {
                var screenPowerLimit = profile.IsDualTriode ? profile.MaxAnodePowerW : profile.MaxScreenPowerW;
                var safeIsAtPoint = CurrentAllowedByPower(screenLimit, screenPowerLimit, target.Vs);
                safeHardwareCeiling = Math.Min(safeHardwareCeiling, safeIsAtPoint);
            }
        }

        var hardwareComplianceMa = CurrentLimitCodes.FloorMilliAmps(safeHardwareCeiling);
        var hardwareComplianceCode = CurrentLimitCodes.ForMilliAmps(hardwareComplianceMa);
        var screenGain = RangeCode(request.ScreenRangeIndex);
        var anodeGain = RangeCode(request.AnodeRangeIndex);

        return new ReferenceMeasurementPlan(
            targets,
            anodeLimit,
            screenLimit,
            hardwareComplianceMa,
            hardwareComplianceCode,
            screenGain,
            anodeGain,
            MeasurementPlanReadiness.Validated,
            $"PLAN OK: {targets.Length} pkt • Ia≤{anodeLimit:F0} mA • Is≤{screenLimit:F0} mA • sprzętowy compliance {hardwareComplianceMa} mA (zaokrąglenie w dół)" );
    }

    public static void ValidateMeasuredPoint(
        TubeProfile profile,
        ReferenceMeasurementPlan plan,
        ReferenceMeasurementTarget target,
        double measuredVa,
        double measuredVs,
        double iaMa,
        double isMa)
    {
        if (iaMa > plan.AnodeSoftwareLimitMa * Guard)
            throw new InvalidOperationException($"Ia={iaMa:F2} mA osiągnęło 95% limitu anody {plan.AnodeSoftwareLimitMa:F0} mA.");

        if (UsesScreenCurrent(profile, target) && isMa > plan.ScreenSoftwareLimitMa * Guard)
            throw new InvalidOperationException($"Is={isMa:F2} mA osiągnęło 95% limitu ekranu/siatki {plan.ScreenSoftwareLimitMa:F0} mA.");

        if (profile.MaxAnodePowerW > 0 && measuredVa * iaMa / 1000.0 > profile.MaxAnodePowerW * Guard)
            throw new InvalidOperationException("Moc anody po korekcji napięcia osiągnęła 95% limitu katalogowego.");

        var screenPowerLimit = profile.IsDualTriode ? profile.MaxAnodePowerW : profile.MaxScreenPowerW;
        if (UsesScreenCurrent(profile, target) && screenPowerLimit > 0 &&
            measuredVs * isMa / 1000.0 > screenPowerLimit * Guard)
            throw new InvalidOperationException(profile.IsDualTriode
                ? "Moc sekcji B po korekcji napięcia osiągnęła 95% limitu katalogowego."
                : "Moc siatki ekranowej po korekcji napięcia osiągnęła 95% limitu katalogowego.");
    }

    public static IReadOnlyList<ReferenceMeasurementTarget> BuildTargets(ReferenceMeasurementRequest request)
    {
        var xs = BuildAxis(request.XStart, request.XStop, request.Intervals, request.LogarithmicX);
        var maxVa = request.Kind switch
        {
            ReferenceMeasurementKind.GridSweepSteppedAnodeUltraLinear => request.SteppingValues.Max(),
            ReferenceMeasurementKind.AnodeSweepSteppedGridUltraLinear => xs.Max(),
            _ => Math.Max(request.ConstantVa, xs.Max())
        };

        var result = new List<ReferenceMeasurementTarget>();
        var sequence = 0;
        for (var curveIndex = 0; curveIndex < request.SteppingValues.Count; curveIndex++)
        {
            var step = request.SteppingValues[curveIndex];
            foreach (var x in xs)
            {
                sequence++;
                var positiveGrid = request.Kind is ReferenceMeasurementKind.PositiveGridSweepSteppedAnode
                    or ReferenceMeasurementKind.AnodeSweepSteppedPositiveGrid;
                var values = request.Kind switch
                {
                    ReferenceMeasurementKind.GridSweepSteppedAnode => (Va: step, Vs: request.ConstantVs, Vg: x, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.GridSweepSteppedTiedAnodeScreen => (Va: step, Vs: step, Vg: x, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.AnodeSweepSteppedGrid => (Va: x, Vs: request.ConstantVs, Vg: step, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.AnodeSweepSteppedScreen => (Va: x, Vs: step, Vg: request.ConstantVg, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.TiedAnodeScreenSweepSteppedGrid => (Va: x, Vs: x, Vg: step, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.ScreenSweepSteppedGrid => (Va: request.ConstantVa, Vs: x, Vg: step, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.PositiveGridSweepSteppedAnode => (Va: step, Vs: x, Vg: 0d, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.AnodeSweepSteppedPositiveGrid => (Va: x, Vs: step, Vg: 0d, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.HeaterSweepSteppedGrid => (Va: request.ConstantVa, Vs: request.ConstantVs, Vg: step, Vh: x),
                    ReferenceMeasurementKind.HeaterSweepSteppedAnode => (Va: step, Vs: request.ConstantVs, Vg: request.ConstantVg, Vh: x),
                    ReferenceMeasurementKind.GridSweepSteppedAnodeUltraLinear =>
                        (Va: step, Vs: UltraLinearScreen(step, maxVa, request.UltraLinearKPercent), Vg: x, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.AnodeSweepSteppedGridUltraLinear =>
                        (Va: x, Vs: UltraLinearScreen(x, maxVa, request.UltraLinearKPercent), Vg: step, Vh: request.ConstantVh),
                    ReferenceMeasurementKind.AnodeSweepSteppedGridSchadeFeedback =>
                        (Va: x, Vs: request.ConstantVs, Vg: SchadeGrid(step, x, request.SchadeFeedbackPercent), Vh: request.ConstantVh),
                    _ => throw new ArgumentOutOfRangeException(nameof(request.Kind))
                };
                result.Add(new ReferenceMeasurementTarget(sequence, curveIndex, x, step,
                    values.Va, values.Vs, values.Vg, values.Vh, positiveGrid));
            }
        }
        return result;
    }

    private static double ValidateRequestedLimit(string electrode, double requested, double profileLimit, double hardwareLimit)
    {
        if (requested <= 0)
            throw new InvalidOperationException($"Brak dodatniego limitu prądowego dla {electrode}.");
        if (profileLimit > 0 && requested > profileLimit + 0.001)
            throw new InvalidOperationException($"Wybrany limit {electrode} {requested:F0} mA przekracza limit profilu {profileLimit:F0} mA.");
        if (requested > hardwareLimit + 0.001)
            throw new InvalidOperationException($"Wybrany limit {electrode} {requested:F0} mA przekracza limit sprzętu {hardwareLimit:F0} mA.");
        return Math.Min(requested, Math.Min(profileLimit > 0 ? profileLimit : requested, hardwareLimit));
    }

    private static void ValidateVoltageTarget(
        TubeProfile profile,
        HardwareCapabilities hardware,
        ReferenceMeasurementRequest request,
        ReferenceMeasurementTarget target,
        double maxVa,
        double maxVs)
    {
        if (target.Va is < 0 || target.Va > maxVa)
            throw new InvalidOperationException($"Punkt {target.Sequence}: Va={target.Va:F2} V przekracza zakres sprzętu/kalibracji.");
        if (target.Vs is < 0 || target.Vs > maxVs)
            throw new InvalidOperationException($"Punkt {target.Sequence}: Vs={target.Vs:F2} V przekracza zakres sprzętu/kalibracji.");
        if (target.Vg < hardware.MinGridVoltage || target.Vg > 0)
            throw new InvalidOperationException($"Punkt {target.Sequence}: Vg={target.Vg:F3} V jest poza zakresem {hardware.MinGridVoltage:F0}…0 V.");
        if (!request.ExternalHeater && (target.Vh <= 0 || target.Vh > 24))
            throw new InvalidOperationException($"Punkt {target.Sequence}: Vh={target.Vh:F2} V jest poza bezpiecznym zakresem wewnętrznego sterownika.");
        if (profile.MaxAnodeVoltage > 0 && target.Va > profile.MaxAnodeVoltage)
            throw new InvalidOperationException($"Punkt {target.Sequence}: Va={target.Va:F1} V przekracza katalogowe Va max.");
        if (profile.MaxScreenVoltage > 0 && !profile.IsDualTriode && target.Vs > profile.MaxScreenVoltage)
            throw new InvalidOperationException($"Punkt {target.Sequence}: Vs={target.Vs:F1} V przekracza katalogowe Vs max.");
    }

    private static double CurrentAllowedByPower(double softwareLimitMa, double maxPowerW, double voltage)
    {
        if (maxPowerW <= 0 || voltage <= 0)
            return softwareLimitMa;
        return Math.Min(softwareLimitMa, Guard * maxPowerW * 1000.0 / voltage);
    }

    private static bool UsesScreenCurrent(TubeProfile profile, ReferenceMeasurementTarget target) =>
        target.Vs > 0.001 && (profile.IsDualTriode || target.PositiveGridViaScreen || profile.MaxScreenVoltage > 0 || profile.NominalScreenCurrentMa > 0);

    private static byte RangeCode(int index) => index switch
    {
        0 => 0x08,
        >= 1 and <= 8 => (byte)(index - 1),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

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
}
