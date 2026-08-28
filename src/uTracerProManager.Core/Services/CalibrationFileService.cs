using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public sealed class CalibrationFileService
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	public async Task<CalibrationProfile> ImportAsync(string path, CancellationToken cancellationToken = default)
	{
		if (!File.Exists(path))
			throw new FileNotFoundException("Nie znaleziono pliku kalibracji.", path);

		if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
		{
			var json = await LoadJsonAsync(path, cancellationToken);
			return json ?? throw new InvalidDataException("Plik JSON nie zawiera profilu kalibracji.");
		}

		var lines = await File.ReadAllLinesAsync(path, cancellationToken);
		var first = lines.Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;
		if (first.Contains("Triode Quick Test", StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("Wybrany plik .txt jest raportem Quick Test, a nie plikiem kalibracji .cal.");

			if (lines.Any(IsKeyValueCalibrationLine))
				return await ImportKeyValueAsync(path, cancellationToken);

		return ImportOriginalGui(path, lines);
	}

	public CalibrationProfile ImportOriginalGui(string path, IReadOnlyList<string> sourceLines)
	{
		var lines = sourceLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
		if (lines.Length < 13)
			throw new InvalidDataException("Oryginalny plik .cal musi zawierać 10 współczynników, port, wersję sprzętu i znacznik -1.");

		var values = lines.Take(13).Select((line, index) => ParseLeadingInteger(line, index + 1)).ToArray();
		if (values[12] != -1)
			throw new InvalidDataException("Oryginalny plik .cal nie kończy się znacznikiem -1.");
		if (values[11] is < 1 or > 2)
			throw new InvalidDataException("Nieobsługiwana wersja sprzętu w pliku .cal. Dozwolone: 1=uTracer 3, 2=uTracer 3+.");

		var portNumber = Math.Abs(values[10]);
		if (portNumber is < 1 or > 256)
			throw new InvalidDataException("Numer portu COM zapisany w pliku .cal jest nieprawidłowy.");

		var profile = new CalibrationProfile
		{
			DeviceName = values[11] == 2
				? "uTracer 3+ — import z oryginalnego GUI"
				: "uTracer 3 — import z oryginalnego GUI",
			SourcePath = Path.GetFullPath(path),
			ImportedAt = DateTimeOffset.Now,
			ImportedFromFile = true,
			PortName = $"COM{portNumber}",
			CalibrationVersion = "1.0",
			VaFactor = ScaleFactor(values[0]),
			VsFactor = ScaleFactor(values[1]),
			IaFactor = ScaleFactor(values[2]),
			IsFactor = ScaleFactor(values[3]),
			VsuFactor = ScaleFactor(values[4]),
			Vg40Factor = ScaleFactor(values[5]),
			OriginalVsatFactor = ScaleFactor(values[6]),
			Vg4Factor = ScaleFactor(values[7]),
			Vg1Factor = ScaleFactor(values[8]),
			OriginalSpareFactor = ScaleFactor(values[9]),
			OriginalHardwareVersion = checked((int)values[11]),
			GridOffsetV = 0,
			GridSlope = 1,
			GridCalibrationModel = "legacy-three-point",
			VnFactor = 1,
			AnodeDividerOhm = 5230,
			AnodeSenseOhm = 18,
			ScreenSenseOhm = 18,
			MaxAnodeVoltage = values[11] == 2 ? 400 : 300,
			MaxAnodeCurrentMa = 200,
			MaxScreenCurrentMa = 200,
			MaxGridMagnitudeV = 50
		};

		var errors = profile.Validate();
		if (errors.Count > 0)
			throw new InvalidDataException("Oryginalny plik .cal nie przeszedł kontroli:\n- " + string.Join("\n- ", errors));

		return profile;
	}

	public async Task<CalibrationProfile> ImportKeyValueAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!File.Exists(path))
		{
			throw new FileNotFoundException("Nie znaleziono pliku kalibracji.", path);
		}
		Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] array = await File.ReadAllLinesAsync(path, cancellationToken);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length != 0 && !text.StartsWith(';') && !text.StartsWith('#'))
			{
				int num = text.IndexOf('=');
				if (num > 0)
				{
					Dictionary<string, string> dictionary = values;
					string key = text.Substring(0, num).Trim();
					string text2 = text;
					int num2 = num + 1;
					dictionary[key] = text2.Substring(num2, text2.Length - num2).Trim();
				}
			}
		}
		string[] array2 = new string[9] { "Va", "Vs", "Ia", "Is", "Vsu", "Vn", "RaVal", "VaMax", "VgMax" }.Where((string key2) => !values.ContainsKey(key2)).ToArray();
		if (array2.Length != 0)
		{
			throw new InvalidDataException("Plik kalibracji nie zawiera wymaganych pól: " + string.Join(", ", array2));
		}
		bool flag = values.ContainsKey("Vg0") && values.ContainsKey("Vgm");
		bool flag2 = values.ContainsKey("Vg1") && values.ContainsKey("Vg4") && values.ContainsKey("Vg40");
		if (!flag && !flag2)
		{
			throw new InvalidDataException("Plik nie zawiera kalibracji siatki. Wymagane są Vg0/Vgm (aktualny uTmax) albo Vg1/Vg4/Vg40 (starszy format).");
		}
		CalibrationProfile obj = new CalibrationProfile
		{
			DeviceName = "uTracer 3+ — import kalibracji",
			SourcePath = Path.GetFullPath(path),
			ImportedAt = DateTimeOffset.Now,
			ImportedFromFile = true,
			PortName = values.GetValueOrDefault("COM", string.Empty),
			CalibrationVersion = (flag ? "2.0" : "1.0"),
			VaFactor = Get("Va", 1.0),
			VsFactor = Get("Vs", 1.0),
			IaFactor = Get("Ia", 1.0),
			IsFactor = Get("Is", 1.0),
			VsuFactor = Get("Vsu", 1.0),
			Vg1Factor = Get("Vg1", 1.0),
			Vg4Factor = Get("Vg4", 1.0),
			Vg40Factor = Get("Vg40", 1.0),
			GridOffsetV = Get("Vg0", 0.0),
			GridSlope = Get("Vgm", 1.0),
			GridCalibrationModel = (flag ? "offset-slope" : "legacy-three-point"),
			VnFactor = Get("Vn", 1.0),
			AnodeDividerOhm = Get("RaVal", 5230.0),
			AnodeSenseOhm = Get("IaRsense", Get("IaSense", 18.0)),
			ScreenSenseOhm = Get("IsRsense", Get("IsSense", 18.0)),
			MaxAnodeVoltage = Get("VaMax", 300.0),
			MaxAnodeCurrentMa = Get("IaMax", 200.0),
			MaxScreenCurrentMa = Get("IsMax", 200.0),
			MaxGridMagnitudeV = Get("VgMax", 50.0),
			VadcOffsetV = Get("VadcOffset", 0.0),
			ExtraSeriesResistanceOhm = Get("Rextra", 0.0)
		};
		IReadOnlyList<string> readOnlyList = obj.Validate();
		if (readOnlyList.Count > 0)
		{
			throw new InvalidDataException("Plik kalibracji nie przeszedł kontroli:\n- " + string.Join("\n- ", readOnlyList));
		}
		return obj;
		double Get(string key2, double fallback)
		{
			if (!values.TryGetValue(key2, out string value))
			{
				return fallback;
			}
			value = value.Replace(',', '.');
			if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				return fallback;
			}
			return result;
		}
	}

	public async Task ExportKeyValueAsync(CalibrationProfile profile, string path, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(profile, "profile");
		string[] contents = new string[16]
		{
			"COM=" + profile.PortName,
			"Va=" + Format(profile.VaFactor),
			"Vs=" + Format(profile.VsFactor),
			"Ia=" + Format(profile.IaFactor),
			"Is=" + Format(profile.IsFactor),
			"Vsu=" + Format(profile.VsuFactor),
			"Vg0=" + Format(profile.GridOffsetV),
			"Vgm=" + Format(profile.GridSlope),
			"Vn=" + Format(profile.VnFactor),
			"RaVal=" + Format(profile.AnodeDividerOhm),
			"VaMax=" + Format(profile.MaxAnodeVoltage),
			"IaRsense=" + Format(profile.AnodeSenseOhm),
			"IsRsense=" + Format(profile.ScreenSenseOhm),
			"IaMax=" + Format(profile.MaxAnodeCurrentMa),
			"IsMax=" + Format(profile.MaxScreenCurrentMa),
			"VgMax=" + Format(profile.MaxGridMagnitudeV)
		};
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		await File.WriteAllLinesAsync(path, contents, cancellationToken);
	}

	public async Task ExportOriginalGuiAsync(
		CalibrationProfile profile,
		string path,
		string? portOverride = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		var errors = profile.Validate();
		if (errors.Count > 0)
			throw new InvalidDataException("Kalibracja nie może zostać wyeksportowana:\n- " + string.Join("\n- ", errors));

		var portNumber = ParseComPort(string.IsNullOrWhiteSpace(portOverride) ? profile.PortName : portOverride);
		var hardwareVersion = profile.OriginalHardwareVersion is 1 or 2
			? profile.OriginalHardwareVersion
			: 2;

		var contents = new[]
		{
			OriginalLine(profile.VaFactor, "Va Gain"),
			OriginalLine(profile.VsFactor, "Vs Gain"),
			OriginalLine(profile.IaFactor, "Ia Gain"),
			OriginalLine(profile.IsFactor, "Is Gain"),
			OriginalLine(profile.VsuFactor, "Vsupp"),
			OriginalLine(profile.Vg40Factor, "Vgrid Gain (40V)"),
			OriginalLine(profile.OriginalVsatFactor, "Vsat"),
			OriginalLine(profile.Vg4Factor, "Vgrid Gain (4V)"),
			OriginalLine(profile.Vg1Factor, "spare"),
			OriginalLine(profile.OriginalSpareFactor, "spare"),
			$"{-portNumber,5}         'COM port number (negative because this is an extended file",
			$"{hardwareVersion,5}         'version number 1 = version 3, 2 = version 3+",
			"   -1         '-1 is end of file"
		};

		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directory))
			Directory.CreateDirectory(directory);
		await File.WriteAllTextAsync(path, string.Join("\r\n", contents) + "\r\n", Encoding.ASCII, cancellationToken);
	}

	public async Task SaveJsonAsync(CalibrationProfile profile, string path, CancellationToken cancellationToken = default(CancellationToken))
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		await using FileStream stream = File.Create(path);
		await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
	}

	public async Task<CalibrationProfile?> LoadJsonAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!File.Exists(path))
		{
			return null;
		}
		CalibrationProfile result;
		await using (FileStream stream = File.OpenRead(path))
		{
			result = await JsonSerializer.DeserializeAsync<CalibrationProfile>(stream, JsonOptions, cancellationToken);
		}
		return result;
	}

	private static string Format(double value)
	{
		return value.ToString("0.########", CultureInfo.InvariantCulture);
	}

	private static long ParseLeadingInteger(string line, int lineNumber)
	{
		var trimmed = line.TrimStart();
		var separator = trimmed.IndexOfAny(new[] { ' ', '\t', '\'' });
		var token = separator < 0 ? trimmed : trimmed.Substring(0, separator);
		if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
			throw new InvalidDataException($"Linia {lineNumber} oryginalnego pliku .cal nie zaczyna się od liczby całkowitej.");
		return value;
	}

	private static double ScaleFactor(long value) => value / 1000.0;

	private static int ParseComPort(string? portName)
	{
		var text = (portName ?? string.Empty).Trim();
		if (text.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
			text = text.Substring(3);
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 256)
			throw new InvalidDataException("Eksport do oryginalnego GUI wymaga portu COM1–COM256.");
		return port;
	}

	private static string OriginalLine(double value, string comment)
	{
		var scaled = checked((long)Math.Round(value * 1000.0, MidpointRounding.AwayFromZero));
		return $"{scaled,5}         '{comment}";
	}

	private static bool IsKeyValueCalibrationLine(string line)
	{
		var text = line.TrimStart();
		if (text.Length == 0 || text.StartsWith(';') || text.StartsWith('#'))
			return false;

		var equals = text.IndexOf('=');
		var comment = text.IndexOf('\'');
		return equals > 0 && (comment < 0 || equals < comment);
	}
}
