namespace uTracerProManager.Core.Models;

public sealed record ReferenceMeasurementPoint(
    int Sequence,
    int CurveIndex,
    double StepValue,
    double XValue,
    double CommandedVa,
    double MeasuredVa,
    double CommandedVs,
    double MeasuredVs,
    double CommandedVg,
    double HeaterVoltage,
    double AnodeCurrentMa,
    double ScreenCurrentMa,
    string Status);

public sealed record ReferenceMeasurementResult(
    ReferenceMeasurementDefinition Definition,
    ReferenceMeasurementRequest Request,
    TubeProfile Profile,
    IReadOnlyList<ReferenceMeasurementPoint> Points,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool Emulator);

public sealed record ReferenceMeasurementProgress(string Message, double Percent, int CurrentPoint, int TotalPoints);
