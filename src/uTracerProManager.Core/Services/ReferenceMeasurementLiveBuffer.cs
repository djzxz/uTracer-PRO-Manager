using System.Collections.Concurrent;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

/// <summary>
/// Bufor sesji do live-plotu. Kontroler dopisuje każdy punkt natychmiast,
/// a UI może opróżniać bufor co 100–250 ms bez blokowania toru pomiarowego.
/// </summary>
public sealed class ReferenceMeasurementLiveBuffer : IProgress<ReferenceMeasurementPoint>
{
    private readonly ConcurrentQueue<ReferenceMeasurementPoint> _pending = new();
    private readonly List<ReferenceMeasurementPoint> _all = new();
    private readonly object _sync = new();

    public bool Paused { get; set; }

    public int Count
    {
        get { lock (_sync) return _all.Count; }
    }

    public void Report(ReferenceMeasurementPoint value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
            _all.Add(value);
        _pending.Enqueue(value);
    }

    public IReadOnlyList<ReferenceMeasurementPoint> DrainPending(int maximum = 256)
    {
        if (Paused)
            return Array.Empty<ReferenceMeasurementPoint>();
        var result = new List<ReferenceMeasurementPoint>();
        while (result.Count < Math.Max(1, maximum) && _pending.TryDequeue(out var point))
            result.Add(point);
        return result;
    }

    public IReadOnlyList<ReferenceMeasurementPoint> Snapshot()
    {
        lock (_sync)
            return _all.ToArray();
    }

    public void Clear()
    {
        while (_pending.TryDequeue(out _)) { }
        lock (_sync)
            _all.Clear();
    }
}
