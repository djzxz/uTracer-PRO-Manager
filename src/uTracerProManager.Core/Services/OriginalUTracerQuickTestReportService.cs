using System.Globalization;
using System.Text;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public sealed class OriginalUTracerQuickTestReportService
{
    public async Task ExportAsync(
        FullTestResult result,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var isTriode = result.Profile.Family.Contains("triod", StringComparison.OrdinalIgnoreCase) ||
                       result.Profile.ScreenVoltage <= 0;
        var type = isTriode ? "Triode" : "Pentode";
        var va = result.Profile.AnodeVoltage;
        var vg = result.Profile.GridVoltage;
        var vaSwing = Math.Abs(va * 0.10);
        var vgSwing = Math.Abs(vg * 0.10);

        var lines = new List<string>
        {
            $"{result.CompletedAt.LocalDateTime:dd.MM.yyyy HH:mm:ss}   uTracer3, GUI  V3.11  {type} Quick Test",
            " ",
            string.Empty,
            " ",
            "SECTION 1",
            " ",
            "Test conditions:",
            $"Va  : {F(va)} (V)                Swing +/- {F(vaSwing)} V (10%)",
            $"Vg  : {F(vg)} (V)                Swing +/- {F(vgSwing)} V (10%)",
            " ",
            "Test results:"
        };
        AddResults(lines, result.Statistics);
        lines.AddRange(new[] { " ", " ", "SECTION 2", " ", "Test conditions:",
            $"Va  : {F(va)} (V)                Swing +/- {F(vaSwing)} V (10%)",
            $"Vg  : {F(vg)} (V)                Swing +/- {F(vgSwing)} V (10%)",
            " ", "Test results:" });
        AddResults(lines, result.SectionBStatistics);
        lines.Add(" ");
        lines.Add(" ");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, string.Join("\r\n", lines), Encoding.ASCII, cancellationToken);
    }

    private static void AddResults(List<string> lines, FullTestStatistics? statistics)
    {
        if (statistics is null)
        {
            lines.Add("Ia  : - - - (mA)                                                 ");
            lines.Add("Ra  : - - - (kohm)                                              Ra = dVa/dIa");
            lines.Add("Gm  : - - - (mA/V)                                              Gm = dIa/dVg");
            lines.Add("mu  : - - - (-)                                                 mu = Gm*Ra");
            return;
        }

        lines.Add($"Ia  : {F(statistics.MeanIaMa)} (mA)                                                 ");
        lines.Add($"Ra  : {F(statistics.MeanRpKohm)} (kohm)                                              Ra = dVa/dIa");
        lines.Add($"Gm  : {F(statistics.MeanGmMaV)} (mA/V)                                              Gm = dIa/dVg");
        lines.Add($"mu  : {F(statistics.MeanMu)} (-)                                                 mu = Gm*Ra");
    }

    private static string F(double value)
    {
        if (!double.IsFinite(value))
            return "- - -";
        return value.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',');
    }
}
