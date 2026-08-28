namespace uTracerProManager.Core.Models;

public sealed record TubeMeasurementDecision(TubeMeasurementAvailability Availability, string Label, string Reason)
{
	public bool CanStartMeasurement => Availability == TubeMeasurementAvailability.VerifiedReady;

	public string FilterKey => Availability switch
	{
		TubeMeasurementAvailability.VerifiedReady => "ready", 
		TubeMeasurementAvailability.VerifiedBlocked => "blocked", 
		TubeMeasurementAvailability.NotMeasurable => "blocked", 
		_ => "pending", 
	};
}
