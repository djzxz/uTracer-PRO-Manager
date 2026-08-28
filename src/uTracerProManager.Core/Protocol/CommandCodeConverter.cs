using System;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Protocol;

public static class CommandCodeConverter
{
	private const double DiodeDrop = 0.5;

	private const double DarlingtonDrop = 0.75;

	public static ushort AnodeCode(double voltage, double measuredSupplyVoltage, CalibrationProfile calibration)
	{
		return VoltageCode(voltage, measuredSupplyVoltage, calibration.VaFactor, calibration.AnodeDividerOhm);
	}

	public static ushort ScreenCode(double voltage, double measuredSupplyVoltage, CalibrationProfile calibration)
	{
		return VoltageCode(voltage, measuredSupplyVoltage, calibration.VsFactor, calibration.AnodeDividerOhm);
	}

	public static ushort GridCode(double negativeVoltage, CalibrationProfile calibration, bool calibrationMode = false)
	{
		if (negativeVoltage > 0.0 || negativeVoltage < 0.0 - calibration.MaxGridMagnitudeV)
		{
			throw new ArgumentOutOfRangeException("negativeVoltage", $"Vg musi być od 0 do -{calibration.MaxGridMagnitudeV:F0} V.");
		}
		if (!calibrationMode && !calibration.GridCalibrationVerified)
		{
			throw new InvalidOperationException("Kalibracja siatki nie została zakończona.");
		}
		if (string.Equals(calibration.GridCalibrationModel, "offset-slope", StringComparison.Ordinal))
		{
			if (!double.IsFinite(calibration.GridSlope) || calibration.GridSlope <= 0.0)
			{
				throw new InvalidOperationException("Nieprawidłowe nachylenie kalibracji siatki.");
			}
			double num = (negativeVoltage - calibration.GridOffsetV) / calibration.GridSlope;
			return Clamp10Bit(-1024.0 * num / calibration.MaxGridMagnitudeV);
		}
		double num2 = Math.Abs(negativeVoltage);
		double num3 = 1024.0 / calibration.MaxGridMagnitudeV * calibration.Vg1Factor;
		double num4 = 4096.0 / calibration.MaxGridMagnitudeV * calibration.Vg4Factor;
		double b = 40960.0 / calibration.MaxGridMagnitudeV * calibration.Vg40Factor;
		double value = ((num2 <= 1.0) ? Interpolate(0.0, num3, num2) : ((!(num2 <= 4.0)) ? Interpolate(num4, b, (num2 - 4.0) / 36.0) : Interpolate(num3, num4, (num2 - 1.0) / 3.0)));
		return Clamp10Bit(value);
	}

	public static ushort HeaterCode(double rmsVoltage, double measuredSupplyVoltage)
	{
		if (rmsVoltage < 0.0)
		{
			throw new ArgumentOutOfRangeException("rmsVoltage");
		}
		if (measuredSupplyVoltage < 5.0)
		{
			throw new InvalidOperationException("Napięcie zasilania Vsu jest zbyt małe.");
		}
		return Clamp10Bit(1024.0 * Math.Pow(rmsVoltage / measuredSupplyVoltage, 2.0));
	}

	private static ushort VoltageCode(double voltage, double supplyVoltage, double calibrationFactor, double dividerOhm)
	{
		if (voltage < 0.0)
		{
			throw new ArgumentOutOfRangeException("voltage");
		}
		if (supplyVoltage < 5.0)
		{
			throw new InvalidOperationException("Napięcie zasilania Vsu jest zbyt małe.");
		}
		if (calibrationFactor <= 0.0)
		{
			throw new InvalidOperationException("Brak współczynnika kalibracji.");
		}
		double num = (470000.0 + dividerOhm) / dividerOhm;
		return Clamp10Bit(204.6 * ((voltage + supplyVoltage - 0.5 + 0.75) / num / calibrationFactor));
	}

	private static double Interpolate(double a, double b, double t)
	{
		return a + (b - a) * t;
	}

	private static ushort Clamp10Bit(double value)
	{
		return checked((ushort)Math.Clamp(Math.Round(value), 0.0, 1023.0));
	}
}
