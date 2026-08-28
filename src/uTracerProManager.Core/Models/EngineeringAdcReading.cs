namespace uTracerProManager.Core.Models;

public sealed record EngineeringAdcReading(double AnodeCurrentMa, double RawAnodeCurrentMa, double ScreenCurrentMa, double RawScreenCurrentMa, double SupplyVoltage, double NegativeSupplyVoltage, double EstimatedAnodeVoltage, double MeasuredScreenVoltage, double? CommandedAnodeVoltage, double? CommandedScreenVoltage, double? CommandedGridVoltage, int AnodeGainIndex, int ScreenGainIndex, double AveragingDivisor);
