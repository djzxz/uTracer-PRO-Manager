using System;

namespace uTracerProManager.Core.Services;

public static class DischargeTimeCalculator
{
	public static int CalculateSeconds(double highestVoltage)
	{
		if (!double.IsFinite(highestVoltage) || highestVoltage <= 0.0)
		{
			return 1;
		}
		return Math.Clamp((int)Math.Ceiling(highestVoltage / 20.0), 1, 25);
	}
}
