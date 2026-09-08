namespace uTracerProManager.Core.Models;

public sealed record ProfileEditDraft
{
    public required string ProfileId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string TubeTypes { get; init; } = string.Empty;
    public string ManufacturerScope { get; init; } = string.Empty;
    public string? Pinout { get; init; }
    public string CriticalWarning { get; init; } = string.Empty;
    public double? HeaterVoltage { get; init; }
    public double? HeaterCurrentAmp { get; init; }
    public double? AnodeVoltage { get; init; }
    public double? ScreenVoltage { get; init; }
    public double? GridVoltage { get; init; }
    public double? NominalAnodeCurrentMa { get; init; }
    public double? NominalScreenCurrentMa { get; init; }
    public double? NominalGmMaV { get; init; }
    public double? NominalMu { get; init; }
    public double? NominalRpKohm { get; init; }
    public double? MaxAnodeVoltage { get; init; }
    public double? MaxScreenVoltage { get; init; }
    public double? MaxAnodePowerW { get; init; }
    public double? MaxScreenPowerW { get; init; }
    public double? AnodeComplianceMa { get; init; }
    public double? ScreenComplianceMa { get; init; }
    public int? WarmupSeconds { get; init; }
    public double? CurveVaStartV { get; init; }
    public double? CurveVaStopV { get; init; }
    public double? CurveVaStepV { get; init; }
    public string? CurveGridVoltages { get; init; }
    public string MeasurementPurpose { get; init; } = string.Empty;
    public string SourceTitle { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string SourcePage { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string HeaterSupplyMode { get; init; } = "INTERNAL_OK";
    public string HeaterSupplyNote { get; init; } = string.Empty;
    public bool SourceIdentityConfirmed { get; init; }
    public bool PinoutConfirmed { get; init; }
    public bool HeaterConfirmed { get; init; }
    public bool OperatingPointConfirmed { get; init; }
    public bool LimitsConfirmed { get; init; }
    public bool PowerGuardConfirmed { get; init; }
    public bool HardwareConfirmed { get; init; }
    public string Reviewer { get; init; } = string.Empty;
    public string ReviewNote { get; init; } = string.Empty;
}

public sealed record ProfileEditValidationResult(
    bool IsReady,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string HardwareStatus,
    string HardwareReason)
{
    public string Summary => IsReady
        ? $"READY • {HardwareStatus}" + (Warnings.Count == 0 ? string.Empty : $" • ostrzeżenia: {Warnings.Count}")
        : $"BLOCKED • błędy: {Errors.Count}";
}
