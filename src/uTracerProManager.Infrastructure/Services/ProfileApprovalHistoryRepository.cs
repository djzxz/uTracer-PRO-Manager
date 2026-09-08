using Microsoft.Data.Sqlite;

namespace uTracerProManager.Services;

public sealed record ProfileApprovalHistoryEntry(
    long Id,
    string ProfileId,
    string HardwareId,
    string DataStatus,
    string HardwareStatus,
    string Decision,
    string Reason,
    string Source,
    string Actor,
    DateTimeOffset CreatedAt);

public sealed class ProfileApprovalHistoryRepository
{
    private readonly string _databasePath;

    public ProfileApprovalHistoryRepository(string databasePath) =>
        _databasePath = Path.GetFullPath(databasePath);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE IF NOT EXISTS profile_approval_history_v2 (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_id TEXT NOT NULL,
    hardware_id TEXT NOT NULL,
    data_status TEXT NOT NULL,
    hardware_status TEXT NOT NULL,
    decision TEXT NOT NULL,
    reason TEXT NOT NULL,
    source TEXT NOT NULL,
    actor TEXT NOT NULL,
    created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_profile_approval_history_v2_profile
ON profile_approval_history_v2(profile_id, hardware_id, created_utc);
""";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendAsync(
        string profileId,
        string hardwareId,
        string dataStatus,
        string hardwareStatus,
        string decision,
        string reason,
        string source,
        string actor,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO profile_approval_history_v2(
    profile_id, hardware_id, data_status, hardware_status,
    decision, reason, source, actor, created_utc)
VALUES($profile,$hardware,$data,$hwstatus,$decision,$reason,$source,$actor,$utc);
""";
        command.Parameters.AddWithValue("$profile", profileId);
        command.Parameters.AddWithValue("$hardware", hardwareId);
        command.Parameters.AddWithValue("$data", dataStatus);
        command.Parameters.AddWithValue("$hwstatus", hardwareStatus);
        command.Parameters.AddWithValue("$decision", decision);
        command.Parameters.AddWithValue("$reason", reason ?? string.Empty);
        command.Parameters.AddWithValue("$source", source ?? string.Empty);
        command.Parameters.AddWithValue("$actor", actor ?? "USER");
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProfileApprovalHistoryEntry>> ReadAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var result = new List<ProfileApprovalHistoryEntry>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, profile_id, hardware_id, data_status, hardware_status,
       decision, reason, source, actor, created_utc
FROM profile_approval_history_v2
WHERE profile_id=$profile
ORDER BY id DESC;
""";
        command.Parameters.AddWithValue("$profile", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProfileApprovalHistoryEntry(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9))));
        }
        return result;
    }

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString());
}
