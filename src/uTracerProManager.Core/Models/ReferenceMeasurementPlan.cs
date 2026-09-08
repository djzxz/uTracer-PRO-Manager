namespace uTracerProManager.Core.Models;

public sealed record ReferenceMeasurementTarget(
    int Sequence,
    int CurveIndex,
    double X,
    double Step,
    double Va,
    double Vs,
    double Vg,
    double Vh,
    bool PositiveGridViaScreen);

public sealed record ReferenceMeasurementPlan(
    IReadOnlyList<ReferenceMeasurementTarget> Targets,
    double AnodeSoftwareLimitMa,
    double ScreenSoftwareLimitMa,
    int HardwareComplianceMa,
    byte HardwareComplianceCode,
    byte ScreenGainCode,
    byte AnodeGainCode,
    MeasurementPlanReadiness State,
    string Summary)
{
    public bool IsValidated => State == MeasurementPlanReadiness.Validated;
}
