using System;

namespace uTracerProManager.Core.Models;

public sealed record FullTestSample(int Sequence, DateTimeOffset Timestamp, bool Conditioning, double AnodeVoltage, double ScreenVoltage, double GridVoltage, double AnodeCurrentMa, double ScreenCurrentMa, double GmMaV, double RpKohm, double Mu, double AnodePowerW, int AveragingIndex, bool IsOutlier, string ActionAfterSample, string RawStatus, double CommandedAnodeVoltage = 0.0, double MeasuredAnodeVoltage = 0.0, double CommandedScreenVoltage = 0.0, double MeasuredScreenVoltage = 0.0, double SectionBGmMaV = 0.0, double SectionBRpKohm = 0.0, double SectionBMu = 0.0, double SectionBPowerW = 0.0, string MeasurementLabel = "Punkt główny");
