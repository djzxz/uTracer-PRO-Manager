namespace uTracerProManager.Core.Models;

public sealed record FullTestStatistics(int TotalSeries, int ValidSeries, int Outliers, double MeanIaMa, double StdDevIaMa, double CvIaPercent, double MeanGmMaV, double StdDevGmMaV, double CvGmPercent, double MeanRpKohm, double MeanMu, double LastStepIaDriftPercent, double LastStepGmDriftPercent, double IaPercentOfNominal, double GmPercentOfNominal, double OverallConditionPercent, bool Stable, string Reliability, string Grade, string Recommendation);
