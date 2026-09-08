using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Services;

namespace uTracerProManager.Services;

public sealed record LegacyCurveMigrationResult(
    int LegacySetsWithPoints,
    int LegacyPoints,
    int AddedSeries,
    int AddedPoints,
    int SkippedExistingSeries);

/// <summary>
/// Kopiuje istniejące, źródłowe punkty z reference_curve_sets/reference_curve_points
/// do modelu v2. Nie usuwa ani nie aktualizuje tabel legacy i nie generuje brakujących
/// punktów przez interpolację. Jednopunktowy wpis pozostaje DOCUMENTED_POINT.
/// </summary>
public sealed class LegacyReferenceCurveMigrationService
{
    private readonly string _databasePath;

    public LegacyReferenceCurveMigrationService(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public async Task<LegacyCurveMigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var repository = new ReferenceCurveRepository(_databasePath);
        await repository.InitializeAsync(cancellationToken);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        await connection.OpenAsync(cancellationToken);

        if (!await TableExistsAsync(connection, "reference_curve_sets", cancellationToken) ||
            !await TableExistsAsync(connection, "reference_curve_points", cancellationToken))
            return new LegacyCurveMigrationResult(0, 0, 0, 0, 0);

        var legacySetCount = await ScalarIntAsync(connection, """
SELECT COUNT(*) FROM (
    SELECT curve_set_id FROM reference_curve_points GROUP BY curve_set_id
);
""", cancellationToken);
        var legacyPointCount = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM reference_curve_points;", cancellationToken);

        var addedSeries = 0;
        var addedPoints = 0;
        var skipped = 0;

        var sets = new List<LegacySet>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
SELECT s.id, s.profile_id, s.curve_kind, s.section_name, s.display_name,
       s.digitization_status, s.source_title, s.source_url, s.source_page
FROM reference_curve_sets s
WHERE EXISTS (SELECT 1 FROM reference_curve_points p WHERE p.curve_set_id=s.id)
ORDER BY s.profile_id, s.id;
""";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sets.Add(new LegacySet(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetString(6), reader.GetString(7), reader.GetString(8)));
            }
        }

        foreach (var set in sets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetId = "LEGACY:" + set.Id;
            if (await SeriesExistsAsync(connection, targetId, cancellationToken))
            {
                skipped++;
                continue;
            }

            var points = await LoadPointsAsync(connection, set.Id, cancellationToken);
            if (points.Count == 0)
                continue;

            var humanReviewed = set.DigitizationStatus.Contains("DOCUMENTED", StringComparison.OrdinalIgnoreCase) ||
                                set.DigitizationStatus.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase);
            var status = points.Count >= 3 && humanReviewed ? "READY" : set.DigitizationStatus;
            var quality = humanReviewed ? (points.Count >= 3 ? 0.90 : 0.75) : 0.50;

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var insertSeries = connection.CreateCommand())
            {
                insertSeries.Transaction = transaction;
                insertSeries.CommandText = """
INSERT INTO reference_curve_series_v2(
    id, profile_id, section, source_kind, source_title, source_url, source_page,
    mode, captured_utc, digitization_status, quality, extraction_version,
    human_reviewed, created_utc)
VALUES($id,$profile,$section,'CATALOG',$title,$url,$page,$mode,$captured,$status,$quality,'LEGACY_CP78', $reviewed,$created);
""";
                insertSeries.Parameters.AddWithValue("$id", targetId);
                insertSeries.Parameters.AddWithValue("$profile", set.ProfileId);
                insertSeries.Parameters.AddWithValue("$section", string.IsNullOrWhiteSpace(set.Section) ? "A" : set.Section);
                insertSeries.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(set.SourceTitle) ? set.DisplayName : set.SourceTitle);
                insertSeries.Parameters.AddWithValue("$url", set.SourceUrl ?? string.Empty);
                insertSeries.Parameters.AddWithValue("$page", set.SourcePage ?? string.Empty);
                insertSeries.Parameters.AddWithValue("$mode", set.CurveKind);
                insertSeries.Parameters.AddWithValue("$captured", DateTimeOffset.UnixEpoch.ToString("O"));
                insertSeries.Parameters.AddWithValue("$status", status);
                insertSeries.Parameters.AddWithValue("$quality", quality);
                insertSeries.Parameters.AddWithValue("$reviewed", humanReviewed ? 1 : 0);
                insertSeries.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
                await insertSeries.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var point in points)
            {
                await using var insertPoint = connection.CreateCommand();
                insertPoint.Transaction = transaction;
                insertPoint.CommandText = """
INSERT INTO reference_curve_points_v2(
    series_id, sequence, series_key, va_set, va_measured, vs_set, vs_measured,
    vg, vh, ia, is_current, gm, rp, compliance_status, interpolated)
VALUES($series,$sequence,$key,$va,$va,$vs,$vs,$vg,0,$ia,$is,NULL,NULL,'OK',0);
""";
                insertPoint.Parameters.AddWithValue("$series", targetId);
                insertPoint.Parameters.AddWithValue("$sequence", point.Sequence);
                insertPoint.Parameters.AddWithValue("$key", point.SeriesKey);
                insertPoint.Parameters.AddWithValue("$va", point.Va);
                insertPoint.Parameters.AddWithValue("$vs", point.Vs);
                insertPoint.Parameters.AddWithValue("$vg", point.Vg);
                insertPoint.Parameters.AddWithValue("$ia", point.Ia);
                insertPoint.Parameters.AddWithValue("$is", point.Is);
                await insertPoint.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            addedSeries++;
            addedPoints += points.Count;
        }

        return new LegacyCurveMigrationResult(
            legacySetCount, legacyPointCount, addedSeries, addedPoints, skipped);
    }

    private static async Task<List<LegacyPoint>> LoadPointsAsync(
        SqliteConnection connection,
        string setId,
        CancellationToken cancellationToken)
    {
        var result = new List<LegacyPoint>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT series_key, sequence, anode_voltage_v, anode_current_ma,
       COALESCE(screen_current_ma,0), grid_voltage_v, screen_voltage_v
FROM reference_curve_points
WHERE curve_set_id=$id
ORDER BY series_key, sequence;
""";
        command.Parameters.AddWithValue("$id", setId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LegacyPoint(
                reader.GetString(0), reader.GetInt32(1), reader.GetDouble(2),
                reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6)));
        }
        return result;
    }

    private static async Task<bool> SeriesExistsAsync(
        SqliteConnection connection,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM reference_curve_series_v2 WHERE id=$id);";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name);";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<int> ScalarIntAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private sealed record LegacySet(
        string Id, string ProfileId, string CurveKind, string Section, string DisplayName,
        string DigitizationStatus, string SourceTitle, string SourceUrl, string SourcePage);

    private sealed record LegacyPoint(
        string SeriesKey, int Sequence, double Va, double Ia, double Is, double Vg, double Vs);
}
