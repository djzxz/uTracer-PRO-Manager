namespace uTracerProManager.Core.Models;

public sealed record ReferenceCurveTarget(
    long Id,
    string CurveSetId,
    string ProfileId,
    string Section,
    string TargetKind,
    string SeriesKey,
    int Sequence,
    double Va,
    double Vs,
    double Vg,
    double Vh,
    string Status,
    double? Ia,
    double? Is,
    string SourceStatus,
    string SourcePage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public bool HasMeasuredOrDigitizedValue => Ia.HasValue;
    public bool IsPending => string.Equals(Status, "PENDING", StringComparison.OrdinalIgnoreCase);
}
