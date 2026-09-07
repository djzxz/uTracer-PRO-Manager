using Microsoft.Data.Sqlite;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Services;

namespace uTracerProManager.Services;

/// <summary>
/// Repozytorium krzywych v2. Używa nowych tabel i nigdy nie modyfikuje
/// historycznych reference_curve_sets/reference_curve_points ani ręcznej historii testów.
/// Migracja jest wyłącznie CREATE IF NOT EXISTS + append-only schema_migrations.
/// </summary>
public sealed class ReferenceCurveRepository : IReferenceCurveRepository
{
    private const string MigrationId = "2026-09-07-reference-curves-v2";
    private readonly string _databasePath;

    private sealed record SeriesMetadata(
        string Id,
        string ProfileId,
        string Section,
        string SourceKind,
        string SourceTitle,
        string SourceUrl,
        string SourcePage,
        string Mode,
        DateTimeOffset CapturedAt,
        string DigitizationStatus,
        double Quality,
        string ExtractionVersion,
        bool HumanReviewed);

    public ReferenceCurveRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Ścieżka bazy nie może być pusta.", nameof(databasePath));
        _databasePath = Path.GetFullPath(databasePath);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, """
CREATE TABLE IF NOT EXISTS schema_migrations (
    migration_id TEXT PRIMARY KEY,
    applied_utc TEXT NOT NULL,
    application_version TEXT NOT NULL,
    note TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS reference_curve_series_v2 (
    id TEXT PRIMARY KEY,
    profile_id TEXT NOT NULL,
    section TEXT NOT NULL,
    source_kind TEXT NOT NULL,
    source_title TEXT NOT NULL,
    source_url TEXT NOT NULL,
    source_page TEXT NOT NULL,
    mode TEXT NOT NULL,
    captured_utc TEXT NOT NULL,
    digitization_status TEXT NOT NULL,
    quality REAL NOT NULL,
    extraction_version TEXT NOT NULL,
    human_reviewed INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_reference_curve_series_v2_match
ON reference_curve_series_v2(profile_id, section, mode, source_kind);

CREATE TABLE IF NOT EXISTS reference_curve_points_v2 (
    series_id TEXT NOT NULL,
    sequence INTEGER NOT NULL,
    series_key TEXT NOT NULL,
    va_set REAL NOT NULL,
    va_measured REAL NOT NULL,
    vs_set REAL NOT NULL,
    vs_measured REAL NOT NULL,
    vg REAL NOT NULL,
    vh REAL NOT NULL,
    ia REAL NOT NULL,
    is_current REAL NOT NULL,
    gm REAL NULL,
    rp REAL NULL,
    compliance_status TEXT NOT NULL,
    interpolated INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(series_id, sequence),
    FOREIGN KEY(series_id) REFERENCES reference_curve_series_v2(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_reference_curve_points_v2_key
ON reference_curve_points_v2(series_id, series_key, sequence);
""", cancellationToken);

        await using (var marker = connection.CreateCommand())
        {
            marker.Transaction = transaction;
            marker.CommandText = """
INSERT OR IGNORE INTO schema_migrations(migration_id, applied_utc, application_version, note)
VALUES($id, $utc, $version, $note);
""";
            marker.Parameters.AddWithValue("$id", MigrationId);
            marker.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            marker.Parameters.AddWithValue("$version", "uTracer PRO Manager post-1.2.7");
            marker.Parameters.AddWithValue("$note", "Krzywe v2; nowe tabele, bez nadpisywania historii i tabel checkpointów.");
            await marker.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveAsync(ReferenceCurveSeries series, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);
        ValidateSeries(series);
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO reference_curve_series_v2(
    id, profile_id, section, source_kind, source_title, source_url, source_page,
    mode, captured_utc, digitization_status, quality, extraction_version,
    human_reviewed, created_utc)
VALUES(
    $id, $profile, $section, $source_kind, $source_title, $source_url, $source_page,
    $mode, $captured, $status, $quality, $version, $reviewed, $created);
""";
            command.Parameters.AddWithValue("$id", series.Id);
            command.Parameters.AddWithValue("$profile", series.ProfileId);
            command.Parameters.AddWithValue("$section", series.Section);
            command.Parameters.AddWithValue("$source_kind", series.SourceKind);
            command.Parameters.AddWithValue("$source_title", series.SourceTitle);
            command.Parameters.AddWithValue("$source_url", series.SourceUrl);
            command.Parameters.AddWithValue("$source_page", series.SourcePage);
            command.Parameters.AddWithValue("$mode", series.Mode);
            command.Parameters.AddWithValue("$captured", series.CapturedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$status", series.DigitizationStatus);
            command.Parameters.AddWithValue("$quality", series.Quality);
            command.Parameters.AddWithValue("$version", series.ExtractionVersion);
            command.Parameters.AddWithValue("$reviewed", series.HumanReviewed ? 1 : 0);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var point in series.Points.OrderBy(point => point.Sequence))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO reference_curve_points_v2(
    series_id, sequence, series_key, va_set, va_measured, vs_set, vs_measured,
    vg, vh, ia, is_current, gm, rp, compliance_status, interpolated)
VALUES(
    $series, $sequence, $key, $va_set, $va_measured, $vs_set, $vs_measured,
    $vg, $vh, $ia, $is, $gm, $rp, $compliance, $interpolated);
""";
            command.Parameters.AddWithValue("$series", series.Id);
            command.Parameters.AddWithValue("$sequence", point.Sequence);
            command.Parameters.AddWithValue("$key", point.SeriesKey);
            command.Parameters.AddWithValue("$va_set", point.VaSet);
            command.Parameters.AddWithValue("$va_measured", point.VaMeasured);
            command.Parameters.AddWithValue("$vs_set", point.VsSet);
            command.Parameters.AddWithValue("$vs_measured", point.VsMeasured);
            command.Parameters.AddWithValue("$vg", point.Vg);
            command.Parameters.AddWithValue("$vh", point.Vh);
            command.Parameters.AddWithValue("$ia", point.Ia);
            command.Parameters.AddWithValue("$is", point.Is);
            command.Parameters.AddWithValue("$gm", (object?)point.Gm ?? DBNull.Value);
            command.Parameters.AddWithValue("$rp", (object?)point.Rp ?? DBNull.Value);
            command.Parameters.AddWithValue("$compliance", point.ComplianceStatus);
            command.Parameters.AddWithValue("$interpolated", point.Interpolated ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceCurveSeries>> FindMatchingAsync(
        ReferenceCurveMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var metadata = new List<SeriesMetadata>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
SELECT id, profile_id, section, source_kind, source_title, source_url, source_page,
       mode, captured_utc, digitization_status, quality, extraction_version, human_reviewed
FROM reference_curve_series_v2
WHERE profile_id = $profile COLLATE NOCASE
  AND section = $section COLLATE NOCASE
  AND ($mode = '' OR mode = $mode COLLATE NOCASE)
  AND ($source_kind = '' OR source_kind = $source_kind COLLATE NOCASE)
ORDER BY human_reviewed DESC, quality DESC, captured_utc DESC;
""";
            command.Parameters.AddWithValue("$profile", request.ProfileId);
            command.Parameters.AddWithValue("$section", request.Section);
            command.Parameters.AddWithValue("$mode", request.Mode ?? string.Empty);
            command.Parameters.AddWithValue("$source_kind", request.SourceKind ?? string.Empty);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                metadata.Add(new SeriesMetadata(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    DateTimeOffset.Parse(reader.GetString(8)),
                    reader.GetString(9),
                    reader.GetDouble(10),
                    reader.GetString(11),
                    reader.GetInt64(12) != 0));
            }
        }

        var candidates = new List<ReferenceCurveSeries>();
        foreach (var item in metadata)
        {
            var points = await LoadPointsAsync(connection, item.Id, cancellationToken);
            var series = new ReferenceCurveSeries(
                item.Id,
                item.ProfileId,
                item.Section,
                item.SourceKind,
                item.SourceTitle,
                item.SourceUrl,
                item.SourcePage,
                item.Mode,
                item.CapturedAt,
                item.DigitizationStatus,
                item.Quality,
                item.ExtractionVersion,
                item.HumanReviewed,
                points);

            if (MatchesBias(series, request))
                candidates.Add(series);
        }

        return candidates;
    }

    private static async Task<IReadOnlyList<ReferenceCurveDataPoint>> LoadPointsAsync(
        SqliteConnection connection,
        string seriesId,
        CancellationToken cancellationToken)
    {
        var points = new List<ReferenceCurveDataPoint>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT sequence, series_key, va_set, va_measured, vs_set, vs_measured,
       vg, vh, ia, is_current, gm, rp, compliance_status, interpolated
FROM reference_curve_points_v2
WHERE series_id = $series
ORDER BY sequence;
""";
        command.Parameters.AddWithValue("$series", seriesId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            points.Add(new ReferenceCurveDataPoint(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetDouble(8),
                reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.GetString(12),
                reader.GetInt64(13) != 0));
        }
        return points;
    }

    private static bool MatchesBias(ReferenceCurveSeries series, ReferenceCurveMatchRequest request)
    {
        if (series.Points.Count == 0)
            return false;
        var tolerance = Math.Max(0, request.VoltageTolerance);
        return MatchesRequested(request.Va, series.Points.Select(point => point.VaSet), tolerance) &&
               MatchesRequested(request.Vs, series.Points.Select(point => point.VsSet), tolerance) &&
               MatchesRequested(request.Vg, series.Points.Select(point => point.Vg), tolerance) &&
               MatchesRequested(request.Vh, series.Points.Select(point => point.Vh), tolerance);
    }

    private static bool MatchesRequested(double? requested, IEnumerable<double> values, double tolerance)
    {
        if (!requested.HasValue)
            return true;
        return values.Any(value => Math.Abs(value - requested.Value) <= tolerance);
    }

    private static void ValidateSeries(ReferenceCurveSeries series)
    {
        if (string.IsNullOrWhiteSpace(series.Id) || string.IsNullOrWhiteSpace(series.ProfileId))
            throw new InvalidOperationException("Krzywa musi mieć id oraz profile_id.");
        if (string.IsNullOrWhiteSpace(series.Section))
            throw new InvalidOperationException("Krzywa musi wskazywać sekcję.");
        if (series.Quality is < 0 or > 1 || !double.IsFinite(series.Quality))
            throw new InvalidOperationException("Jakość digitizacji musi mieścić się w zakresie 0–1.");
        if (series.Points.Count == 0)
            throw new InvalidOperationException("Nie zapisuje się pustej serii.");
        if (series.Points.Select(point => point.Sequence).Distinct().Count() != series.Points.Count)
            throw new InvalidOperationException("Numery sequence w serii muszą być unikalne.");
        if (string.Equals(series.SourceKind, "CATALOG", StringComparison.OrdinalIgnoreCase) &&
            !series.HumanReviewed &&
            string.Equals(series.DigitizationStatus, "READY", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Krzywa katalogowa nie może dostać READY bez kontroli człowieka.");
        }
    }

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString());

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
