using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace uTracerProManager.Services;

public sealed class TubeFavoritesService
{
	private sealed class FavoriteFile
	{
		public int SchemaVersion { get; init; } = 1;

		public List<string> ProfileIds { get; init; } = new List<string>();

		public List<string> DatasheetUrls { get; init; } = new List<string>();
	}

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private readonly string _filePath;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private HashSet<string>? _profileIds;

	private HashSet<string>? _datasheetUrls;

	public string FilePath => _filePath;

	public TubeFavoritesService()
	{
		_filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uTracerProManager", "Catalog", "favorites.json");
	}

	public async Task<TubeFavoritesSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await EnsureLoadedLockedAsync(cancellationToken);
			return CreateSnapshotLocked();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<bool> ToggleProfileAsync(string profileId, CancellationToken cancellationToken = default(CancellationToken))
	{
		string normalized = RequireValue(profileId, "profileId");
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await EnsureLoadedLockedAsync(cancellationToken);
			bool isFavorite = !_profileIds.Remove(normalized);
			if (isFavorite)
			{
				_profileIds.Add(normalized);
			}
			await SaveLockedAsync(cancellationToken);
			return isFavorite;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<bool> ToggleDatasheetAsync(string dataSheetUrl, CancellationToken cancellationToken = default(CancellationToken))
	{
		string normalized = NormalizeDatasheetUrl(RequireValue(dataSheetUrl, "dataSheetUrl"));
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await EnsureLoadedLockedAsync(cancellationToken);
			bool isFavorite = !_datasheetUrls.Remove(normalized);
			if (isFavorite)
			{
				_datasheetUrls.Add(normalized);
			}
			await SaveLockedAsync(cancellationToken);
			return isFavorite;
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task EnsureLoadedLockedAsync(CancellationToken cancellationToken)
	{
		if (_profileIds != null && _datasheetUrls != null)
		{
			return;
		}
		FavoriteFile data = new FavoriteFile();
		if (File.Exists(_filePath))
		{
			try
			{
				await using FileStream stream = File.OpenRead(_filePath);
				data = (await JsonSerializer.DeserializeAsync<FavoriteFile>(stream, JsonOptions, cancellationToken)) ?? new FavoriteFile();
			}
			catch (JsonException)
			{
				string destFileName = _filePath + $".invalid_{DateTime.Now:yyyyMMdd_HHmmss}";
				File.Copy(_filePath, destFileName, overwrite: false);
				data = new FavoriteFile();
			}
		}
		_profileIds = new HashSet<string>(data.ProfileIds.Where((string value) => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
		_datasheetUrls = new HashSet<string>(data.DatasheetUrls.Where((string value) => !string.IsNullOrWhiteSpace(value)).Select(NormalizeDatasheetUrl), StringComparer.OrdinalIgnoreCase);
	}

	private async Task SaveLockedAsync(CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
		FavoriteFile value = new FavoriteFile
		{
			ProfileIds = _profileIds.OrderBy<string, string>((string result) => result, StringComparer.OrdinalIgnoreCase).ToList(),
			DatasheetUrls = _datasheetUrls.OrderBy<string, string>((string result) => result, StringComparer.OrdinalIgnoreCase).ToList()
		};
		string temporaryPath = _filePath + ".new";
		await using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
			await stream.FlushAsync(cancellationToken);
		}
		File.Move(temporaryPath, _filePath, overwrite: true);
	}

	private TubeFavoritesSnapshot CreateSnapshotLocked()
	{
		return new TubeFavoritesSnapshot(new HashSet<string>(_profileIds, StringComparer.OrdinalIgnoreCase), new HashSet<string>(_datasheetUrls, StringComparer.OrdinalIgnoreCase));
	}

	private static string RequireValue(string value, string parameterName)
	{
		string text = value.Trim();
		if (text.Length == 0)
		{
			throw new ArgumentException("Wartość ulubionego rekordu nie może być pusta.", parameterName);
		}
		return text;
	}

	public static string NormalizeDatasheetUrl(string value)
	{
		string text = value.Trim();
		if (!Uri.TryCreate(text, UriKind.Absolute, out Uri result))
		{
			return text;
		}
		return new Uri(new Uri("https://tube-data.com/"), result.PathAndQuery.TrimStart('/')).ToString();
	}
}
