using System;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public static class CalibrationMath
{
	public static double CorrectMultiplicativeFactor(double oldFactor, double desiredValue, double measuredValue)
	{
		if (!double.IsFinite(oldFactor) || !double.IsFinite(desiredValue) || !double.IsFinite(measuredValue) || Math.Abs(measuredValue) < 1E-09)
		{
			throw new ArgumentException("Nie można obliczyć współczynnika z podanych wartości.");
		}
		return Math.Clamp(oldFactor * desiredValue / measuredValue, 0.4, 1.6);
	}

	public static double CorrectCommandDividerFactor(double oldFactor, double desiredOutput, double measuredOutput)
	{
		if (!double.IsFinite(oldFactor) || !double.IsFinite(desiredOutput) || !double.IsFinite(measuredOutput) || Math.Abs(desiredOutput) < 1E-09)
		{
			throw new ArgumentException("Nie można obliczyć współczynnika napięcia.");
		}
		return Math.Clamp(oldFactor * measuredOutput / desiredOutput, 0.4, 1.6);
	}

	public static bool IsWithinTolerance(double desired, double measured, double percent = 0.5, double absolute = 0.1)
	{
		double num = Math.Max(absolute, Math.Abs(desired) * percent / 100.0);
		return Math.Abs(desired - measured) <= num;
	}

	public static CalibrationProfile ApplySupply(CalibrationProfile profile, double rawCalculated, double dmmValue)
	{
		return profile with
		{
			VsuFactor = CorrectMultiplicativeFactor(profile.VsuFactor, dmmValue, rawCalculated),
			SupplyCalibrationVerified = IsWithinTolerance(dmmValue, rawCalculated, 1.0, 0.15)
		};
	}

	public static CalibrationProfile ApplyNegativeSupply(CalibrationProfile profile, double rawCalculated, double dmmValue)
	{
		return profile with
		{
			VnFactor = CorrectMultiplicativeFactor(profile.VnFactor, Math.Abs(dmmValue), Math.Abs(rawCalculated)),
			NegativeSupplyCalibrationVerified = IsWithinTolerance(Math.Abs(dmmValue), Math.Abs(rawCalculated), 1.0, 0.25)
		};
	}

	public static CalibrationProfile ApplyGridPoint(CalibrationProfile profile, double targetMagnitude, double measuredMagnitude)
	{
		double oldFactor = ((targetMagnitude <= 1.0) ? profile.Vg1Factor : ((!(targetMagnitude <= 4.0)) ? profile.Vg40Factor : profile.Vg4Factor));
		double num = CorrectMultiplicativeFactor(oldFactor, targetMagnitude, measuredMagnitude);
		if (!(targetMagnitude <= 1.0))
		{
			if (targetMagnitude <= 4.0)
			{
				return profile with
				{
					Vg4Factor = num
				};
			}
			return profile with
			{
				Vg40Factor = num
			};
		}
		return profile with
		{
			Vg1Factor = num
		};
	}

	public static (double OffsetV, double Slope) CalculateGridOffsetSlope(double measuredAtMinusHalfV, double measuredAtMinusFortyV)
	{
		double num = Math.Abs(measuredAtMinusHalfV);
		double num2 = Math.Abs(measuredAtMinusFortyV);
		bool flag = !double.IsFinite(num) || !double.IsFinite(num2);
		if (!flag)
		{
			bool flag2 = ((num < 0.3 || num > 0.7) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			bool flag2 = ((num2 < 35.0 || num2 > 45.0) ? true : false);
			flag3 = flag2;
		}
		if (flag3 || num2 <= num)
		{
			throw new ArgumentException("Punkty siatki muszą wynosić 0,3–0,7 V oraz 35–45 V.");
		}
		double num3 = (num2 - num) / 39.5;
		double num4 = num3 * 40.0 - num2;
		flag3 = ((num3 < 0.8 || num3 > 1.2) ? true : false);
		flag = flag3;
		if (!flag)
		{
			bool flag2 = ((num4 < -0.4 || num4 > 0.4) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException($"Wynik Vg offset={num4:F4} V, slope={num3:F4} jest poza " + "zakresem panelu uTmax. Sprawdź przewody i tor siatki.");
		}
		return (OffsetV: num4, Slope: num3);
	}

	public static CalibrationProfile ApplyGridOffsetSlope(CalibrationProfile profile, double measuredAtMinusHalfV, double measuredAtMinusFortyV)
	{
		var (gridOffsetV, gridSlope) = CalculateGridOffsetSlope(measuredAtMinusHalfV, measuredAtMinusFortyV);
		return profile with
		{
			CalibrationVersion = "2.0",
			GridCalibrationModel = "offset-slope",
			GridOffsetV = gridOffsetV,
			GridSlope = gridSlope,
			GridCalibrationVerified = false,
			GridOffsetSlopeVerified = false
		};
	}

	public static CalibrationProfile ApplyScreenVoltage(CalibrationProfile profile, double targetIncrement, double measuredIncrement)
	{
		return profile with
		{
			VsFactor = CorrectCommandDividerFactor(profile.VsFactor, targetIncrement, measuredIncrement)
		};
	}

	public static CalibrationProfile ApplyAnodeVoltage(CalibrationProfile profile, double targetIncrement, double measuredIncrement)
	{
		return profile with
		{
			VaFactor = CorrectCommandDividerFactor(profile.VaFactor, targetIncrement, measuredIncrement)
		};
	}

	public static CalibrationProfile ApplyCurrents(CalibrationProfile profile, double expectedAnodeMa, double measuredAnodeMa, double expectedScreenMa, double measuredScreenMa)
	{
		return profile with
		{
			IaFactor = CorrectMultiplicativeFactor(profile.IaFactor, expectedAnodeMa, measuredAnodeMa),
			IsFactor = CorrectMultiplicativeFactor(profile.IsFactor, expectedScreenMa, measuredScreenMa),
			CurrentCalibrationVerified = (IsWithinTolerance(expectedAnodeMa, measuredAnodeMa, 1.0, 0.15) && IsWithinTolerance(expectedScreenMa, measuredScreenMa, 1.0, 0.15))
		};
	}
}
