namespace uTracerProManager.Core.Models;

public enum FullTestStage
{
	Idle,
	Preflight,
	HeaterRamp,
	InitialWarmup,
	Conditioning,
	Measuring,
	Evaluating,
	ExtraStabilization,
	ThermalStability,
	CharacteristicScan,
	Saving,
	ShuttingDown,
	Discharging,
	Completed,
	Aborted,
	Faulted
}
