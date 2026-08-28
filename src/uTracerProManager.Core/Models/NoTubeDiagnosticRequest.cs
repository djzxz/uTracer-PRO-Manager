using System;
using System.Runtime.CompilerServices;

namespace uTracerProManager.Core.Models;

public sealed record NoTubeDiagnosticRequest(double AnodeVoltage = 20.0, double ScreenVoltage = 20.0)
{
	public const double MaximumDiagnosticVoltage = 50.0;

	public void Validate()
	{
		double anodeVoltage = AnodeVoltage;
		if ((anodeVoltage < 2.0 || anodeVoltage > 50.0) ? true : false)
		{
			throw new ArgumentOutOfRangeException("AnodeVoltage", $"Diagnostyka bez lampy pozwala na 2–{50.0} V.");
		}
		anodeVoltage = ScreenVoltage;
		if ((anodeVoltage < 2.0 || anodeVoltage > 50.0) ? true : false)
		{
			throw new ArgumentOutOfRangeException("ScreenVoltage", $"Diagnostyka bez lampy pozwala na 2–{50.0} V.");
		}
	}

	[CompilerGenerated]
	private NoTubeDiagnosticRequest(NoTubeDiagnosticRequest original)
	{
		AnodeVoltage = original.AnodeVoltage;
		ScreenVoltage = original.ScreenVoltage;
	}
}
