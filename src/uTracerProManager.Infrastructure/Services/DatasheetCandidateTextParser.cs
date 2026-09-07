using System.Globalization;
using System.Text.RegularExpressions;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Services;

/// <summary>
/// Konserwatywny etap po ekstrakcji tekstu/OCR. Nie zgaduje: jeżeli pole ma
/// kilka sprzecznych wartości lub nie ma jednoznacznej etykiety, Value=null.
/// Właściwy silnik PDF/OCR może dostarczyć tekst strony przez IDatasheetTextExtractor.
/// </summary>
public sealed class DatasheetCandidateTextParser
{
    public DatasheetExtractionCandidate Parse(
        string tubeType,
        string manufacturer,
        string sourceTitle,
        string sourceUrl,
        string sourcePage,
        string pageText,
        string extractionEngine,
        string extractionVersion)
    {
        pageText ??= string.Empty;
        return new DatasheetExtractionCandidate(
            Guid.NewGuid().ToString("N"),
            tubeType ?? string.Empty,
            manufacturer ?? string.Empty,
            sourceTitle ?? string.Empty,
            sourceUrl ?? string.Empty,
            sourcePage ?? string.Empty,
            extractionEngine ?? "TEXT",
            extractionVersion ?? "1",
            FindVoltage(pageText, ["heater", "filament", "uf", "vh", "żarzen", "zarzen"]),
            FindCurrentAmp(pageText, ["heater current", "filament current", "if"]),
            FindVoltage(pageText, ["anode voltage", "plate voltage", "va"]),
            FindVoltage(pageText, ["screen voltage", "g2 voltage", "vs", "vg2"]),
            FindSignedVoltage(pageText, ["grid voltage", "control grid", "vg1", "vg"]),
            FindCurrentMa(pageText, ["anode current", "plate current", "ia"]),
            FindCurrentMa(pageText, ["screen current", "g2 current", "is", "ig2"]),
            FindPower(pageText, ["anode dissipation", "plate dissipation", "pa", "wa"]),
            FindPower(pageText, ["screen dissipation", "g2 dissipation", "ps", "wg2"]),
            ExtractPinout(pageText),
            false,
            DateTimeOffset.UtcNow);
    }

    private static ExtractedField<double> FindVoltage(string text, IReadOnlyList<string> labels) =>
        FindQuantity(text, labels, @"(?<value>[-+]?\d+(?:[\.,]\d+)?)\s*(?:V|volt(?:s)?)\b", 0.92);

    private static ExtractedField<double> FindSignedVoltage(string text, IReadOnlyList<string> labels) =>
        FindQuantity(text, labels, @"(?<value>[-+]?\d+(?:[\.,]\d+)?)\s*(?:V|volt(?:s)?)\b", 0.94);

    private static ExtractedField<double> FindCurrentMa(string text, IReadOnlyList<string> labels) =>
        FindQuantity(text, labels, @"(?<value>[-+]?\d+(?:[\.,]\d+)?)\s*mA\b", 0.92);

    private static ExtractedField<double> FindCurrentAmp(string text, IReadOnlyList<string> labels) =>
        FindQuantity(text, labels, @"(?<value>[-+]?\d+(?:[\.,]\d+)?)\s*(?:A|amp(?:s|ere)?)\b", 0.92);

    private static ExtractedField<double> FindPower(string text, IReadOnlyList<string> labels) =>
        FindQuantity(text, labels, @"(?<value>[-+]?\d+(?:[\.,]\d+)?)\s*(?:W|watt(?:s)?)\b", 0.93);

    private static ExtractedField<double> FindQuantity(
        string text,
        IReadOnlyList<string> labels,
        string valuePattern,
        double baseConfidence)
    {
        var candidates = new List<(double Value, string Evidence)>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = Regex.Replace(rawLine, @"\s+", " ").Trim();
            if (!labels.Any(label => ContainsLabel(line, label)))
                continue;
            var match = Regex.Match(line, valuePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;
            var normalized = match.Groups["value"].Value.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
                candidates.Add((value, line.Length <= 220 ? line : line[..220]));
        }

        var distinct = candidates
            .GroupBy(item => Math.Round(item.Value, 6))
            .Select(group => group.First())
            .ToArray();

        if (distinct.Length != 1)
        {
            var evidence = distinct.Length == 0
                ? "Nie znaleziono jednoznacznej wartości."
                : "Wykryto sprzeczne wartości: " + string.Join(" | ", distinct.Take(4).Select(item => item.Evidence));
            return new ExtractedField<double>(null, distinct.Length == 0 ? 0 : 0.45, evidence, true);
        }

        return new ExtractedField<double>(distinct[0].Value, baseConfidence, distinct[0].Evidence, true);
    }

    private static string ExtractPinout(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => line.Contains("pin", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("base", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("socket", StringComparison.OrdinalIgnoreCase))
            .Where(line => Regex.IsMatch(line, @"\b[1-9]\b"))
            .Take(8)
            .ToArray();
        return lines.Length == 0 ? string.Empty : string.Join("; ", lines);
    }

    private static bool ContainsLabel(string line, string label)
    {
        if (label.Length <= 3)
            return Regex.IsMatch(line, $@"(?<![A-Za-z0-9]){Regex.Escape(label)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
        return line.Contains(label, StringComparison.OrdinalIgnoreCase);
    }
}

public interface IDatasheetTextExtractor
{
    Task<IReadOnlyList<DatasheetPageText>> ExtractAsync(string source, CancellationToken cancellationToken = default);
}

public sealed record DatasheetPageText(
    int PageNumber,
    string Text,
    bool OcrWasRequired,
    string Engine,
    string Version);
