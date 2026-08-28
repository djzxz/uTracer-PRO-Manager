using System;

namespace uTracerProManager.Core.Models;

public sealed record MeasurementSessionResult(TubeProfile Profile, MeasurementResult Measurement, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, int WarmupSeconds, int DischargeSeconds, bool Emulator)
{
	public TimeSpan Duration => CompletedAt - StartedAt;
}
