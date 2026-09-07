using System;

namespace uTracerProManager.Core.Models;

public sealed class TubeProfile
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Family { get; init; }
    public string[] Aliases { get; init; } = Array.Empty<string>();
    public string TubeTypes { get; init; } = string.Empty;
    public string ManufacturerScope { get; init; } = string.Empty;
    public required string Pinout { get; init; }
    public string CriticalWarning { get; init; } = string.Empty;
    public double HeaterVoltage { get; init; }
    public double HeaterCurrentAmp { get; init; }
    public double AnodeVoltage { get; init; }
    public double ScreenVoltage { get; init; }
    public double GridVoltage { get; init; }
    public double NominalAnodeCurrentMa { get; init; }
    public double NominalScreenCurrentMa { get; init; }
    public double NominalGmMaV { get; init; }
    public double NominalMu { get; init; }
    public double NominalRpKohm { get; init; }
    public double MaxAnodeVoltage { get; init; }
    public double MaxScreenVoltage { get; init; }
    public double MaxAnodePowerW { get; init; }
    public double MaxScreenPowerW { get; init; }
    public double AnodeComplianceMa { get; init; }
    public double ScreenComplianceMa { get; init; }
    public int WarmupSeconds { get; init; } = 60;
    public string MeasurementPurpose { get; init; } = "Punkt katalogowy";
    public string SourceTitle { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string SourcePage { get; init; } = string.Empty;
    public string ExtractionStatus { get; init; } = string.Empty;
    public bool ApprovedForHardware { get; init; } = true;
    public bool CountsForConditionPercent { get; init; } = true;
    public bool IsUserDefined { get; init; }
    public double CurveVaStartV { get; init; }
    public double CurveVaStopV { get; init; }
    public double CurveVaStepV { get; init; }
    public string CurveGridVoltages { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string CatalogCompatibilityNote { get; init; } = string.Empty;
    public string HardwareCompatibilityStatus { get; set; } = "FULL_CURVE";
    public string HardwareCompatibilityLabel { get; set; } = "GOTOWY";
    public string HardwareCompatibilityReason { get; set; } = string.Empty;
    public double UsableCurveStopV { get; set; }
    public double UsableCurrentMa { get; set; }
    public bool RequiresManualConfirmation { get; set; }

    // Stan danych i stan sprzętu są celowo niezależne. Żaden z nich nie oznacza,
    // że konkretny plan skanu został już zwalidowany.
    public ProfileDataReadiness ProfileDataState =>
        ApprovedForHardware ? ProfileDataReadiness.Ready : ProfileDataReadiness.Blocked;

    public bool RequiresExternalHeater =>
        string.Equals(HardwareCompatibilityStatus, "READY_EXTERNAL_HEATER", StringComparison.OrdinalIgnoreCase);

    public bool IsBlockedForSelectedHardware =>
        !ApprovedForHardware ||
        string.Equals(HardwareCompatibilityStatus, "BLOCKED", StringComparison.OrdinalIgnoreCase);

    public bool RequiresHardwareModification =>
        string.Equals(HardwareCompatibilityStatus, "REQUIRES_MODIFICATION", StringComparison.OrdinalIgnoreCase);

    public HardwareReadiness HardwareState =>
        IsBlockedForSelectedHardware ? HardwareReadiness.Unsupported :
        RequiresHardwareModification || RequiresManualConfirmation || RequiresExternalHeater
            ? HardwareReadiness.RequiresConfirmation
            : HardwareReadiness.Compatible;

    public MeasurementReadiness BuildReadiness(
        MeasurementPlanReadiness planState = MeasurementPlanReadiness.NotValidated,
        string reason = "Plan pomiaru nie został jeszcze zwalidowany.") =>
        new(ProfileDataState, HardwareState, planState, reason);

    public string ReadinessSummary =>
        $"DANE: {(ProfileDataState == ProfileDataReadiness.Ready ? "READY" : "BLOCKED")} • " +
        $"SPRZĘT: {HardwareState} • PLAN: NIEZWALIDOWANY";

    public string ListForeground => IsBlockedForSelectedHardware ? "#C62828" :
        RequiresHardwareModification || RequiresExternalHeater ? "#9A5A00" : "#17395C";

    public bool IsDualTriode
    {
        get
        {
            if (!Family.Contains("Podwójna trioda", StringComparison.OrdinalIgnoreCase) &&
                !Family.Contains("dual triode", StringComparison.OrdinalIgnoreCase))
            {
                if (TubeTypes.Contains("ECC", StringComparison.OrdinalIgnoreCase))
                    return Pinout.Contains("Połówka B", StringComparison.OrdinalIgnoreCase);
                return false;
            }
            return true;
        }
    }

    public string ApprovalLabel
    {
        get
        {
            if (IsUserDefined)
                return "PROFIL RĘCZNY • PLAN WYMAGA WALIDACJI";
            if (!ApprovedForHardware)
                return "DANE BLOCKED • POMIAR ZABLOKOWANY";
            if (IsBlockedForSelectedHardware)
                return "DANE READY • SPRZĘT NIEZGODNY";
            if (RequiresExternalHeater)
                return "DANE READY • SPRZĘT: ZEWNĘTRZNE ŻARZENIE";
            if (RequiresHardwareModification)
                return "DANE READY • SPRZĘT: WYMAGA MODYFIKACJI";
            if (RequiresManualConfirmation)
                return "DANE READY • SPRZĘT: WYMAGA POTWIERDZENIA";
            return "DANE READY • SPRZĘT ZGODNY • PLAN NIEZWALIDOWANY";
        }
    }

    public string ConditionLabel =>
        !CountsForConditionPercent ? "TYLKO PORÓWNANIE" : "LICZY KONDYCJĘ";

    public override string ToString() => DisplayName;
}
