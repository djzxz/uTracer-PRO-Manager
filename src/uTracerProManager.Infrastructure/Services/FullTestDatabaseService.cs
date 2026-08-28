using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Services;

public sealed class FullTestDatabaseService
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = false,
		PropertyNameCaseInsensitive = true
	};

	public string DatabasePath { get; }

	public FullTestDatabaseService(string databasePath)
	{
		DatabasePath = databasePath;
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
		await using SqliteConnection connection = CreateConnection();
		await connection.OpenAsync(cancellationToken);
		string commandText = "PRAGMA journal_mode=WAL;\nPRAGMA foreign_keys=ON;\n\nCREATE TABLE IF NOT EXISTS full_tests (\n    test_id TEXT PRIMARY KEY,\n    completed_at TEXT NOT NULL,\n    tube_inventory_number TEXT NOT NULL,\n    manufacturer TEXT NOT NULL,\n    production_code_part1 TEXT NOT NULL DEFAULT '',\n    production_code_part2 TEXT NOT NULL DEFAULT '',\n    declared_condition TEXT NOT NULL DEFAULT 'Nieznany',\n    test_mode TEXT NOT NULL DEFAULT 'FullDiagnostic',\n    section_match_percent REAL NOT NULL DEFAULT 0,\n    profile_id TEXT NOT NULL,\n    profile_name TEXT NOT NULL,\n    grade TEXT NOT NULL,\n    reliability TEXT NOT NULL,\n    condition_percent REAL NOT NULL,\n    valid_series INTEGER NOT NULL,\n    emulator INTEGER NOT NULL,\n    payload_json TEXT NOT NULL\n);\n\nCREATE INDEX IF NOT EXISTS idx_full_tests_completed_at\n    ON full_tests(completed_at DESC);\n\nCREATE INDEX IF NOT EXISTS idx_full_tests_inventory\n    ON full_tests(tube_inventory_number);\n\nCREATE TABLE IF NOT EXISTS full_test_samples (\n    test_id TEXT NOT NULL,\n    sequence INTEGER NOT NULL,\n    timestamp TEXT NOT NULL,\n    conditioning INTEGER NOT NULL,\n    anode_voltage REAL NOT NULL,\n    screen_voltage REAL NOT NULL,\n    grid_voltage REAL NOT NULL,\n    anode_current_ma REAL NOT NULL,\n    screen_current_ma REAL NOT NULL,\n    gm_ma_v REAL NOT NULL,\n    rp_kohm REAL NOT NULL,\n    mu REAL NOT NULL,\n    anode_power_w REAL NOT NULL,\n    averaging_index INTEGER NOT NULL,\n    is_outlier INTEGER NOT NULL,\n    action_after_sample TEXT NOT NULL,\n    raw_status TEXT NOT NULL,\n    commanded_anode_voltage REAL NOT NULL,\n    measured_anode_voltage REAL NOT NULL,\n    commanded_screen_voltage REAL NOT NULL,\n    measured_screen_voltage REAL NOT NULL,\n    section_b_gm_ma_v REAL NOT NULL DEFAULT 0,\n    section_b_rp_kohm REAL NOT NULL DEFAULT 0,\n    section_b_mu REAL NOT NULL DEFAULT 0,\n    section_b_power_w REAL NOT NULL DEFAULT 0,\n    measurement_label TEXT NOT NULL DEFAULT 'Punkt główny',\n    PRIMARY KEY(test_id, sequence),\n    FOREIGN KEY(test_id) REFERENCES full_tests(test_id)\n        ON DELETE CASCADE\n);";
		await using SqliteCommand command = connection.CreateCommand();
		command.CommandText = commandText;
		await command.ExecuteNonQueryAsync(cancellationToken);
		await EnsureColumnAsync(connection, "full_tests", "production_code_part1", "TEXT NOT NULL DEFAULT ''", cancellationToken);
		await EnsureColumnAsync(connection, "full_tests", "production_code_part2", "TEXT NOT NULL DEFAULT ''", cancellationToken);
		await EnsureColumnAsync(connection, "full_tests", "declared_condition", "TEXT NOT NULL DEFAULT 'Nieznany'", cancellationToken);
		await EnsureColumnAsync(connection, "full_tests", "test_mode", "TEXT NOT NULL DEFAULT 'FullDiagnostic'", cancellationToken);
		await EnsureColumnAsync(connection, "full_tests", "section_match_percent", "REAL NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "full_test_samples", "section_b_gm_ma_v", "REAL NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "full_test_samples", "section_b_rp_kohm", "REAL NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "full_test_samples", "section_b_mu", "REAL NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "full_test_samples", "section_b_power_w", "REAL NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "full_test_samples", "measurement_label", "TEXT NOT NULL DEFAULT 'Punkt główny'", cancellationToken);
		await using SqliteCommand indexCommand = connection.CreateCommand();
		indexCommand.CommandText = "CREATE INDEX IF NOT EXISTS idx_full_tests_code1 ON full_tests(production_code_part1);\nCREATE INDEX IF NOT EXISTS idx_full_tests_code2 ON full_tests(production_code_part2);";
		await indexCommand.ExecuteNonQueryAsync(cancellationToken);
		await using SqliteCommand immutableCommand = connection.CreateCommand();
		immutableCommand.CommandText = "CREATE TRIGGER IF NOT EXISTS full_tests_no_update\nBEFORE UPDATE ON full_tests\nBEGIN\n    SELECT RAISE(ABORT, 'Historia pomiarów jest tylko do odczytu. Utwórz nowy pomiar.');\nEND;\n\nCREATE TRIGGER IF NOT EXISTS full_tests_no_delete\nBEFORE DELETE ON full_tests\nBEGIN\n    SELECT RAISE(ABORT, 'Historia pomiarów jest tylko do odczytu.');\nEND;\n\nCREATE TRIGGER IF NOT EXISTS full_test_samples_no_update\nBEFORE UPDATE ON full_test_samples\nBEGIN\n    SELECT RAISE(ABORT, 'Próbki pomiarowe są niezmienne.');\nEND;\n\nCREATE TRIGGER IF NOT EXISTS full_test_samples_no_delete\nBEFORE DELETE ON full_test_samples\nBEGIN\n    SELECT RAISE(ABORT, 'Próbki pomiarowe są niezmienne.');\nEND;";
		await immutableCommand.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task SaveAsync(FullTestResult result, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(result, "result");
		await InitializeAsync(cancellationToken);
		await using SqliteConnection connection = CreateConnection();
		await connection.OpenAsync(cancellationToken);
		using SqliteTransaction transaction = connection.BeginTransaction();
		try
		{
			await using (SqliteCommand testCommand = connection.CreateCommand())
			{
				testCommand.Transaction = transaction;
				testCommand.CommandText = "INSERT INTO full_tests (\n    test_id,\n    completed_at,\n    tube_inventory_number,\n    manufacturer,\n    production_code_part1,\n    production_code_part2,\n    declared_condition,\n    test_mode,\n    section_match_percent,\n    profile_id,\n    profile_name,\n    grade,\n    reliability,\n    condition_percent,\n    valid_series,\n    emulator,\n    payload_json\n) VALUES (\n    $test_id,\n    $completed_at,\n    $tube_inventory_number,\n    $manufacturer,\n    $production_code_part1,\n    $production_code_part2,\n    $declared_condition,\n    $test_mode,\n    $section_match_percent,\n    $profile_id,\n    $profile_name,\n    $grade,\n    $reliability,\n    $condition_percent,\n    $valid_series,\n    $emulator,\n    $payload_json\n);";
				testCommand.Parameters.AddWithValue("$test_id", result.TestId.ToString("D"));
				testCommand.Parameters.AddWithValue("$completed_at", result.CompletedAt.ToString("O", CultureInfo.InvariantCulture));
				testCommand.Parameters.AddWithValue("$tube_inventory_number", result.TubeInventoryNumber);
				testCommand.Parameters.AddWithValue("$manufacturer", result.Manufacturer);
				testCommand.Parameters.AddWithValue("$production_code_part1", result.ProductionCodePart1);
				testCommand.Parameters.AddWithValue("$production_code_part2", result.ProductionCodePart2);
				testCommand.Parameters.AddWithValue("$declared_condition", result.DeclaredCondition);
				testCommand.Parameters.AddWithValue("$test_mode", result.TestMode.DisplayName());
				testCommand.Parameters.AddWithValue("$section_match_percent", result.DualComparison?.OverallMatchPercent ?? 0.0);
				testCommand.Parameters.AddWithValue("$profile_id", result.Profile.Id);
				testCommand.Parameters.AddWithValue("$profile_name", result.Profile.DisplayName);
				testCommand.Parameters.AddWithValue("$grade", result.Statistics.Grade);
				testCommand.Parameters.AddWithValue("$reliability", result.Statistics.Reliability);
				testCommand.Parameters.AddWithValue("$condition_percent", result.Statistics.OverallConditionPercent);
				testCommand.Parameters.AddWithValue("$valid_series", result.Statistics.ValidSeries);
				testCommand.Parameters.AddWithValue("$emulator", result.Emulator ? 1 : 0);
				testCommand.Parameters.AddWithValue("$payload_json", JsonSerializer.Serialize(result, JsonOptions));
				await testCommand.ExecuteNonQueryAsync(cancellationToken);
			}
			foreach (FullTestSample sample in result.Samples)
			{
				await using SqliteCommand testCommand = connection.CreateCommand();
				testCommand.Transaction = transaction;
				testCommand.CommandText = "INSERT INTO full_test_samples (\n    test_id,\n    sequence,\n    timestamp,\n    conditioning,\n    anode_voltage,\n    screen_voltage,\n    grid_voltage,\n    anode_current_ma,\n    screen_current_ma,\n    gm_ma_v,\n    rp_kohm,\n    mu,\n    anode_power_w,\n    averaging_index,\n    is_outlier,\n    action_after_sample,\n    raw_status,\n    commanded_anode_voltage,\n    measured_anode_voltage,\n    commanded_screen_voltage,\n    measured_screen_voltage,\n    section_b_gm_ma_v,\n    section_b_rp_kohm,\n    section_b_mu,\n    section_b_power_w,\n    measurement_label\n) VALUES (\n    $test_id,\n    $sequence,\n    $timestamp,\n    $conditioning,\n    $anode_voltage,\n    $screen_voltage,\n    $grid_voltage,\n    $anode_current_ma,\n    $screen_current_ma,\n    $gm_ma_v,\n    $rp_kohm,\n    $mu,\n    $anode_power_w,\n    $averaging_index,\n    $is_outlier,\n    $action_after_sample,\n    $raw_status,\n    $commanded_anode_voltage,\n    $measured_anode_voltage,\n    $commanded_screen_voltage,\n    $measured_screen_voltage,\n    $section_b_gm_ma_v,\n    $section_b_rp_kohm,\n    $section_b_mu,\n    $section_b_power_w,\n    $measurement_label\n);";
				testCommand.Parameters.AddWithValue("$test_id", result.TestId.ToString("D"));
				testCommand.Parameters.AddWithValue("$sequence", sample.Sequence);
				testCommand.Parameters.AddWithValue("$timestamp", sample.Timestamp.ToString("O", CultureInfo.InvariantCulture));
				testCommand.Parameters.AddWithValue("$conditioning", sample.Conditioning ? 1 : 0);
				testCommand.Parameters.AddWithValue("$anode_voltage", sample.AnodeVoltage);
				testCommand.Parameters.AddWithValue("$screen_voltage", sample.ScreenVoltage);
				testCommand.Parameters.AddWithValue("$grid_voltage", sample.GridVoltage);
				testCommand.Parameters.AddWithValue("$anode_current_ma", sample.AnodeCurrentMa);
				testCommand.Parameters.AddWithValue("$screen_current_ma", sample.ScreenCurrentMa);
				testCommand.Parameters.AddWithValue("$gm_ma_v", sample.GmMaV);
				testCommand.Parameters.AddWithValue("$rp_kohm", sample.RpKohm);
				testCommand.Parameters.AddWithValue("$mu", sample.Mu);
				testCommand.Parameters.AddWithValue("$anode_power_w", sample.AnodePowerW);
				testCommand.Parameters.AddWithValue("$averaging_index", sample.AveragingIndex);
				testCommand.Parameters.AddWithValue("$is_outlier", sample.IsOutlier ? 1 : 0);
				testCommand.Parameters.AddWithValue("$action_after_sample", sample.ActionAfterSample);
				testCommand.Parameters.AddWithValue("$raw_status", sample.RawStatus);
				testCommand.Parameters.AddWithValue("$commanded_anode_voltage", sample.CommandedAnodeVoltage);
				testCommand.Parameters.AddWithValue("$measured_anode_voltage", sample.MeasuredAnodeVoltage);
				testCommand.Parameters.AddWithValue("$commanded_screen_voltage", sample.CommandedScreenVoltage);
				testCommand.Parameters.AddWithValue("$measured_screen_voltage", sample.MeasuredScreenVoltage);
				testCommand.Parameters.AddWithValue("$section_b_gm_ma_v", sample.SectionBGmMaV);
				testCommand.Parameters.AddWithValue("$section_b_rp_kohm", sample.SectionBRpKohm);
				testCommand.Parameters.AddWithValue("$section_b_mu", sample.SectionBMu);
				testCommand.Parameters.AddWithValue("$section_b_power_w", sample.SectionBPowerW);
				testCommand.Parameters.AddWithValue("$measurement_label", sample.MeasurementLabel);
				await testCommand.ExecuteNonQueryAsync(cancellationToken);
			}
			transaction.Commit();
		}
		catch
		{
			transaction.Rollback();
			throw;
		}
	}

	public async Task<IReadOnlyList<StoredTestSummary>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await InitializeAsync(cancellationToken);
		IReadOnlyList<StoredTestSummary> result;
		await using (SqliteConnection connection = CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			IReadOnlyList<StoredTestSummary> readOnlyList2;
			await using (SqliteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT\n    test_id,\n    completed_at,\n    tube_inventory_number,\n    manufacturer,\n    production_code_part1,\n    production_code_part2,\n    declared_condition,\n    profile_name,\n    grade,\n    reliability,\n    condition_percent,\n    valid_series,\n    emulator,\n    test_mode,\n    section_match_percent\nFROM full_tests\nORDER BY completed_at DESC\nLIMIT $limit;";
				command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
				List<StoredTestSummary> results = new List<StoredTestSummary>();
				IReadOnlyList<StoredTestSummary> readOnlyList;
				await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new StoredTestSummary(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), reader.GetString(2), reader.GetString(3), ProductionCodePart1: reader.GetString(4), ProductionCodePart2: reader.GetString(5), DeclaredCondition: reader.GetString(6), ProfileName: reader.GetString(7), Grade: reader.GetString(8), Reliability: reader.GetString(9), ConditionPercent: reader.GetDouble(10), ValidSeries: reader.GetInt32(11), Emulator: reader.GetInt32(12) == 1, TestMode: reader.GetString(13), SectionMatchPercent: reader.GetDouble(14)));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<StoredTestSummary>> SearchAsync(string query, int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await InitializeAsync(cancellationToken);
		string pattern = "%" + (query ?? string.Empty).Trim() + "%";
		IReadOnlyList<StoredTestSummary> result;
		await using (SqliteConnection connection = CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			IReadOnlyList<StoredTestSummary> readOnlyList2;
			await using (SqliteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT test_id, completed_at, tube_inventory_number, manufacturer,\n       production_code_part1, production_code_part2, declared_condition,\n       profile_name, grade, reliability, condition_percent, valid_series, emulator,\n       test_mode, section_match_percent\nFROM full_tests\nWHERE $query = ''\n   OR tube_inventory_number LIKE $pattern COLLATE NOCASE\n   OR manufacturer LIKE $pattern COLLATE NOCASE\n   OR production_code_part1 LIKE $pattern COLLATE NOCASE\n   OR production_code_part2 LIKE $pattern COLLATE NOCASE\n   OR (production_code_part1 || ' ' || production_code_part2) LIKE $pattern COLLATE NOCASE\n   OR profile_name LIKE $pattern COLLATE NOCASE\nORDER BY completed_at DESC\nLIMIT $limit;";
				command.Parameters.AddWithValue("$query", (query ?? string.Empty).Trim());
				command.Parameters.AddWithValue("$pattern", pattern);
				command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
				List<StoredTestSummary> results = new List<StoredTestSummary>();
				IReadOnlyList<StoredTestSummary> readOnlyList;
				await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new StoredTestSummary(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), reader.GetString(2), reader.GetString(3), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetDouble(10), reader.GetInt32(11), reader.GetInt32(12) == 1, reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(13), reader.GetDouble(14)));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<FullTestResult?> LoadAsync(Guid testId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await InitializeAsync(cancellationToken);
		FullTestResult result;
		await using (SqliteConnection connection = CreateConnection())
		{
			await connection.OpenAsync(cancellationToken);
			FullTestResult fullTestResult;
			await using (SqliteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT payload_json FROM full_tests WHERE test_id=$test_id;";
				command.Parameters.AddWithValue("$test_id", testId.ToString("D"));
				fullTestResult = ((await command.ExecuteScalarAsync(cancellationToken) is string json) ? JsonSerializer.Deserialize<FullTestResult>(json, JsonOptions) : null);
			}
			result = fullTestResult;
		}
		return result;
	}

	private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
	{
		await using SqliteCommand check = connection.CreateCommand();
		check.CommandText = "PRAGMA table_info(" + table + ");";
		await using SqliteDataReader reader = await check.ExecuteReaderAsync(cancellationToken);
		do
		{
			if (!(await reader.ReadAsync(cancellationToken)))
			{
				await reader.DisposeAsync();
				await using (SqliteCommand alter = connection.CreateCommand())
				{
					alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
					await alter.ExecuteNonQueryAsync(cancellationToken);
				}
				break;
			}
		}
		while (!string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase));
	}

	private SqliteConnection CreateConnection()
	{
		return new SqliteConnection("Data Source=" + DatabasePath + ";Cache=Shared;Mode=ReadWriteCreate");
	}
}
