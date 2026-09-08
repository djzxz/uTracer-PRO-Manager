using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

/// <summary>
/// Procesowy strumień próbek pomiaru referencyjnego. Służy wyłącznie do
/// prezentacji live i nie bierze udziału w decyzjach bezpieczeństwa.
/// </summary>
public static class ReferenceMeasurementSampleBus
{
    public static event EventHandler<ReferenceMeasurementPoint>? SampleReceived;
    public static event EventHandler? SessionStarted;
    public static event EventHandler? SessionCompleted;

    public static void Start() => SessionStarted?.Invoke(null, EventArgs.Empty);

    public static void Publish(ReferenceMeasurementPoint point) =>
        SampleReceived?.Invoke(null, point);

    public static void Complete() => SessionCompleted?.Invoke(null, EventArgs.Empty);
}
