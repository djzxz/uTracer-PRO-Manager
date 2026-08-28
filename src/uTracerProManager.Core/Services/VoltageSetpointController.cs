using System;

namespace uTracerProManager.Core.Services;

public sealed class VoltageSetpointController
{
	public VoltageCorrection Correct(double targetVoltage, double previousCommandVoltage, double measuredVoltage, double maximumAllowedVoltage, double minimumAllowedVoltage = 0.0, double proportionalGain = 0.7, double maximumStepVoltage = 5.0)
	{
		if (!double.IsFinite(targetVoltage) || !double.IsFinite(previousCommandVoltage) || !double.IsFinite(measuredVoltage))
		{
			throw new ArgumentException("Napięcia regulatora muszą być liczbami skończonymi.");
		}
		double num = Math.Max(0.75, targetVoltage * 0.005);
		double num2 = targetVoltage - measuredVoltage;
		if (Math.Abs(num2) <= num)
		{
			return new VoltageCorrection(targetVoltage, previousCommandVoltage, measuredVoltage, previousCommandVoltage, num2, InTolerance: true, Limited: false);
		}
		double num3 = Math.Clamp(proportionalGain * num2, 0.0 - maximumStepVoltage, maximumStepVoltage);
		double num4 = previousCommandVoltage + num3;
		double num5 = Math.Clamp(num4, minimumAllowedVoltage, maximumAllowedVoltage);
		return new VoltageCorrection(targetVoltage, previousCommandVoltage, measuredVoltage, num5, num2, InTolerance: false, Math.Abs(num5 - num4) > 1E-09);
	}
}
