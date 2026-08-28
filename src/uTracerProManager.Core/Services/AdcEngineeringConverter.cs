using System;
using System.Collections.Generic;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public static class AdcEngineeringConverter
{
	private static readonly double[] Gain = new double[8] { 1.0, 2.0, 5.0, 10.0, 20.0, 50.0, 100.0, 200.0 };

	private static readonly double[] Average = new double[8] { 0.0, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0, 1.0 };

	private static readonly double[] AutoAverage = new double[8] { 1.0, 1.0, 1.0, 1.0, 2.0, 2.0, 4.0, 8.0 };

	private const double DiodeDrop = 0.5;

	private const double DarlingtonDrop = 0.75;

	public static EngineeringAdcReading Convert(UTracerAdcPacket packet, CalibrationProfile calibration, AdcConversionOptions options)
	{
		ArgumentNullException.ThrowIfNull(packet, "packet");
		ArgumentNullException.ThrowIfNull(calibration, "calibration");
		ArgumentNullException.ThrowIfNull(options, "options");
		IReadOnlyList<string> readOnlyList = calibration.Validate();
		if (readOnlyList.Count > 0)
		{
			throw new InvalidOperationException("Kalibracja jest nieprawidłowa:\n- " + string.Join("\n- ", readOnlyList));
		}
		int num = Math.Min(packet.AnodeGain, (byte)7);
		int num2 = Math.Min(packet.ScreenGain, (byte)7);
		int num3 = Math.Max(num, num2);
		double num4 = ((options.AveragingIndex == 0) ? AutoAverage[num3] : Average[Math.Clamp(options.AveragingIndex, 1, 7)]);
		double anodeSenseOhm = calibration.AnodeSenseOhm;
		double screenSenseOhm = calibration.ScreenSenseOhm;
		double num5 = 4.887585532746823 / anodeSenseOhm;
		double num6 = 4.887585532746823 / screenSenseOhm;
		double val = (double)(int)packet.Ia / Gain[num] * num5 * calibration.IaFactor / num4;
		double rawAnodeCurrentMa = (double)(int)packet.IaRaw * num5 / num4;
		double val2 = (double)(int)packet.Is / Gain[num2] * num6 * calibration.IsFactor / num4;
		double rawScreenCurrentMa = (double)(int)packet.IsRaw * num6 / num4;
		val = Math.Max(0.0, val);
		val2 = Math.Max(0.0, val2);
		double num7 = 0.023351797545345932;
		double num8 = (double)(int)packet.Vsu * num7 * calibration.VsuFactor;
		double num9 = 24.5;
		double negativeSupplyVoltage = 5.0 * (num9 * ((double)(int)packet.Vn / 1023.0 - 1.0) + 1.0) * calibration.VnFactor;
		double num10 = (470000.0 + calibration.AnodeDividerOhm) / calibration.AnodeDividerOhm;
		double num11 = (double)(int)packet.Va * 5.0 / 1023.0 * num10 * calibration.VaFactor - num8 + 0.5 - 0.75 - val * anodeSenseOhm / 1000.0;
		double num12 = (double)(int)packet.Vs * 5.0 / 1023.0 * num10 * calibration.VsFactor - num8 + 0.5 - 0.75 - val2 * screenSenseOhm / 1000.0;
		if (num11 < 1.0)
		{
			num11 = 0.0;
		}
		if (num12 < 1.0)
		{
			num12 = 0.0;
		}
		return new EngineeringAdcReading(val, rawAnodeCurrentMa, val2, rawScreenCurrentMa, num8, negativeSupplyVoltage, num11, num12, (options.CommandedAnodeVoltage > 0.0) ? new double?(options.CommandedAnodeVoltage) : ((double?)null), (options.CommandedScreenVoltage > 0.0) ? new double?(options.CommandedScreenVoltage) : ((double?)null), options.CommandedGridVoltage, num, num2, num4);
	}
}
