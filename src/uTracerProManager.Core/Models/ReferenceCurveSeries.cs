namespace uTracerProManager.Core.Models;

public sealed record ReferenceCurveSeries(
    string Id,
    string ProfileId,
    string Section,
    string SourceKind,
    string SourceTitle,
    string SourceUrl,
    string SourcePage,
    string Mode,
    DateTimeOffset CapturedAt,
    string DigitizationStatus,
    double Quality,
    string ExtractionVersion,
    bool HumanReviewed,
    IReadOnlyList<ReferenceCurveDataPoint> Points);

public sealed record ReferenceCurveDataPoint(
    int Sequence,
    string SeriesKey,
    double VaSet,
    double VaMeasured,
    double VsSet,
    double VsMeasured,
    double Vg,
    double Vh,
    double Ia,
    double Is,
    double? Gm,
    double? Rp,
    string ComplianceStatus,
    bool Interpolated);

public sealed record ReferenceCurveMatchRequest(
    string ProfileId,
    string Section,
    double? Va,
    double? Vs,
    double? Vg,
    double? Vh,
    double VoltageTolerance,
    string? Mode = null,
    string? SourceKind = null);

public sealed record ReferenceCurveComparison(
    string ReferenceSeriesId,
    int ComparedPoints,
    int RejectedPoints,
    double IaMae,
    double IaRms,
    double IsMae,
    double IsRms,
    double? GmMae,
    double? RpMae,
    string Summary);
