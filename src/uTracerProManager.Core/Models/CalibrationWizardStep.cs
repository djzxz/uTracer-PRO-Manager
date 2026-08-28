namespace uTracerProManager.Core.Models;

public enum CalibrationWizardStep
{
	Welcome,
	HardwareValues,
	NegativeSupply,
	SupplyVoltage,
	AnodeVoltage,
	ScreenVoltage,
	GridMinusHalf,
	GridMinus40,
	GridVerification,
	CurrentAmplifiers,
	Summary
}
