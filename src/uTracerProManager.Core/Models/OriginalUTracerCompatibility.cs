namespace uTracerProManager.Core.Models;

public sealed record OriginalUTracerQuickTestSettings(
    string Title,
    bool IsTriode,
    double HeaterVoltage,
    double ExternalHeaterVoltage,
    bool ExternalHeaterEnabled,
    double AnodeVoltage,
    double AnodeSwingPercent,
    double ScreenVoltage,
    double ScreenSwingPercent,
    double GridVoltage,
    double GridSwingPercent,
    double NominalAnodeCurrentMa,
    double NominalSecondCurrentMa,
    double NominalRpKohm,
    double NominalGmMaV,
    double NominalMu);

public sealed record OriginalUTracerSetupDocument(
    string SourcePath,
    string Variant,
    IReadOnlyList<string> Lines,
    int MeasurementType,
    double VariableStart,
    double VariableStop,
    int VariableIntervals,
    double Constant1,
    double Constant2,
    double FirstSteppingValue,
    OriginalUTracerQuickTestSettings QuickTest)
{
    public string Summary =>
        $"{Variant}: {Lines.Count} linii • Quick Test {(QuickTest.IsTriode ? "trioda" : "pentoda")} • " +
        $"Va {QuickTest.AnodeVoltage:F1} V • Vs {QuickTest.ScreenVoltage:F1} V • " +
        $"Vg {QuickTest.GridVoltage:+0.###;-0.###;0} V • Uf {QuickTest.HeaterVoltage:F2} V";
}
