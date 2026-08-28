namespace uTracerProManager.Core.Models;

public sealed record FrankCatalogEntry(string TubeType, string Manufacturer, string SystemCode, string DataSheetUrl, string FileName, string SourcePage)
{
	public bool HasApprovedMeasurementProfile { get; init; }

	public bool HasBlockedMeasurementProfile { get; init; }

	public string DisplayName
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Manufacturer))
			{
				return $"{TubeType} — {Manufacturer} — {FileName}";
			}
			return TubeType + " — " + FileName;
		}
	}

	public TubeMeasurementDecision MeasurementDecision => TubeMeasurementCapabilityClassifier.Classify(this);

	public string MeasurementStatusLabel => MeasurementDecision.Label;

	public string MeasurementStatusReason => MeasurementDecision.Reason;

	public string MeasurementStatusFilterKey => MeasurementDecision.FilterKey;

	public bool CanLoadProfile => MeasurementDecision.CanStartMeasurement;

	public string ListForeground => CanLoadProfile ? "#17395C" : "#C62828";
}
