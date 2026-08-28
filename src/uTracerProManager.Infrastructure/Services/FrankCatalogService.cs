using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Services;

public sealed class FrankCatalogService
{
	private const string BundledCatalogFileName = "frank_datasheet_catalog.json";

	private static readonly string[] MirrorUrls = new string[2] { "https://tube-data.com/", "https://frank.pocnet.net/" };

	private static readonly Regex RowRegex = new Regex("<tr\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex CellRegex = new Regex("<td\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex PdfLinkRegex = new Regex("href=[\\\"'](?<href>[^\\\"']+\\.pdf)[\\\"'][^>]*>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

	private static readonly Regex TagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _httpClient;

	private readonly string _cacheDirectory;

	private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);

	private IReadOnlyList<FrankCatalogEntry>? _bundledCatalog;

	public string CacheDirectory => _cacheDirectory;

	public string BundledCatalogPath => Path.Combine(AppContext.BaseDirectory, "Data", "frank_datasheet_catalog.json");

	public int IndexedEntryCount { get; private set; }

	public string CatalogStatus { get; private set; } = "Pełny indeks lokalny nie został jeszcze wczytany.";

	public FrankCatalogService()
	{
		_httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(20.0)
		};
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("uTracer-PRO-Manager/1.1.24 (+full local Frank catalog)");
		_cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uTracerProManager", "FrankCatalog");
		Directory.CreateDirectory(_cacheDirectory);
	}

