namespace uTracerProManager.Core.Models;

public sealed record DualSectionComparison(double IaDifferencePercent, double GmDifferencePercent, double RpDifferencePercent, double MuDifferencePercent, double OverallMatchPercent, string Grade, string Recommendation);
