namespace uTracerProManager.Core.Models;

public sealed record MeasurementProgress(MeasurementState State, string Message, double Percent, int? RemainingSeconds = null);