	public async Task<IReadOnlyList<FrankCatalogEntry>> SearchAsync(string tubeType, bool forceRefresh, CancellationToken cancellationToken = default(CancellationToken))
	{
		string normalized = NormalizeTubeType(tubeType);
		if (normalized.Length == 0)
		{
			return Array.Empty<FrankCatalogEntry>();
		}
		char first = normalized[0];
		if (!char.IsLetterOrDigit(first))
		{
			throw new InvalidOperationException("Typ lampy musi zaczynać się literą lub cyfrą.");
		}
		IReadOnlyList<FrankCatalogEntry> bundled = await LoadBundledCatalogAsync(cancellationToken);
		IReadOnlyList<FrankCatalogEntry> source = bundled;
		if (forceRefresh)
		{
			try
			{
				source = await DownloadIndexCharacterAsync(char.ToUpperInvariant(first), cancellationToken);
				CatalogStatus = $"Odświeżono wszystkie strony indeksu „{char.ToUpperInvariant(first)}”. Pełna baza lokalna: {IndexedEntryCount:N0} pozycji.";
			}
			catch (Exception ex) when (!(ex is OperationCanceledException) && bundled.Count > 0)
			{
				source = bundled;
				CatalogStatus = $"Odświeżenie internetowe nieudane; użyto pełnej bazy lokalnej ({IndexedEntryCount:N0} pozycji).";
			}
		}
		return (from entry in source.Where((FrankCatalogEntry entry) => Matches(entry, normalized)).DistinctBy((FrankCatalogEntry entry) => (entry.TubeType.ToUpperInvariant(), entry.Manufacturer.ToUpperInvariant(), entry.DataSheetUrl.ToUpperInvariant()))
			orderby MatchRank(entry, normalized)
			select entry).ThenBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.TubeType, StringComparer.OrdinalIgnoreCase).ThenBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.Manufacturer, StringComparer.OrdinalIgnoreCase).ThenBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.FileName, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public async Task<IReadOnlyList<FrankCatalogEntry>> SearchFavoritesAsync(string tubeType, IReadOnlySet<string> favoriteUrls, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (favoriteUrls.Count == 0)
		{
			return Array.Empty<FrankCatalogEntry>();
		}
		string normalized = NormalizeTubeType(tubeType);
		return (from entry in (await LoadBundledCatalogAsync(cancellationToken)).Where((FrankCatalogEntry entry) => favoriteUrls.Contains(entry.DataSheetUrl) && (normalized.Length == 0 || Matches(entry, normalized))).DistinctBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.DataSheetUrl, StringComparer.OrdinalIgnoreCase)
			orderby (normalized.Length != 0) ? MatchRank(entry, normalized) : 0
			select entry).ThenBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.TubeType, StringComparer.OrdinalIgnoreCase).ThenBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.Manufacturer, StringComparer.OrdinalIgnoreCase).ThenBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.FileName, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private async Task<IReadOnlyList<FrankCatalogEntry>> LoadBundledCatalogAsync(CancellationToken cancellationToken)
	{
		if (_bundledCatalog != null)
		{
			return _bundledCatalog;
		}
		await _loadLock.WaitAsync(cancellationToken);
		try
		{
			if (_bundledCatalog != null)
			{
				return _bundledCatalog;
			}
			if (!File.Exists(BundledCatalogPath))
			{
				_bundledCatalog = Array.Empty<FrankCatalogEntry>();
				IndexedEntryCount = 0;
				CatalogStatus = "Brak dołączonego pełnego indeksu Frank’s.";
				return _bundledCatalog;
			}
			IReadOnlyList<FrankCatalogEntry> bundledCatalog;
			await using (FileStream stream = File.OpenRead(BundledCatalogPath))
			{
				_bundledCatalog = ((await JsonSerializer.DeserializeAsync<List<FrankCatalogEntry>>(stream, JsonOptions, cancellationToken)) ?? new List<FrankCatalogEntry>()).Where((FrankCatalogEntry entry) => !string.IsNullOrWhiteSpace(entry.TubeType) && !string.IsNullOrWhiteSpace(entry.DataSheetUrl)).ToArray();
				IndexedEntryCount = _bundledCatalog.Count;
				CatalogStatus = $"Pełna baza lokalna Frank’s: {IndexedEntryCount:N0} pozycji.";
				bundledCatalog = _bundledCatalog;
			}
			return bundledCatalog;
		}
		finally
		{
			_loadLock.Release();
		}
	}

	private async Task<IReadOnlyList<FrankCatalogEntry>> DownloadIndexCharacterAsync(char indexCharacter, CancellationToken cancellationToken)
	{
		string item = $"sheets{indexCharacter}.html";
		Queue<string> pending = new Queue<string>();
		HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<FrankCatalogEntry> entries = new List<FrankCatalogEntry>();
		pending.Enqueue(item);
		Regex continuationRegex = new Regex("href=[\\\"'](?<page>sheets" + Regex.Escape(indexCharacter.ToString()) + "\\d*\\.html)[\\\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		while (pending.Count > 0)
		{
			string text = pending.Dequeue();
			if (!visited.Add(text))
			{
				continue;
			}
			var (text2, sourceUrl) = await GetIndexPageAsync(text, cancellationToken);
			entries.AddRange(ParseEntries(text2, sourceUrl));
			foreach (Match item2 in continuationRegex.Matches(text2))
			{
				string value = item2.Groups["page"].Value;
				if (!visited.Contains(value))
				{
					pending.Enqueue(value);
				}
			}
		}
		return entries;
	}

	private async Task<(string Html, string SourceUrl)> GetIndexPageAsync(string pageName, CancellationToken cancellationToken)
	{
		string cachePath = Path.Combine(_cacheDirectory, pageName);
		Exception inner = null;
		string[] mirrorUrls = MirrorUrls;
		foreach (string uriString in mirrorUrls)
		{
			string sourceUrl = new Uri(new Uri(uriString), pageName).ToString();
			try
			{
				string html = await _httpClient.GetStringAsync(sourceUrl, cancellationToken);
				await File.WriteAllTextAsync(cachePath, html, cancellationToken);
				return (Html: html, SourceUrl: sourceUrl);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				inner = ex;
			}
		}
		if (File.Exists(cachePath))
		{
			return (Html: await File.ReadAllTextAsync(cachePath, cancellationToken), SourceUrl: new Uri(new Uri(MirrorUrls[0]), pageName).ToString());
		}
		throw new HttpRequestException("Nie udało się pobrać strony " + pageName + " z serwerów Frank’s.", inner);
	}

	private static IEnumerable<FrankCatalogEntry> ParseEntries(string html, string sourceUrl)
	{
		string[] array = RowRegex.Split(html);
		foreach (string input in array)
		{
			string[] array2 = CellRegex.Split(input).Skip(1).Take(4)
				.ToArray();
			if (array2.Length < 4)
			{
				continue;
			}
			Match match = PdfLinkRegex.Match(array2[3]);
			if (!match.Success)
			{
				continue;
			}
			string text = CleanText(array2[0]);
			if (text.Length != 0)
			{
				string text2 = CleanText(array2[1]);
				string systemCode = CleanText(array2[2]);
				string relativeUri = WebUtility.HtmlDecode(match.Groups["href"].Value.Trim());
				string text3 = CleanText(match.Groups["text"].Value);
				string text4 = new Uri(new Uri(sourceUrl), relativeUri).ToString();
				if (text3.Length == 0)
				{
					text3 = Path.GetFileName(new Uri(text4).LocalPath);
				}
				yield return new FrankCatalogEntry(text, (text2.Length == 0) ? "Nie podano" : text2, systemCode, text4, text3, sourceUrl);
			}
		}
	}

	private static bool Matches(FrankCatalogEntry entry, string normalizedQuery)
	{
		string text = NormalizeTubeType(entry.TubeType);
		string text2 = NormalizeTubeType(Path.GetFileNameWithoutExtension(entry.FileName));
		string text3 = NormalizeTubeType(entry.Manufacturer);
		if (!text.Contains(normalizedQuery, StringComparison.Ordinal) && !text2.Contains(normalizedQuery, StringComparison.Ordinal))
		{
			return text3.Contains(normalizedQuery, StringComparison.Ordinal);
		}
		return true;
	}

	private static int MatchRank(FrankCatalogEntry entry, string normalizedQuery)
	{
		if (NormalizeTubeType(entry.TubeType.Split(new char[2] { ' ', '(' }, StringSplitOptions.RemoveEmptyEntries)[0]) == normalizedQuery)
		{
			return 0;
		}
		if (NormalizeTubeType(entry.TubeType).StartsWith(normalizedQuery, StringComparison.Ordinal))
		{
			return 1;
		}
		if (NormalizeTubeType(Path.GetFileNameWithoutExtension(entry.FileName)) == normalizedQuery)
		{
			return 2;
		}
		return 3;
	}

	private static string NormalizeTubeType(string value)
	{
		return new string(value.Trim().Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)
			.ToArray());
	}

	private static string CleanText(string value)
	{
		return Regex.Replace(WebUtility.HtmlDecode(TagRegex.Replace(value, " ")), "\\s+", " ").Trim();
	}
}
