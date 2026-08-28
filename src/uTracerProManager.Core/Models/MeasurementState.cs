namespace uTracerProManager.Core.Models;

public enum MeasurementState
{
	Idle,
	Preflight,
	ReadingSupply,
	Configuring,
	HeaterRamp,
	Warmup,
	Measuring,
	HeaterOff,
	EndingMeasurement,
	Discharging,
	Completed,
	Aborted,
	Faulted
}
