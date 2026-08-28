using System.Globalization;
using System.Text;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public sealed class OriginalUTracerSetupFileService
{
    private const int MinimumLineCount = 147;

    public async Task<OriginalUTracerSetupDocument> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Nie znaleziono pliku ustawień uTracera.", path);

        var lines = await File.ReadAllLinesAsync(path, Encoding.ASCII, cancellationToken);
        return Parse(path, lines);
    }

    public OriginalUTracerSetupDocument Parse(string sourcePath, IReadOnlyList<string> sourceLines)
    {
        if (sourceLines.Count < MinimumLineCount)
            throw new InvalidDataException(
                $"Plik .uts ma {sourceLines.Count} linii; oryginalny format wymaga co najmniej {MinimumLineCount}.");

        var lines = sourceLines.ToArray();
        if (ReadInteger(lines[^1], lines.Length) != -1)
            throw new InvalidDataException("Plik .uts nie ma znacznika końca -1.");

        var triode = ReadInteger(lines[104], 105) == 1;
        var externalHeaterEnabled = ReadInteger(lines[1], 2) != 0;
        var externalHeaterVoltage = ReadScaled(lines[0], 1);
        var constantHeaterVoltage = ReadScaled(lines[16], 17);
        var variant = DetectVariant(sourcePath, lines);

        var quickTest = triode
            ? new OriginalUTracerQuickTestSettings(
                CleanTitle(lines[56], sourcePath),
                true,
                externalHeaterEnabled ? externalHeaterVoltage : constantHeaterVoltage,
                externalHeaterVoltage,
                externalHeaterEnabled,
                ReadScaled(lines[105], 106),
                ReadScaled(lines[106], 107),
                ReadScaled(lines[107], 108),
                ReadScaled(lines[108], 109),
                ReadScaled(lines[109], 110),
                ReadScaled(lines[110], 111),
                ReadScaled(lines[111], 112),
                ReadScaled(lines[112], 113),
                ReadScaled(lines[124], 125),
                ReadScaled(lines[125], 126),
                ReadScaled(lines[126], 127))
            : new OriginalUTracerQuickTestSettings(
                CleanTitle(lines[56], sourcePath),
                false,
                externalHeaterEnabled ? externalHeaterVoltage : constantHeaterVoltage,
                externalHeaterVoltage,
                externalHeaterEnabled,
                ReadScaled(lines[113], 114),
                ReadScaled(lines[114], 115),
                ReadScaled(lines[115], 116),
                ReadScaled(lines[116], 117),
                ReadScaled(lines[117], 118),
                ReadScaled(lines[118], 119),
                ReadScaled(lines[119], 120),
                ReadScaled(lines[120], 121),
                ReadScaled(lines[121], 122),
                ReadScaled(lines[122], 123),
                ReadScaled(lines[123], 124));

        ValidateQuickTest(quickTest);

        return new OriginalUTracerSetupDocument(
            Path.GetFullPath(sourcePath),
            variant,
            lines,
            checked((int)ReadInteger(lines[10], 11)),
            ReadScaled(lines[11], 12),
            ReadScaled(lines[12], 13),
            checked((int)ReadInteger(lines[13], 14)),
            ReadScaled(lines[15], 16),
            constantHeaterVoltage,
            ReadScaled(lines[19], 20),
            quickTest);
    }

    public async Task ExportAsync(
        OriginalUTracerSetupDocument? importedTemplate,
        OriginalUTracerQuickTestSettings settings,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateQuickTest(settings);

        var lines = importedTemplate?.Lines.ToArray() ?? LoadDefaultTemplate();
        if (lines.Length < MinimumLineCount)
            throw new InvalidDataException("Szablon ustawień .uts jest niepełny.");

        SetInteger(lines, 0, Scale(settings.ExternalHeaterVoltage));
        SetInteger(lines, 1, settings.ExternalHeaterEnabled ? 1 : 0);
        SetInteger(lines, 16, Scale(settings.HeaterVoltage));
        SetInteger(lines, 104, settings.IsTriode ? 1 : 0);
        lines[56] = SanitizeTitle(settings.Title);

        if (settings.IsTriode)
        {
            SetInteger(lines, 105, Scale(settings.AnodeVoltage));
            SetInteger(lines, 106, Scale(settings.AnodeSwingPercent));
            SetInteger(lines, 107, Scale(settings.ScreenVoltage));
            SetInteger(lines, 108, Scale(settings.ScreenSwingPercent));
            SetInteger(lines, 109, Scale(settings.GridVoltage));
            SetInteger(lines, 110, Scale(settings.GridSwingPercent));
            SetInteger(lines, 111, Scale(settings.NominalAnodeCurrentMa));
            SetInteger(lines, 112, Scale(settings.NominalSecondCurrentMa));
            SetInteger(lines, 124, Scale(settings.NominalRpKohm));
            SetInteger(lines, 125, Scale(settings.NominalGmMaV));
            SetInteger(lines, 126, Scale(settings.NominalMu));
        }
        else
        {
            SetInteger(lines, 113, Scale(settings.AnodeVoltage));
            SetInteger(lines, 114, Scale(settings.AnodeSwingPercent));
            SetInteger(lines, 115, Scale(settings.ScreenVoltage));
            SetInteger(lines, 116, Scale(settings.ScreenSwingPercent));
            SetInteger(lines, 117, Scale(settings.GridVoltage));
            SetInteger(lines, 118, Scale(settings.GridSwingPercent));
            SetInteger(lines, 119, Scale(settings.NominalAnodeCurrentMa));
            SetInteger(lines, 120, Scale(settings.NominalSecondCurrentMa));
            SetInteger(lines, 121, Scale(settings.NominalRpKohm));
            SetInteger(lines, 122, Scale(settings.NominalGmMaV));
            SetInteger(lines, 123, Scale(settings.NominalMu));
        }

        if (importedTemplate is null)
        {
            SetInteger(lines, 11, Scale(Math.Min(settings.GridVoltage, 0)));
            SetInteger(lines, 12, 0);
            SetInteger(lines, 15, Scale(settings.ScreenVoltage > 0 ? settings.ScreenVoltage : settings.AnodeVoltage));
            SetInteger(lines, 19, Scale(settings.AnodeVoltage));
            SetInteger(lines, 38, Scale(Math.Min(settings.GridVoltage, 0)));
            SetInteger(lines, 41, 0);
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var content = string.Join("\r\n", lines) + "\r\n";
        await File.WriteAllTextAsync(path, content, Encoding.ASCII, cancellationToken);
    }

    private static string DetectVariant(string sourcePath, IReadOnlyList<string> lines)
    {
        if (lines[25].Contains("low voltage grid", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(sourcePath).Contains("3p12", StringComparison.OrdinalIgnoreCase))
            return "Oryginalne GUI V3.12.6";

        if (Path.GetFileName(sourcePath).Contains("3p11", StringComparison.OrdinalIgnoreCase))
            return "Oryginalne GUI V3.11";

        return "Oryginalny format uTracer V3.x";
    }

    private static string CleanTitle(string line, string sourcePath)
    {
        var title = line.Trim();
        return title.Length == 0 || title.Equals("Title", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : title;
    }

    private static string SanitizeTitle(string title)
    {
        var result = new string((title ?? string.Empty)
            .Where(character => character is >= ' ' and <= '~' && character is not '\r' and not '\n')
            .Take(80)
            .ToArray()).Trim();
        return result.Length == 0 ? "uTracer PRO Manager" : result;
    }

    private static void ValidateQuickTest(OriginalUTracerQuickTestSettings settings)
    {
        if (!double.IsFinite(settings.HeaterVoltage) || settings.HeaterVoltage is < 0 or > 100)
            throw new InvalidDataException("Napięcie żarzenia w pliku .uts jest poza zakresem 0–100 V.");
        if (!double.IsFinite(settings.AnodeVoltage) || settings.AnodeVoltage is < 0 or > 1000)
            throw new InvalidDataException("Napięcie anody w pliku .uts jest poza zakresem 0–1000 V.");
        if (!double.IsFinite(settings.ScreenVoltage) || settings.ScreenVoltage is < 0 or > 1000)
            throw new InvalidDataException("Napięcie ekranu w pliku .uts jest poza zakresem 0–1000 V.");
        if (!double.IsFinite(settings.GridVoltage) || settings.GridVoltage is < -200 or > 0)
            throw new InvalidDataException("Napięcie siatki w pliku .uts musi mieścić się w zakresie -200…0 V.");
    }

    private static long ReadInteger(string line, int lineNumber)
    {
        var token = FirstToken(line);
        if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"Linia {lineNumber} pliku .uts nie zaczyna się od liczby całkowitej.");
        return value;
    }

    private static double ReadScaled(string line, int lineNumber) => ReadInteger(line, lineNumber) / 1000.0;

    private static string FirstToken(string line)
    {
        var trimmed = line.TrimStart();
        var separator = trimmed.IndexOfAny([' ', '\t']);
        return separator < 0 ? trimmed : trimmed[..separator];
    }

    private static long Scale(double value) =>
        checked((long)Math.Round(value * 1000.0, MidpointRounding.AwayFromZero));

    private static void SetInteger(string[] lines, int index, long value)
    {
        var line = lines[index];
        var start = 0;
        while (start < line.Length && char.IsWhiteSpace(line[start]))
            start++;
        var end = start;
        if (end < line.Length && (line[end] == '-' || line[end] == '+'))
            end++;
        while (end < line.Length && char.IsDigit(line[end]))
            end++;

        var prefix = line[..start];
        var suffix = end < line.Length ? line[end..] : string.Empty;
        lines[index] = prefix + value.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string[] LoadDefaultTemplate()
    {
        var assembly = typeof(OriginalUTracerSetupFileService).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("uTracer_3p12p6_default.uts", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Brak wbudowanego szablonu .uts.");
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        return content.Split('\n', StringSplitOptions.None).Where((_, index) => index < MinimumLineCount).ToArray();
    }
}
