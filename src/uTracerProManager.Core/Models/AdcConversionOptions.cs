namespace uTracerProManager.Core.Models;

public sealed record AdcConversionOptions(int AveragingIndex = 0, double CommandedAnodeVoltage = 0.0, double CommandedScreenVoltage = 0.0, double CommandedGridVoltage = 0.0);
