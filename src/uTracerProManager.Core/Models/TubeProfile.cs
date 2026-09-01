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

	public string HeaterSupplyMode { get; init; } = "INTERNAL_OK";

	public string HeaterSupplyNote { get; init; } = string.Empty;

	public bool RequiresExternalHeater =>
		string.Equals(HeaterSupplyMode, "EXTERNAL_DC_REQUIRED", StringComparison.OrdinalIgnoreCase);

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

	public bool IsBlockedForSelectedHardware =>
		!ApprovedForHardware ||
		string.Equals(HardwareCompatibilityStatus, "BLOCKED", StringComparison.OrdinalIgnoreCase);

	public bool RequiresHardwareModification =>
		string.Equals(HardwareCompatibilityStatus, "REQUIRES_MODIFICATION", StringComparison.OrdinalIgnoreCase);

	public string ListForeground => IsBlockedForSelectedHardware ? "#C62828" :
		RequiresHardwareModification ? "#9A5A00" : "#17395C";

	public bool IsDualTriode
	{
		get
		{
			if (!Family.Contains("Podwójna trioda", StringComparison.OrdinalIgnoreCase) && !Family.Contains("dual triode", StringComparison.OrdinalIgnoreCase))
			{
				if (TubeTypes.Contains("ECC", StringComparison.OrdinalIgnoreCase))
				{
					return Pinout.Contains("Połówka B", StringComparison.OrdinalIgnoreCase);
				}
				return false;
			}
			return true;
		}
	}

	public string ApprovalLabel
	{
		get
		{
			if (!IsUserDefined)
			{
				if (!ApprovedForHardware)
				{
					return "PROFIL SPECJALNY / ZABLOKOWANY";
				}
				return IsBlockedForSelectedHardware
					? "NIEOBSŁUGIWANY — ZABLOKOWANY"
					: RequiresHardwareModification
						? "WYMAGA MODYFIKACJI SPRZĘTU"
						: string.IsNullOrWhiteSpace(HardwareCompatibilityLabel)
							? "GOTOWY DO UŻYCIA"
							: HardwareCompatibilityLabel;
			}
			return "PROFIL RĘCZNY UŻYTKOWNIKA";
		}
	}

	public string ConditionLabel
	{
		get
		{
			if (!CountsForConditionPercent)
			{
				return "TYLKO PORÓWNANIE";
			}
			return "LICZY KONDYCJĘ";
		}
	}

	public override string ToString()
	{
		return DisplayName;
	}
}
