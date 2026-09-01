using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Services;

public sealed class TubeMeasurementCatalogService
{
	private sealed record ProfileAvailabilityLookup(IReadOnlySet<string> ApprovedDatasheetUrls, IReadOnlySet<string> BlockedDatasheetUrls);

	private readonly string _databasePath;

	private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _profileAvailabilityLock = new SemaphoreSlim(1, 1);

	private TubeCatalogInfo? _cachedInfo;

	private readonly Dictionary<string, ProfileAvailabilityLookup> _profileAvailabilityByHardware =
		new(StringComparer.OrdinalIgnoreCase);

	public string ActiveDatabasePath => _databasePath;

	public string BackupDirectoryPath => Path.Combine(Path.GetDirectoryName(_databasePath), "BACKUP_BAZY");

	public TubeMeasurementCatalogService(string databasePath)
	{
		if (string.IsNullOrWhiteSpace(databasePath))
		{
			throw new ArgumentException("Ścieżka bazy nie może być pusta.", "databasePath");
		}
		_databasePath = Path.GetFullPath(databasePath);
	}

	public async Task<TubeCatalogInfo> EnsureReadyAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if ((object)_cachedInfo != null)
		{
			return _cachedInfo;
		}
		await _initializationLock.WaitAsync(cancellationToken);
		try
		{
			if ((object)_cachedInfo != null)
			{
				return _cachedInfo;
			}
			if (!File.Exists(_databasePath))
			{
				throw new FileNotFoundException("Brak pliku Data\\tube_measurements.db. Skopiuj otrzymaną bazę do folderu Data obok programu.", _databasePath);
			}
			_cachedInfo = await ReadInfoAsync(_databasePath, cancellationToken);
			return _cachedInfo;
		}
		finally
		{
			_initializationLock.Release();
		}
	}

	public async Task<IReadOnlyList<TubeProfile>> LoadAllAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await EnsureReadyAsync(cancellationToken);
		return await SearchAsync(string.Empty, "UTRACER3_PLUS_STOCK", cancellationToken);
	}

	public async Task<IReadOnlyList<TubeProfile>> SearchAsync(string query, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await SearchAsync(query, "UTRACER3_PLUS_STOCK", cancellationToken);
	}

	public async Task<IReadOnlyList<TubeProfile>> SearchAsync(
		string query,
		string hardwareId,
		CancellationToken cancellationToken = default)
	{
		await EnsureReadyAsync(cancellationToken);
		var profiles = await QueryAsync(query, cancellationToken);
		await AttachHardwareCompatibilityAsync(profiles, hardwareId, cancellationToken);
		return profiles;
	}

	public async Task<IReadOnlyList<FrankCatalogEntry>> SearchDatasheetsAsync(string query, string manufacturer = "", string tubeModel = "", int limit = 400, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await SearchDatasheetsForHardwareAsync(query, manufacturer, tubeModel, limit, "UTRACER3_PLUS_STOCK", cancellationToken);
	}

	public async Task<IReadOnlyList<FrankCatalogEntry>> SearchDatasheetsForHardwareAsync(
		string query,
		string manufacturer,
		string tubeModel,
		int limit,
		string hardwareId,
		CancellationToken cancellationToken = default)
	{
		await EnsureReadyAsync(cancellationToken);
		var entries = await QueryDatasheetsAsync(_databasePath, query, manufacturer, tubeModel,
			Math.Clamp(limit, 1, 5000), cancellationToken);
		return await AttachProfileAvailabilityAsync(entries, hardwareId, cancellationToken);
	}

	public async Task<IReadOnlyList<string>> LoadManufacturersAsync(string query = "", int limit = 500, CancellationToken cancellationToken = default(CancellationToken))
	{
		await EnsureReadyAsync(cancellationToken);
		string databasePath = _databasePath;
		string normalized = (query ?? string.Empty).Trim();
		IReadOnlyList<string> result;
		await using (SqliteConnection connection = CreateConnection(databasePath, readOnly: true))
		{
			await connection.OpenAsync(cancellationToken);
			IReadOnlyList<string> readOnlyList2;
			await using (SqliteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT manufacturer\nFROM frank_datasheets\nWHERE manufacturer <> ''\n  AND ($query = ''\n       OR manufacturer LIKE $pattern COLLATE NOCASE)\nGROUP BY manufacturer COLLATE NOCASE\nORDER BY manufacturer COLLATE NOCASE\nLIMIT $limit;";
				command.Parameters.AddWithValue("$query", normalized);
				command.Parameters.AddWithValue("$pattern", "%" + normalized + "%");
				command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
				List<string> results = new List<string>();
				IReadOnlyList<string> readOnlyList;
				await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(reader.GetString(0));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<string>> LoadModelsAsync(string manufacturer, string query = "", int limit = 5000, CancellationToken cancellationToken = default(CancellationToken))
	{
		await EnsureReadyAsync(cancellationToken);
		string databasePath = _databasePath;
		string selectedManufacturer = (manufacturer ?? string.Empty).Trim();
		string normalized = (query ?? string.Empty).Trim();
		if (selectedManufacturer.Length == 0 && normalized.Length == 0)
		{
			return Array.Empty<string>();
		}
		IReadOnlyList<string> result;
		await using (SqliteConnection connection = CreateConnection(databasePath, readOnly: true))
		{
			await connection.OpenAsync(cancellationToken);
			IReadOnlyList<string> readOnlyList2;
			await using (SqliteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT tube_type\nFROM frank_datasheets\nWHERE ($manufacturer = ''\n       OR manufacturer = $manufacturer COLLATE NOCASE)\n  AND ($query = ''\n       OR tube_type LIKE $pattern COLLATE NOCASE)\nGROUP BY tube_type COLLATE NOCASE\nORDER BY\n    CASE\n        WHEN tube_type = $query COLLATE NOCASE THEN 0\n        WHEN tube_type LIKE $prefix COLLATE NOCASE THEN 1\n        ELSE 2\n    END,\n    tube_type COLLATE NOCASE\nLIMIT $limit;";
				command.Parameters.AddWithValue("$manufacturer", selectedManufacturer);
				command.Parameters.AddWithValue("$query", normalized);
				command.Parameters.AddWithValue("$prefix", normalized + "%");
				command.Parameters.AddWithValue("$pattern", "%" + normalized + "%");
				command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 5000));
				List<string> results = new List<string>();
				IReadOnlyList<string> readOnlyList;
				await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(reader.GetString(0));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public Task<IReadOnlyList<TubeProfile>> FindMatchingProfilesAsync(
		string dataSheetUrl,
		CancellationToken cancellationToken)
	{
		return FindMatchingProfilesAsync(dataSheetUrl, string.Empty, string.Empty, cancellationToken);
	}

	public async Task<IReadOnlyList<TubeProfile>> FindMatchingProfilesAsync(
		string dataSheetUrl,
		string tubeType = "",
		string manufacturer = "",
		CancellationToken cancellationToken = default(CancellationToken))
	{
		return await FindMatchingProfilesForHardwareAsync(
			dataSheetUrl, tubeType, manufacturer, "UTRACER3_PLUS_STOCK", cancellationToken);
	}

	public async Task<IReadOnlyList<TubeProfile>> FindMatchingProfilesForHardwareAsync(
		string dataSheetUrl,
		string tubeType,
		string manufacturer,
		string hardwareId,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(dataSheetUrl))
		{
			return Array.Empty<TubeProfile>();
		}
		TubeCatalogInfo info = await EnsureReadyAsync(cancellationToken);
		string databasePath = _databasePath;
		IReadOnlyList<TubeProfile> result;
		await using (SqliteConnection connection = CreateConnection(databasePath, readOnly: true))
		{
			await connection.OpenAsync(cancellationToken);
			IReadOnlyList<TubeProfile> readOnlyList2;
			await using (SqliteCommand command = connection.CreateCommand())
			{
				command.CommandText = SupportsRecommendations(info)
					? "SELECT DISTINCT\n    profile.id, profile.display_name, profile.family,\n    profile.aliases_json, profile.tube_types,\n    profile.manufacturer_scope, profile.pinout,\n    profile.critical_warning,\n    profile.heater_voltage, profile.heater_current_amp,\n    profile.anode_voltage, profile.screen_voltage,\n    profile.grid_voltage,\n    profile.nominal_anode_current_ma,\n    profile.nominal_screen_current_ma,\n    profile.nominal_gm_ma_v, profile.nominal_mu,\n    profile.nominal_rp_kohm,\n    profile.max_anode_voltage, profile.max_screen_voltage,\n    profile.max_anode_power_w, profile.max_screen_power_w,\n    profile.anode_compliance_ma,\n    profile.screen_compliance_ma,\n    profile.warmup_seconds, profile.measurement_purpose,\n    profile.source_title, profile.source_url,\n    profile.source_page, profile.extraction_status,\n    profile.approved_for_hardware,\n    profile.counts_for_condition_percent,\n    profile.curve_va_start_v, profile.curve_va_stop_v,\n    profile.curve_va_step_v, profile.curve_grid_voltages,\n    profile.notes, profile.heater_supply_mode, profile.heater_supply_note,\n    'ZALECANY ' || recommendation.confidence || ': ' || recommendation.reason ||\n    ' | ' || compatibility.short_label || ' — ' || compatibility.reason\nFROM frank_datasheets AS datasheet\nINNER JOIN datasheet_profile_recommendations AS recommendation\n    ON recommendation.datasheet_id = datasheet.id\nINNER JOIN measurement_profiles AS profile\n    ON profile.id = recommendation.recommended_profile_id\nINNER JOIN profile_hardware_compatibility AS compatibility\n    ON compatibility.profile_id = profile.id\n   AND compatibility.hardware_id = $hardware\nWHERE datasheet.data_sheet_url = $url\n  AND ($tubeType = '' OR datasheet.tube_type = $tubeType COLLATE NOCASE)\n  AND ($manufacturer = '' OR datasheet.manufacturer = $manufacturer COLLATE NOCASE)\n  AND recommendation.decision = 'READY'\n  AND profile.approved_for_hardware = 1\n  AND compatibility.status <> 'BLOCKED'\nORDER BY profile.display_name COLLATE NOCASE;"
					: "SELECT DISTINCT\n    profile.id, profile.display_name, profile.family,\n    profile.aliases_json, profile.tube_types,\n    profile.manufacturer_scope, profile.pinout,\n    profile.critical_warning,\n    profile.heater_voltage, profile.heater_current_amp,\n    profile.anode_voltage, profile.screen_voltage,\n    profile.grid_voltage,\n    profile.nominal_anode_current_ma,\n    profile.nominal_screen_current_ma,\n    profile.nominal_gm_ma_v, profile.nominal_mu,\n    profile.nominal_rp_kohm,\n    profile.max_anode_voltage, profile.max_screen_voltage,\n    profile.max_anode_power_w, profile.max_screen_power_w,\n    profile.anode_compliance_ma,\n    profile.screen_compliance_ma,\n    profile.warmup_seconds, profile.measurement_purpose,\n    profile.source_title, profile.source_url,\n    profile.source_page, profile.extraction_status,\n    profile.approved_for_hardware,\n    profile.counts_for_condition_percent,\n    profile.curve_va_start_v, profile.curve_va_stop_v,\n    profile.curve_va_step_v, profile.curve_grid_voltages,\n    profile.notes, profile.heater_supply_mode, profile.heater_supply_note,\n    link.link_method\nFROM measurement_profiles AS profile\nINNER JOIN frank_profile_links AS link\n    ON link.profile_id = profile.id\nINNER JOIN frank_datasheets AS datasheet\n    ON datasheet.id = link.datasheet_id\nWHERE datasheet.data_sheet_url = $url\n  AND ($tubeType = '' OR datasheet.tube_type = $tubeType COLLATE NOCASE)\n  AND ($manufacturer = '' OR datasheet.manufacturer = $manufacturer COLLATE NOCASE)\nORDER BY\n         CASE\n             WHEN link.link_method LIKE 'KARTA ŹRÓDŁOWA:%' THEN 0\n             WHEN link.link_method LIKE 'ZGODNOŚĆ PRODUCENTA:%' THEN 1\n             ELSE 2\n         END,\n         profile.approved_for_hardware DESC,\n         profile.display_name COLLATE NOCASE;";
				command.Parameters.AddWithValue("$url", dataSheetUrl.Trim());
				command.Parameters.AddWithValue("$tubeType", (tubeType ?? string.Empty).Trim());
				command.Parameters.AddWithValue("$manufacturer", (manufacturer ?? string.Empty).Trim());
				command.Parameters.AddWithValue("$hardware", NormalizeHardwareId(hardwareId));
				List<TubeProfile> profiles = new List<TubeProfile>();
				IReadOnlyList<TubeProfile> readOnlyList;
				await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						profiles.Add(ReadProfile(reader, reader.GetString(39)));
					}
					readOnlyList = profiles;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		await AttachHardwareCompatibilityAsync(result, hardwareId, cancellationToken);
		return result;
	}

	public async Task<IReadOnlyList<FrankCatalogEntry>> LoadFavoriteDatasheetsAsync(IReadOnlyCollection<string> dataSheetUrls, CancellationToken cancellationToken = default(CancellationToken))
	{
		await EnsureReadyAsync(cancellationToken);
		if (dataSheetUrls.Count == 0)
		{
			return Array.Empty<FrankCatalogEntry>();
		}
		List<FrankCatalogEntry> results = new List<FrankCatalogEntry>();
		string datasheetDatabasePath = _databasePath;
		string[] source = dataSheetUrls.Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		foreach (string[] batch in source.Chunk(400))
		{
			await using SqliteConnection connection = CreateConnection(datasheetDatabasePath, readOnly: true);
			await connection.OpenAsync(cancellationToken);
			await using SqliteCommand command = connection.CreateCommand();
			List<string> list = new List<string>();
			for (int num = 0; num < batch.Length; num++)
			{
				string text = $"$url{num}";
				list.Add(text);
				command.Parameters.AddWithValue(text, batch[num]);
			}
			command.CommandText = "SELECT\n    tube_type, manufacturer, system_code,\n    data_sheet_url, file_name, source_page\nFROM frank_datasheets\nWHERE data_sheet_url IN (" + string.Join(",", list) + ")\nORDER BY tube_type COLLATE NOCASE,\n         manufacturer COLLATE NOCASE,\n         file_name COLLATE NOCASE;";
			await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				results.Add(ReadDatasheet(reader));
			}
		}
		FrankCatalogEntry[] entries = results.DistinctBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.DataSheetUrl, StringComparer.OrdinalIgnoreCase).OrderBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.TubeType, StringComparer.OrdinalIgnoreCase).ThenBy<FrankCatalogEntry, string>((FrankCatalogEntry entry) => entry.Manufacturer, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return await AttachProfileAvailabilityAsync(entries, "UTRACER3_PLUS_STOCK", cancellationToken);
	}

	public async Task<IReadOnlyList<FrankCatalogEntry>> AttachProfileAvailabilityAsync(IEnumerable<FrankCatalogEntry> entries, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await AttachProfileAvailabilityAsync(entries, "UTRACER3_PLUS_STOCK", cancellationToken);
	}

	public async Task<IReadOnlyList<FrankCatalogEntry>> AttachProfileAvailabilityAsync(
		IEnumerable<FrankCatalogEntry> entries,
		string hardwareId,
		CancellationToken cancellationToken = default)
	{
		ProfileAvailabilityLookup lookup = await GetProfileAvailabilityAsync(hardwareId, cancellationToken);
		return entries.Select((FrankCatalogEntry entry) => entry with
		{
			HasApprovedMeasurementProfile = lookup.ApprovedDatasheetUrls.Contains(AvailabilityKey(entry.DataSheetUrl, entry.TubeType, entry.Manufacturer)),
			HasBlockedMeasurementProfile = lookup.BlockedDatasheetUrls.Contains(AvailabilityKey(entry.DataSheetUrl, entry.TubeType, entry.Manufacturer))
		}).ToArray();
	}

	public async Task<TubeCatalogInfo> ImportDatabaseAsync(string sourcePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
		{
			throw new FileNotFoundException("Nie znaleziono wskazanej bazy.", sourcePath);
		}
		if (string.Equals(Path.GetFullPath(sourcePath), _databasePath, StringComparison.OrdinalIgnoreCase))
		{
			return await EnsureReadyAsync(cancellationToken);
		}
		await _initializationLock.WaitAsync(cancellationToken);
		string directoryName = Path.GetDirectoryName(_databasePath);
		Directory.CreateDirectory(directoryName);
		string temporary = Path.Combine(directoryName, $".tube_measurements_{Guid.NewGuid():N}.new");
		string backup = null;
		bool databaseReplaced = false;
		try
		{
			File.Copy(sourcePath, temporary, overwrite: true);
			TubeCatalogInfo sourceInfo = await ReadInfoAsync(temporary, cancellationToken);
			TubeCatalogInfo tubeCatalogInfo = _cachedInfo;
			if ((object)tubeCatalogInfo == null)
			{
				tubeCatalogInfo = await ReadInfoAsync(_databasePath, cancellationToken);
			}
			TubeCatalogInfo tubeCatalogInfo2 = tubeCatalogInfo;
			if (IsOlderCatalogVersion(sourceInfo.CatalogVersion, tubeCatalogInfo2.CatalogVersion))
			{
				throw new InvalidDataException($"Importowana baza v{sourceInfo.CatalogVersion} jest starsza od aktywnej bazy v{tubeCatalogInfo2.CatalogVersion}.");
			}
			if (File.Exists(_databasePath))
			{
				Directory.CreateDirectory(BackupDirectoryPath);
				backup = Path.Combine(BackupDirectoryPath, $"tube_measurements_backup_{tubeCatalogInfo2.CatalogVersion.Replace('.', '_')}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.db");
				File.Copy(_databasePath, backup, overwrite: false);
			}
			await ReplaceDatabaseFileAsync(temporary, _databasePath, cancellationToken);
			databaseReplaced = true;
			_cachedInfo = await ReadInfoAsync(_databasePath, cancellationToken);
			_profileAvailabilityByHardware.Clear();
			return _cachedInfo;
		}
		catch
		{
			if (databaseReplaced && backup != null && File.Exists(backup))
			{
				SqliteConnection.ClearAllPools();
				File.Copy(backup, _databasePath, overwrite: true);
				_cachedInfo = null;
					_profileAvailabilityByHardware.Clear();
			}
			throw;
		}
		finally
		{
			if (File.Exists(temporary))
			{
				SqliteConnection.ClearAllPools();
				try
				{
					File.Delete(temporary);
				}
				catch (IOException)
				{
				}
			}
			_initializationLock.Release();
		}
	}

	private async Task<ProfileAvailabilityLookup> GetProfileAvailabilityAsync(string hardwareId, CancellationToken cancellationToken)
	{
		hardwareId = NormalizeHardwareId(hardwareId);
		if (_profileAvailabilityByHardware.TryGetValue(hardwareId, out var cached))
			return cached;
		TubeCatalogInfo info = await EnsureReadyAsync(cancellationToken);
		await _profileAvailabilityLock.WaitAsync(cancellationToken);
		try
		{
			if (_profileAvailabilityByHardware.TryGetValue(hardwareId, out cached))
				return cached;
			HashSet<string> approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			HashSet<string> blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			ProfileAvailabilityLookup result;
			await using (SqliteConnection connection = CreateConnection(_databasePath, readOnly: true))
			{
				await connection.OpenAsync(cancellationToken);
				ProfileAvailabilityLookup profileAvailabilityLookup;
				await using (SqliteCommand command = connection.CreateCommand())
				{
					command.CommandText = SupportsRecommendations(info)
						? "SELECT\n    datasheet.data_sheet_url,\n    datasheet.tube_type,\n    datasheet.manufacturer,\n    CASE\n        WHEN recommendation.decision = 'READY'\n         AND profile.approved_for_hardware = 1\n         AND compatibility.status <> 'BLOCKED' THEN 1\n        ELSE 0\n    END\nFROM frank_datasheets AS datasheet\nINNER JOIN datasheet_profile_recommendations AS recommendation\n    ON recommendation.datasheet_id = datasheet.id\nLEFT JOIN measurement_profiles AS profile\n    ON profile.id = recommendation.recommended_profile_id\nLEFT JOIN profile_hardware_compatibility AS compatibility\n    ON compatibility.profile_id = profile.id\n   AND compatibility.hardware_id = $hardware;"
						: "SELECT\n    datasheet.data_sheet_url,\n    datasheet.tube_type,\n    datasheet.manufacturer,\n    profile.approved_for_hardware\nFROM frank_profile_links AS link\nINNER JOIN frank_datasheets AS datasheet\n    ON datasheet.id = link.datasheet_id\nINNER JOIN measurement_profiles AS profile\n    ON profile.id = link.profile_id;";
					command.Parameters.AddWithValue("$hardware", hardwareId);
					ProfileAvailabilityLookup profileAvailability;
					await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
					{
						while (await reader.ReadAsync(cancellationToken))
						{
							string key = AvailabilityKey(reader.GetString(0), reader.GetString(1), reader.GetString(2));
							if (reader.GetInt32(3) == 1)
							{
								approved.Add(key);
								blocked.Remove(key);
							}
							else if (!approved.Contains(key))
							{
								blocked.Add(key);
							}
						}
						profileAvailability = new ProfileAvailabilityLookup(approved, blocked);
						_profileAvailabilityByHardware[hardwareId] = profileAvailability;
					}
					profileAvailabilityLookup = profileAvailability;
				}
				result = profileAvailabilityLookup;
			}
			return result;
		}
		finally
		{
			_profileAvailabilityLock.Release();
		}
	}

	private async Task AttachHardwareCompatibilityAsync(
		IReadOnlyList<TubeProfile> profiles,
		string hardwareId,
		CancellationToken cancellationToken)
	{
		if (profiles.Count == 0)
			return;

		var info = await EnsureReadyAsync(cancellationToken);
		if (!SupportsRecommendations(info))
		{
			foreach (var profile in profiles)
			{
				profile.HardwareCompatibilityStatus = profile.ApprovedForHardware ? "FULL_CURVE" : "BLOCKED";
				profile.HardwareCompatibilityLabel = profile.ApprovedForHardware ? "GOTOWY" : "BLOKADA";
				profile.HardwareCompatibilityReason = "Starszy schemat bazy nie zawiera osobnej macierzy wariantów sprzętu.";
			}
			return;
		}

		hardwareId = NormalizeHardwareId(hardwareId);
		var byId = profiles
			.GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
		var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var batch in byId.Keys.Chunk(350))
		{
			await using var connection = CreateConnection(_databasePath, readOnly: true);
			await connection.OpenAsync(cancellationToken);
			await using var command = connection.CreateCommand();
			var parameters = new List<string>();
			for (var index = 0; index < batch.Length; index++)
			{
				var parameter = "$profile" + index;
				parameters.Add(parameter);
				command.Parameters.AddWithValue(parameter, batch[index]);
			}
			command.Parameters.AddWithValue("$hardware", hardwareId);
			command.CommandText = $"""
				SELECT profile_id, status, short_label, reason,
				       usable_curve_stop_v, usable_current_ma,
				       requires_manual_confirmation
				FROM profile_hardware_compatibility
				WHERE hardware_id = $hardware
				  AND profile_id IN ({string.Join(",", parameters)});
				""";
			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				var id = reader.GetString(0);
				found.Add(id);
				foreach (var profile in byId[id])
				{
					profile.HardwareCompatibilityStatus = reader.GetString(1);
					profile.HardwareCompatibilityLabel = reader.GetString(2);
					profile.HardwareCompatibilityReason = reader.GetString(3);
					profile.UsableCurveStopV = reader.GetDouble(4);
					profile.UsableCurrentMa = reader.GetDouble(5);
					profile.RequiresManualConfirmation = reader.GetInt32(6) == 1;
				}
			}
		}

		foreach (var (id, group) in byId)
		{
			if (found.Contains(id))
				continue;
			foreach (var profile in group)
			{
				profile.HardwareCompatibilityStatus = "BLOCKED";
				profile.HardwareCompatibilityLabel = "BRAK ZGODNOŚCI";
				profile.HardwareCompatibilityReason = "Baza nie zawiera zatwierdzonej macierzy zgodności dla wybranego wariantu sprzętu.";
				profile.UsableCurveStopV = 0;
				profile.UsableCurrentMa = 0;
				profile.RequiresManualConfirmation = true;
			}
		}
	}

	private async Task<IReadOnlyList<TubeProfile>> QueryAsync(string query, CancellationToken cancellationToken)
	{
		string normalized = (query ?? string.Empty).Trim();
		string pattern = "%" + normalized + "%";
		IReadOnlyList<TubeProfile> result;
		await using (SqliteConnection connection = CreateConnection(_databasePath, readOnly: true))
		{
			await connection.OpenAsync(cancellationToken);
			IReadOnlyList<TubeProfile> readOnlyList2;
			await using (SqliteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT\n    id, display_name, family, aliases_json, tube_types,\n    manufacturer_scope, pinout, critical_warning,\n    heater_voltage, heater_current_amp,\n    anode_voltage, screen_voltage, grid_voltage,\n    nominal_anode_current_ma, nominal_screen_current_ma,\n    nominal_gm_ma_v, nominal_mu, nominal_rp_kohm,\n    max_anode_voltage, max_screen_voltage,\n    max_anode_power_w, max_screen_power_w,\n    anode_compliance_ma, screen_compliance_ma,\n    warmup_seconds, measurement_purpose,\n    source_title, source_url, source_page, extraction_status,\n    approved_for_hardware, counts_for_condition_percent,\n    curve_va_start_v, curve_va_stop_v, curve_va_step_v,\n    curve_grid_voltages, notes, heater_supply_mode, heater_supply_note\nFROM measurement_profiles\nWHERE ($query = '' AND approved_for_hardware = 1)\n   OR ($query <> '' AND (\n          display_name LIKE $pattern COLLATE NOCASE\n       OR tube_types LIKE $pattern COLLATE NOCASE\n       OR aliases_json LIKE $pattern COLLATE NOCASE\n       OR family LIKE $pattern COLLATE NOCASE\n       OR manufacturer_scope LIKE $pattern COLLATE NOCASE\n       OR source_title LIKE $pattern COLLATE NOCASE\n   ))\nORDER BY approved_for_hardware DESC, display_name COLLATE NOCASE\nLIMIT 5000;";
				command.Parameters.AddWithValue("$query", normalized);
				command.Parameters.AddWithValue("$pattern", pattern);
				List<TubeProfile> results = new List<TubeProfile>();
				IReadOnlyList<TubeProfile> readOnlyList;
				await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadProfile(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	private static TubeProfile ReadProfile(SqliteDataReader reader, string compatibilityNote = "")
	{
		string[] aliases = JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? Array.Empty<string>();
		return new TubeProfile
		{
			Id = reader.GetString(0),
			DisplayName = reader.GetString(1),
			Family = reader.GetString(2),
			Aliases = aliases,
			TubeTypes = reader.GetString(4),
			ManufacturerScope = reader.GetString(5),
			Pinout = reader.GetString(6),
			CriticalWarning = reader.GetString(7),
			HeaterVoltage = reader.GetDouble(8),
			HeaterCurrentAmp = reader.GetDouble(9),
			AnodeVoltage = reader.GetDouble(10),
			ScreenVoltage = reader.GetDouble(11),
			GridVoltage = reader.GetDouble(12),
			NominalAnodeCurrentMa = reader.GetDouble(13),
			NominalScreenCurrentMa = reader.GetDouble(14),
			NominalGmMaV = reader.GetDouble(15),
			NominalMu = reader.GetDouble(16),
			NominalRpKohm = reader.GetDouble(17),
			MaxAnodeVoltage = reader.GetDouble(18),
			MaxScreenVoltage = reader.GetDouble(19),
			MaxAnodePowerW = reader.GetDouble(20),
			MaxScreenPowerW = reader.GetDouble(21),
			AnodeComplianceMa = reader.GetDouble(22),
			ScreenComplianceMa = reader.GetDouble(23),
			WarmupSeconds = reader.GetInt32(24),
			MeasurementPurpose = reader.GetString(25),
			SourceTitle = reader.GetString(26),
			SourceUrl = reader.GetString(27),
			SourcePage = reader.GetString(28),
			ExtractionStatus = reader.GetString(29),
			ApprovedForHardware = (reader.GetInt32(30) == 1),
			CountsForConditionPercent = (reader.GetInt32(31) == 1),
			CurveVaStartV = reader.GetDouble(32),
			CurveVaStopV = reader.GetDouble(33),
			CurveVaStepV = reader.GetDouble(34),
			CurveGridVoltages = reader.GetString(35),
			Notes = reader.GetString(36),
			HeaterSupplyMode = reader.FieldCount > 37 ? reader.GetString(37) : "INTERNAL_OK",
			HeaterSupplyNote = reader.FieldCount > 38 ? reader.GetString(38) : string.Empty,
			CatalogCompatibilityNote = compatibilityNote
		};
	}

	private async Task<IReadOnlyList<FrankCatalogEntry>> QueryDatasheetsAsync(string databasePath, string query, string manufacturer, string tubeModel, int limit, CancellationToken cancellationToken)
	{
		string normalized = (query ?? string.Empty).Trim();
		string selectedManufacturer = (manufacturer ?? string.Empty).Trim();
		string selectedModel = (tubeModel ?? string.Empty).Trim();
		if (normalized.Length == 0 && selectedManufacturer.Length == 0 && selectedModel.Length == 0)
		{
			return Array.Empty<FrankCatalogEntry>();
		}
		string pattern = "%" + normalized + "%";
		IReadOnlyList<FrankCatalogEntry> result;
		await using (SqliteConnection connection = CreateConnection(databasePath, readOnly: true))
		{
			await connection.OpenAsync(cancellationToken);
			IReadOnlyList<FrankCatalogEntry> readOnlyList2;
			await using (SqliteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT\n    tube_type, manufacturer, system_code,\n    data_sheet_url, file_name, source_page\nFROM frank_datasheets\nWHERE (\n        $query = ''\n        OR tube_type LIKE $pattern COLLATE NOCASE\n        OR manufacturer LIKE $pattern COLLATE NOCASE\n        OR system_code LIKE $pattern COLLATE NOCASE\n        OR file_name LIKE $pattern COLLATE NOCASE\n      )\n  AND (\n        $manufacturer = ''\n        OR manufacturer = $manufacturer COLLATE NOCASE\n      )\n  AND (\n        $model = ''\n        OR tube_type = $model COLLATE NOCASE\n      )\nORDER BY\n    CASE\n        WHEN $model <> ''\n             AND tube_type = $model COLLATE NOCASE THEN 0\n        WHEN $query <> ''\n             AND tube_type = $query COLLATE NOCASE THEN 1\n        WHEN $query <> ''\n             AND tube_type LIKE $prefix COLLATE NOCASE THEN 2\n        ELSE 3\n    END,\n    tube_type COLLATE NOCASE,\n    manufacturer COLLATE NOCASE,\n    file_name COLLATE NOCASE\nLIMIT $limit;";
				command.Parameters.AddWithValue("$query", normalized);
				command.Parameters.AddWithValue("$manufacturer", selectedManufacturer);
				command.Parameters.AddWithValue("$model", selectedModel);
				command.Parameters.AddWithValue("$prefix", normalized + "%");
				command.Parameters.AddWithValue("$pattern", pattern);
				command.Parameters.AddWithValue("$limit", limit);
				List<FrankCatalogEntry> results = new List<FrankCatalogEntry>();
				IReadOnlyList<FrankCatalogEntry> readOnlyList;
				await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadDatasheet(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	private static FrankCatalogEntry ReadDatasheet(SqliteDataReader reader)
	{
		return new FrankCatalogEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
	}

	private static async Task ReplaceDatabaseFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
	{
		for (int attempt = 1; attempt <= 10; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			SqliteConnection.ClearAllPools();
			try
			{
				File.Move(sourcePath, destinationPath, overwrite: true);
				return;
			}
			catch (IOException) when (attempt < 10)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
			}
		}
		throw new IOException("Nie udało się podmienić bazy. Zamknij pozostałe uruchomione kopie programu i spróbuj ponownie.");
	}

	private static async Task<TubeCatalogInfo> ReadInfoAsync(string databasePath, CancellationToken cancellationToken)
	{
		TubeCatalogInfo result2;
		await using (SqliteConnection connection = CreateConnection(databasePath, readOnly: true))
		{
			await connection.OpenAsync(cancellationToken);
			TubeCatalogInfo tubeCatalogInfo10;
			await using (SqliteCommand integrityCheck = connection.CreateCommand())
			{
				integrityCheck.CommandText = "PRAGMA quick_check;";
				string text = Convert.ToString(await integrityCheck.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
				if (!string.Equals(text, "ok", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Baza SQLite nie przeszła kontroli integralności: " + text);
				}
				TubeCatalogInfo tubeCatalogInfo9;
				await using (SqliteCommand schemaCheck = connection.CreateCommand())
				{
					schemaCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master\nWHERE type='table'\n  AND name IN (\n      'catalog_info',\n      'measurement_profiles',\n      'frank_datasheets',\n      'frank_profile_links'\n  );";
					if (Convert.ToInt64(await schemaCheck.ExecuteScalarAsync(cancellationToken)) != 4)
					{
						throw new InvalidDataException("Baza nie zawiera wszystkich wymaganych tabel katalogu.");
					}
					Dictionary<string, string> info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					TubeCatalogInfo tubeCatalogInfo8;
					await using (SqliteCommand command = connection.CreateCommand())
					{
						command.CommandText = "SELECT key, value FROM catalog_info;";
						TubeCatalogInfo tubeCatalogInfo7;
						await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
						{
							while (await reader.ReadAsync(cancellationToken))
							{
								info[reader.GetString(0)] = reader.GetString(1);
							}
							string schemaVersionText = info.GetValueOrDefault("schema_version", string.Empty);
							if (!int.TryParse(schemaVersionText, out int schemaVersion) || schemaVersion is not (2 or 7))
							{
								throw new InvalidDataException("Nieobsługiwana wersja schematu bazy. Program obsługuje schemat v2 i pełny schemat v7.");
							}
							bool usesRecommendations = schemaVersion >= 7;
							string catalogVersion = info.GetValueOrDefault("catalog_version", "");
							if (!Version.TryParse(catalogVersion, out Version _))
							{
								throw new InvalidDataException("Baza nie zawiera prawidłowej wersji katalogu.");
							}
							TubeCatalogInfo tubeCatalogInfo6;
							await using (SqliteCommand countCommand = connection.CreateCommand())
							{
								countCommand.CommandText = "SELECT COUNT(*) FROM measurement_profiles;";
								int count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
								TubeCatalogInfo tubeCatalogInfo5;
								await using (SqliteCommand readyCountCommand = connection.CreateCommand())
								{
									readyCountCommand.CommandText = "SELECT COUNT(*) FROM measurement_profiles WHERE approved_for_hardware=1;";
									int readyCount = Convert.ToInt32(await readyCountCommand.ExecuteScalarAsync(cancellationToken));
									if (count <= 0 || readyCount <= 0 || readyCount > count)
									{
										throw new InvalidDataException("Baza nie zawiera prawidłowego zestawu profili pomiarowych.");
									}
									TubeCatalogInfo tubeCatalogInfo4;
									await using (SqliteCommand catalogMetricsCommand = connection.CreateCommand())
									{
										catalogMetricsCommand.CommandText = usesRecommendations
											? "SELECT\n    (SELECT COUNT(*) FROM frank_datasheets),\n    (SELECT COUNT(*) FROM (\n        SELECT tube_type\n        FROM frank_datasheets\n        GROUP BY tube_type COLLATE NOCASE\n    )),\n    (SELECT COUNT(*) FROM (\n        SELECT manufacturer\n        FROM frank_datasheets\n        WHERE manufacturer <> ''\n        GROUP BY manufacturer COLLATE NOCASE\n    )),\n    (SELECT COUNT(*) FROM datasheet_profile_recommendations),\n    (SELECT COUNT(*) FROM datasheet_profile_recommendations WHERE decision = 'READY'),\n    (SELECT COUNT(*) FROM (\n        SELECT datasheet.normalized_type\n        FROM datasheet_profile_recommendations AS recommendation\n        INNER JOIN frank_datasheets AS datasheet\n            ON datasheet.id = recommendation.datasheet_id\n        GROUP BY datasheet.normalized_type COLLATE NOCASE\n    )),\n    (SELECT COUNT(*) FROM (\n        SELECT datasheet.tube_type\n        FROM datasheet_profile_recommendations AS recommendation\n        INNER JOIN frank_datasheets AS datasheet\n            ON datasheet.id = recommendation.datasheet_id\n        WHERE recommendation.decision = 'READY'\n        GROUP BY datasheet.tube_type COLLATE NOCASE\n    ));"
											: "SELECT\n    (SELECT COUNT(*) FROM frank_datasheets),\n    (SELECT COUNT(*) FROM (\n        SELECT tube_type\n        FROM frank_datasheets\n        GROUP BY tube_type COLLATE NOCASE\n    )),\n    (SELECT COUNT(*) FROM (\n        SELECT manufacturer\n        FROM frank_datasheets\n        WHERE manufacturer <> ''\n        GROUP BY manufacturer COLLATE NOCASE\n    )),\n    (SELECT COUNT(DISTINCT datasheet_id)\n        FROM frank_profile_links),\n    (SELECT COUNT(DISTINCT link.datasheet_id)\n        FROM frank_profile_links AS link\n        INNER JOIN measurement_profiles AS profile\n            ON profile.id = link.profile_id\n        WHERE profile.approved_for_hardware = 1),\n    (SELECT COUNT(*) FROM (\n        SELECT datasheet.tube_type\n        FROM frank_profile_links AS link\n        INNER JOIN frank_datasheets AS datasheet\n            ON datasheet.id = link.datasheet_id\n        GROUP BY datasheet.tube_type COLLATE NOCASE\n    )),\n    (SELECT COUNT(*) FROM (\n        SELECT datasheet.tube_type\n        FROM frank_profile_links AS link\n        INNER JOIN frank_datasheets AS datasheet\n            ON datasheet.id = link.datasheet_id\n        INNER JOIN measurement_profiles AS profile\n            ON profile.id = link.profile_id\n        WHERE profile.approved_for_hardware = 1\n        GROUP BY datasheet.tube_type COLLATE NOCASE\n    ));";
										TubeCatalogInfo tubeCatalogInfo3;
										await using (SqliteDataReader metricsReader = await catalogMetricsCommand.ExecuteReaderAsync(cancellationToken))
										{
											if (!(await metricsReader.ReadAsync(cancellationToken)))
											{
												throw new InvalidDataException("Nie udało się odczytać metryk pełnego katalogu.");
											}
											int datasheetCount = metricsReader.GetInt32(0);
											int modelCount = metricsReader.GetInt32(1);
											int manufacturerCount = metricsReader.GetInt32(2);
											int linkedDatasheetCount = metricsReader.GetInt32(3);
											int readyDatasheetCount = metricsReader.GetInt32(4);
											int linkedModelCount = metricsReader.GetInt32(5);
											int readyModelCount = metricsReader.GetInt32(6);
											if (datasheetCount <= 0 || modelCount <= 0 || manufacturerCount <= 0)
											{
												throw new InvalidDataException("Baza nie zawiera pełnego katalogu modeli i producentów.");
											}
											foreach (var (text3, num2) in new Dictionary<string, int>
											{
												["profile_count"] = count,
												["ready_profile_count"] = readyCount,
												["datasheet_count"] = datasheetCount,
												["model_count"] = modelCount,
												["manufacturer_count"] = manufacturerCount,
												["linked_datasheet_count"] = linkedDatasheetCount,
												["ready_datasheet_count"] = readyDatasheetCount,
												["linked_model_count"] = linkedModelCount,
												["ready_model_count"] = readyModelCount
											})
											{
												if (ReadInfoNumber(info, text3) != num2)
												{
													throw new InvalidDataException("Metadane katalogu nie odpowiadają zawartości bazy: " + text3 + ".");
												}
											}
											TubeCatalogInfo tubeCatalogInfo2;
											await using (SqliteCommand orphanCheck = connection.CreateCommand())
											{
												orphanCheck.CommandText = "SELECT COUNT(*)\nFROM frank_profile_links AS link\nLEFT JOIN frank_datasheets AS datasheet\n    ON datasheet.id = link.datasheet_id\nLEFT JOIN measurement_profiles AS profile\n    ON profile.id = link.profile_id\nWHERE datasheet.id IS NULL OR profile.id IS NULL;";
													if (Convert.ToInt64(await orphanCheck.ExecuteScalarAsync(cancellationToken)) != 0L)
													{
														throw new InvalidDataException("Baza zawiera osierocone powiązania kart z profilami.");
													}
													if (usesRecommendations)
													{
														await using SqliteCommand recommendationCheck = connection.CreateCommand();
														recommendationCheck.CommandText = "SELECT COUNT(*)\nFROM datasheet_profile_recommendations AS recommendation\nLEFT JOIN measurement_profiles AS profile\n    ON profile.id = recommendation.recommended_profile_id\nLEFT JOIN profile_hardware_compatibility AS compatibility\n    ON compatibility.profile_id = profile.id\n   AND compatibility.hardware_id = 'UTRACER3_PLUS_STOCK'\nWHERE recommendation.decision NOT IN ('READY', 'BLOCKED')\n   OR (recommendation.decision = 'READY' AND (\n          recommendation.recommended_profile_id IS NULL\n       OR profile.id IS NULL\n       OR profile.approved_for_hardware <> 1\n       OR compatibility.profile_id IS NULL\n       OR compatibility.status = 'BLOCKED'\n   ));";
														if (Convert.ToInt64(await recommendationCheck.ExecuteScalarAsync(cancellationToken)) != 0L)
														{
															throw new InvalidDataException("Baza zawiera niespójną rekomendację profilu lub niezgodność z fabrycznym uTracerem 3+.");
														}
													}
												TubeCatalogInfo tubeCatalogInfo;
												await using (SqliteCommand unsafeProfileCheck = connection.CreateCommand())
												{
													unsafeProfileCheck.CommandText = "SELECT COUNT(*)\nFROM measurement_profiles\nWHERE heater_voltage <= 0\n   OR heater_current_amp <= 0\n   OR anode_compliance_ma <= 0\n   OR anode_compliance_ma > 200\n   OR screen_compliance_ma > 200\n   OR anode_voltage > max_anode_voltage\n   OR screen_voltage > max_screen_voltage\n   OR source_title = ''\n   OR source_url = ''\n   OR source_page = ''\n   OR (counts_for_condition_percent = 1\n       AND (nominal_anode_current_ma <= 0\n            OR nominal_gm_ma_v <= 0));";
													if (Convert.ToInt64(await unsafeProfileCheck.ExecuteScalarAsync(cancellationToken)) != 0L)
													{
														throw new InvalidDataException("Baza zawiera niekompletny albo niebezpieczny profil.");
													}
													tubeCatalogInfo = new TubeCatalogInfo(info.GetValueOrDefault("schema_version", "?"), catalogVersion, info.GetValueOrDefault("source", "Nie podano"), count, readyCount, datasheetCount, modelCount, manufacturerCount, linkedDatasheetCount, readyDatasheetCount, linkedModelCount, readyModelCount, databasePath);
												}
												tubeCatalogInfo2 = tubeCatalogInfo;
											}
											tubeCatalogInfo3 = tubeCatalogInfo2;
										}
										tubeCatalogInfo4 = tubeCatalogInfo3;
									}
									tubeCatalogInfo5 = tubeCatalogInfo4;
								}
								tubeCatalogInfo6 = tubeCatalogInfo5;
							}
							tubeCatalogInfo7 = tubeCatalogInfo6;
						}
						tubeCatalogInfo8 = tubeCatalogInfo7;
					}
					tubeCatalogInfo9 = tubeCatalogInfo8;
				}
				tubeCatalogInfo10 = tubeCatalogInfo9;
			}
			result2 = tubeCatalogInfo10;
		}
		return result2;
	}

	private static int ReadInfoNumber(IReadOnlyDictionary<string, string> info, string key)
	{
		if (!int.TryParse(info.GetValueOrDefault(key), out var result))
		{
			return 0;
		}
		return result;
	}

	private static bool SupportsRecommendations(TubeCatalogInfo info)
	{
		return int.TryParse(info.SchemaVersion, out int schemaVersion) && schemaVersion >= 7;
	}

	private static string AvailabilityKey(string dataSheetUrl, string tubeType, string manufacturer)
	{
		return string.Join("|",
			(dataSheetUrl ?? string.Empty).Trim().ToUpperInvariant(),
			(tubeType ?? string.Empty).Trim().ToUpperInvariant(),
			(manufacturer ?? string.Empty).Trim().ToUpperInvariant());
	}

	private static string NormalizeHardwareId(string? hardwareId) =>
		string.IsNullOrWhiteSpace(hardwareId) ? "UTRACER3_PLUS_STOCK" : hardwareId.Trim();

	private static SqliteConnection CreateConnection(string path, bool readOnly)
	{
		return new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = path,
			Mode = (readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate),
			Cache = SqliteCacheMode.Private,
			Pooling = false
		}.ToString());
	}

	private static bool IsOlderCatalogVersion(string current, string required)
	{
		if (Version.TryParse(current, out Version result) && Version.TryParse(required, out Version result2))
		{
			return result < result2;
		}
		return false;
	}
}
