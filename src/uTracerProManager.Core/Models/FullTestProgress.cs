namespace uTracerProManager.Core.Models;

public sealed record FullTestProgress(FullTestStage Stage, string Message, double Percent, int CurrentSeries = 0, int MaximumSeries = 0, int? RemainingSeconds = null, FullTestSample? LatestSample = null, FullTestStatistics? CurrentStatistics = null);
