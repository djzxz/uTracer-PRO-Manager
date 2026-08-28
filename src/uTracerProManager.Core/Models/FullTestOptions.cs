using System;

namespace uTracerProManager.Core.Models;

public sealed record FullTestOptions(TubeTestMode TestMode = TubeTestMode.FullDiagnostic, int InitialWarmupSeconds = 60, int ConditioningSeries = 2, int MinimumValidSeries = 5, int MaximumSeries = 10, int IntervalSeconds = 2, int ExtraStabilizationSeconds = 15, double MaxIaCvPercent = 2.0, double MaxGmCvPercent = 2.5, double MaxStepDriftPercent = 1.5, int StartingAveragingIndex = 0, int MaximumAveragingIndex = 6, bool AccelerateEmulator = true, int EmulatorSpeedMultiplier = 100, bool DualSectionSimultaneous = true, bool MeasureDynamicParameters = true, bool RunCharacteristicScan = false, int MaximumCorrectionIterations = 5, int ThermalStabilitySeconds = 0, int ThermalSampleIntervalSeconds = 30)
{
	public void Validate()
	{
		if (InitialWarmupSeconds < 60 || InitialWarmupSeconds > 1800)
		{
			throw new ArgumentOutOfRangeException("InitialWarmupSeconds", "Nagrzewanie początkowe musi wynosić 60–1800 s.");
		}
		int conditioningSeries = ConditioningSeries;
		if ((conditioningSeries < 0 || conditioningSeries > 5) ? true : false)
		{
			throw new ArgumentOutOfRangeException("ConditioningSeries");
		}
		int num = ((TestMode == TubeTestMode.Quick) ? 1 : 2);
		if (MinimumValidSeries < num || MinimumValidSeries > 10)
		{
			throw new ArgumentOutOfRangeException("MinimumValidSeries");
		}
		if (MaximumSeries < MinimumValidSeries || MaximumSeries > 20)
		{
			throw new ArgumentOutOfRangeException("MaximumSeries");
		}
		conditioningSeries = IntervalSeconds;
		if ((conditioningSeries < 0 || conditioningSeries > 30) ? true : false)
		{
			throw new ArgumentOutOfRangeException("IntervalSeconds");
		}
		conditioningSeries = ExtraStabilizationSeconds;
		if ((conditioningSeries < 0 || conditioningSeries > 120) ? true : false)
		{
			throw new ArgumentOutOfRangeException("ExtraStabilizationSeconds");
		}
		double maxIaCvPercent = MaxIaCvPercent;
		if ((maxIaCvPercent <= 0.0 || maxIaCvPercent > 15.0) ? true : false)
		{
			throw new ArgumentOutOfRangeException("MaxIaCvPercent");
		}
		maxIaCvPercent = MaxGmCvPercent;
		if ((maxIaCvPercent <= 0.0 || maxIaCvPercent > 15.0) ? true : false)
		{
			throw new ArgumentOutOfRangeException("MaxGmCvPercent");
		}
		maxIaCvPercent = MaxStepDriftPercent;
		if ((maxIaCvPercent <= 0.0 || maxIaCvPercent > 15.0) ? true : false)
		{
			throw new ArgumentOutOfRangeException("MaxStepDriftPercent");
		}
		conditioningSeries = StartingAveragingIndex;
		bool flag = ((conditioningSeries < 0 || conditioningSeries > 7) ? true : false);
		if (flag || MaximumAveragingIndex < StartingAveragingIndex || MaximumAveragingIndex > 7)
		{
			throw new ArgumentOutOfRangeException("MaximumAveragingIndex");
		}
		conditioningSeries = EmulatorSpeedMultiplier;
		if ((conditioningSeries < 1 || conditioningSeries > 500) ? true : false)
		{
			throw new ArgumentOutOfRangeException("EmulatorSpeedMultiplier");
		}
		conditioningSeries = MaximumCorrectionIterations;
		if ((conditioningSeries < 1 || conditioningSeries > 10) ? true : false)
		{
			throw new ArgumentOutOfRangeException("MaximumCorrectionIterations");
		}
		conditioningSeries = ThermalStabilitySeconds;
		if ((conditioningSeries < 0 || conditioningSeries > 1800) ? true : false)
		{
			throw new ArgumentOutOfRangeException("ThermalStabilitySeconds");
		}
		flag = ThermalStabilitySeconds > 0;
		if (flag)
		{
			conditioningSeries = ThermalSampleIntervalSeconds;
			bool flag2 = ((conditioningSeries < 5 || conditioningSeries > 120) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new ArgumentOutOfRangeException("ThermalSampleIntervalSeconds");
		}
	}

	public static FullTestOptions ForMode(TubeTestMode mode, bool emulator)
	{
		return mode switch
		{
			TubeTestMode.FullDiagnostic => new FullTestOptions(mode, 60, 2, 5, 10, 2, 15, 2.0, 2.5, 1.5, 1, 6, emulator, 100, DualSectionSimultaneous: true, MeasureDynamicParameters: true, RunCharacteristicScan: true, 5, 300), 
			TubeTestMode.NormalDual => new FullTestOptions(mode, 60, 1, 3, 5, 1, 8, 3.0, 3.5, 2.5, 1, 5, emulator), 
			TubeTestMode.Quick => new FullTestOptions(mode, 60, 0, 1, 1, 0, 0, 10.0, 10.0, 10.0, 2, 2, emulator, 100, DualSectionSimultaneous: true, MeasureDynamicParameters: false), 
			_ => throw new ArgumentOutOfRangeException("mode"), 
		};
	}
}
