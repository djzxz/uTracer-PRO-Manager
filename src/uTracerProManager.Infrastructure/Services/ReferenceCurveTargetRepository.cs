using System.Globalization;
using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Services;

public sealed record ReferenceCurveTargetSeedResult(
    int PlannedSets,
    int ExpectedTargets,
    int InsertedTargets,
    int ExistingTargets);

/// <summary>
/// Brakujący punkt jest targetem, a nie zmyślonym pomiarem. Ia/Is pozostają NULL
/// do czasu prawdziwego pomiaru lub zatwierdzonej digitizacji źródła.
/// </summary>
public sealed class ReferenceCurveTargetRepository
{
    private readonly string _databasePath;

    public ReferenceCurveTargetRepository(string databasePath) =>
        _databasePath = Path.GetFullPath(databasePath);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE IF NOT EXISTS reference_curve_targets_v2 (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    curve_set_id TEXT NOT NULL,
    profile_id TEXT NOT NULL,
    section TEXT NOT NULL,
    target_kind TEXT NOT NULL,
    series_key TEXT NOT NULL,
    sequence INTEGER NOT NULL,
    va REAL NOT NULL,
    vs REAL NOT NULL,
    vg REAL NOT NULL,
    vh REAL NOT NULL DEFAULT 0,
    status TEXT NOT NULL,
    ia REAL NULL,
    is_current REAL NULL,
    source_status TEXT NOT NULL,
    source_page TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    completed_utc TEXT NULL,
    UNIQUE(curve_set_id, series_key, sequence)
);
CREATE INDEX IF NOT EXISTS ix_reference_curve_targets_v2_profile
ON reference_curve_targets_v2(profile_id, status, curve_set_id);
""";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ReferenceCurveTargetSeedResult> SeedFromLegacyPlansAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await TableExistsAsync(connection, "reference_curve_sets", cancellationToken))
            return new ReferenceCurveTargetSeedResult(0, 0, 0, 0);

        var existing = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM reference_curve_targets_v2;", cancellationToken);
        if (existing > 0)
            return new ReferenceCurveTargetSeedResult(0, existing, 0, existing);

        var plans = new List<LegacyPlan>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
SELECT id, profile_id, section_name, digitization_status, source_page,
       screen_voltage_v, va_start_v, va_stop_v, va_step_v, planned_grid_voltages
FROM reference_curve_sets
WHERE va_step_v > 0
  AND va_stop_v >= va_start_v
  AND trim(planned_grid_voltages) <> ''
ORDER BY profile_id, id;
""";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                plans.Add(new LegacyPlan(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetDouble(5),
                    reader.GetDouble(6), reader.GetDouble(7), reader.GetDouble(8), reader.GetString(9)));
            }
        }

        var expected = 0;
        var inserted = 0;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grids = ParseGridValues(plan.GridValues);
            if (grids.Count == 0)
                continue;

            var axis = BuildAxis(plan.VaStart, plan.VaStop, plan.VaStep);
            expected += grids.Count * axis.Count;
            var targetKind = TargetKind(plan.Status);

            foreach (var vg in grids)
            {
                for (var index = 0; index < axis.Count; index++)
                {
                    await using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = """
INSERT OR IGNORE INTO reference_curve_targets_v2(
    curve_set_id, profile_id, section, target_kind, series_key, sequence,
    va, vs, vg, vh, status, ia, is_current, source_status, source_page,
    created_utc, completed_utc)
VALUES(
    $set,$profile,$section,$kind,$series,$sequence,
    $va,$vs,$vg,0,'PENDING',NULL,NULL,$source_status,$page,$utc,NULL);
""";
                    insert.Parameters.AddWithValue("$set", plan.Id);
                    insert.Parameters.AddWithValue("$profile", plan.ProfileId);
                    insert.Parameters.AddWithValue("$section", string.IsNullOrWhiteSpace(plan.Section) ? "A" : plan.Section);
                    insert.Parameters.AddWithValue("$kind", targetKind);
                    insert.Parameters.AddWithValue("$series", "Vg=" + vg.ToString("0.######", CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$sequence", index + 1);
                    insert.Parameters.AddWithValue("$va", axis[index]);
                    insert.Parameters.AddWithValue("$vs", plan.Vs);
                    insert.Parameters.AddWithValue("$vg", vg);
                    insert.Parameters.AddWithValue("$source_status", plan.Status);
                    insert.Parameters.AddWithValue("$page", plan.SourcePage ?? string.Empty);
                    insert.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
                    inserted += await insert.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
        await transaction.CommitAsync(cancellationToken);

        return new ReferenceCurveTargetSeedResult(plans.Count, expected, inserted, existing);
    }

    public async Task<IReadOnlyList<ReferenceCurveTarget>> LoadPendingAsync(
        string profileId,
        int limit = 5000,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var result = new List<ReferenceCurveTarget>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, curve_set_id, profile_id, section, target_kind, series_key, sequence,
       va, vs, vg, vh, status, ia, is_current, source_status, source_page,
       created_utc, completed_utc
FROM reference_curve_targets_v2
WHERE profile_id=$profile AND status='PENDING'
ORDER BY curve_set_id, series_key, sequence
LIMIT $limit;
""";
        command.Parameters.AddWithValue("$profile", profileId);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ReferenceCurveTarget(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetInt32(6),
                reader.GetDouble(7), reader.GetDouble(8), reader.GetDouble(9), reader.GetDouble(10),
                reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetDouble(12),
                reader.IsDBNull(13) ? null : reader.GetDouble(13), reader.GetString(14), reader.GetString(15),
                DateTimeOffset.Parse(reader.GetString(16)),
                reader.IsDBNull(17) ? null : DateTimeOffset.Parse(reader.GetString(17))));
        }
        return result;
    }

    private static IReadOnlyList<double> ParseGridValues(string text)
    {
        var result = new List<double>();
        foreach (var token in text.Split(new[] { ';', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = token.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
                result.Add(value);
        }
        return result.Distinct().ToArray();
    }

    private static IReadOnlyList<double> BuildAxis(double start, double stop, double step)
    {
        if (step <= 0 || stop < start)
            return Array.Empty<double>();
        var result = new List<double>();
        for (var value = start; value <= stop + step * 0.001 && result.Count < 1000; value += step)
            result.Add(Math.Min(value, stop));
        if (result.Count == 0 || Math.Abs(result[^1] - stop) > 1e-6)
            result.Add(stop);
        return result.Distinct().ToArray();
    }

    private static string TargetKind(string status) =>
        status.Contains("MEASUREMENT", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("SWEEP_PLAN", StringComparison.OrdinalIgnoreCase)
            ? "MEASUREMENT_TARGET"
            : "DIGITIZATION_TARGET";

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string name, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name);";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) == 1;
    }

    private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(token));
    }

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString());

    private sealed record LegacyPlan(
        string Id, string ProfileId, string Section, string Status, string SourcePage,
        double Vs, double VaStart, double VaStop, double VaStep, string GridValues);
}
