namespace uTracerProManager.Core.Models;

public sealed record CalibrationProfile
{
    public string DeviceName { get; init; } = "uTracer 3+";
    public string SourcePath { get; init; } = string.Empty;
    public DateTimeOffset? ImportedAt { get; init; }
    public bool ImportedFromFile { get; init; }
    public string PortName { get; init; } = string.Empty;

    public string CalibrationVersion { get; init; } = "2.0";
    public DateTimeOffset? CalibrationCompletedAt { get; init; }

    public double VaFactor { get; init; } = 1.0;
    public double VsFactor { get; init; } = 1.0;
    public double IaFactor { get; init; } = 1.0;
    public double IsFactor { get; init; } = 1.0;
    public double VsuFactor { get; init; } = 1.0;
    public double Vg1Factor { get; init; } = 1.0;
    public double Vg4Factor { get; init; } = 1.0;
    public double Vg40Factor { get; init; } = 1.0;
    public double OriginalVsatFactor { get; init; } = 1.0;
    public double OriginalSpareFactor { get; init; } = 1.0;
    public int OriginalHardwareVersion { get; init; } = 2;
    public double GridOffsetV { get; init; }
    public double GridSlope { get; init; } = 1.0;
    public string GridCalibrationModel { get; init; } = "offset-slope";
    public double VnFactor { get; init; } = 1.0;

    public double AnodeDividerOhm { get; init; } = 5230;
    public double AnodeSenseOhm { get; init; } = 18;
    public double ScreenSenseOhm { get; init; } = 18;
    public double MaxAnodeVoltage { get; init; } = 400;
    public double MaxAnodeCurrentMa { get; init; } = 200;
    public double MaxScreenCurrentMa { get; init; } = 200;
    public double MaxGridMagnitudeV { get; init; } = 50;

    public double VadcOffsetV { get; init; }
    public double ExtraSeriesResistanceOhm { get; init; }

    public bool SupplyCalibrationVerified { get; init; }
    public bool NegativeSupplyCalibrationVerified { get; init; }
    public bool GridCalibrationVerified { get; init; }
    public bool GridOffsetSlopeVerified { get; init; }
    public bool VoltageCalibrationVerified { get; init; }
    public bool CurrentCalibrationVerified { get; init; }

    public bool IsValidForHardwareDiagnostics => Validate().Count == 0;

    public bool IsCompleteForTubeTesting =>
        IsValidForHardwareDiagnostics &&
        CalibrationVersion == "2.0" &&
        SupplyCalibrationVerified &&
        NegativeSupplyCalibrationVerified &&
        GridCalibrationVerified &&
        GridOffsetSlopeVerified &&
        GridCalibrationModel == "offset-slope" &&
        VoltageCalibrationVerified &&
        CurrentCalibrationVerified;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!ImportedFromFile)
            errors.Add("Nie zapisano profilu kalibracji.");

        ValidateFactor(VaFactor, nameof(VaFactor), errors);
        ValidateFactor(VsFactor, nameof(VsFactor), errors);
        ValidateFactor(IaFactor, nameof(IaFactor), errors);
        ValidateFactor(IsFactor, nameof(IsFactor), errors);
        ValidateFactor(VsuFactor, nameof(VsuFactor), errors);
        ValidateFactor(Vg1Factor, nameof(Vg1Factor), errors);
        ValidateFactor(Vg4Factor, nameof(Vg4Factor), errors);
        ValidateFactor(Vg40Factor, nameof(Vg40Factor), errors);
        ValidateFactor(OriginalVsatFactor, nameof(OriginalVsatFactor), errors);
        ValidateFactor(OriginalSpareFactor, nameof(OriginalSpareFactor), errors);
        ValidateFactor(VnFactor, nameof(VnFactor), errors);

        if (OriginalHardwareVersion is < 1 or > 2)
            errors.Add("Wersja sprzętu oryginalnego GUI musi wynosić 1 (uTracer 3) albo 2 (uTracer 3+).");

        if (!double.IsFinite(GridOffsetV) || GridOffsetV is < -5 or > 5)
            errors.Add("Vg offset musi mieścić się w zakresie -5…+5 V.");

        ValidateFactor(GridSlope, nameof(GridSlope), errors);

        if (GridCalibrationModel is not ("offset-slope" or "legacy-three-point"))
            errors.Add("Nieznany model kalibracji siatki.");

        if (AnodeDividerOhm is < 1000 or > 50000)
            errors.Add("RaVal musi mieścić się w zakresie 1–50 kΩ.");
        if (AnodeSenseOhm is < 1 or > 500)
            errors.Add("Rezystor pomiarowy anody jest poza zakresem 1–500 Ω.");
        if (ScreenSenseOhm is < 1 or > 500)
            errors.Add("Rezystor pomiarowy ekranu jest poza zakresem 1–500 Ω.");
        if (MaxAnodeVoltage is < 50 or > 425)
            errors.Add("VaMax musi mieścić się w zakresie 50–425 V.");
        if (MaxAnodeCurrentMa is < 25 or > 400)
            errors.Add("IaMax musi mieścić się w zakresie 25–400 mA.");
        if (MaxScreenCurrentMa is < 25 or > 400)
            errors.Add("IsMax musi mieścić się w zakresie 25–400 mA.");
        if (MaxGridMagnitudeV is < 30 or > 100)
            errors.Add("VgMax musi mieścić się w zakresie 30–100 V.");

        return errors;
    }

    private static void ValidateFactor(double value, string name, List<string> errors)
    {
        if (!double.IsFinite(value) || value is < 0.4 or > 1.6)
            errors.Add($"{name}={value} jest poza zakresem 0,4–1,6.");
    }
}
