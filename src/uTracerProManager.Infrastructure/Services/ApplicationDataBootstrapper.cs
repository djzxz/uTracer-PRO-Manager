using Microsoft.Data.Sqlite;

namespace uTracerProManager.Services;

public sealed record ApplicationDataPaths(
    string RootDirectory,
    string ActiveCatalogPath,
    string CalibrationPath,
    string HistoryPath,
    string LogPath);

public static class ApplicationDataBootstrapper
{
    public static ApplicationDataPaths Prepare(string bundledCatalogPath)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "uTracerProManagerAvalonia");
        var data = Path.Combine(root, "Data");
        var logs = Path.Combine(root, "Logs");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(logs);

        var activeCatalog = Path.Combine(data, "tube_measurements.db");
        if (!File.Exists(bundledCatalogPath))
            throw new FileNotFoundException("Brak wbudowanej bazy pomiarowej.", bundledCatalogPath);

        if (!File.Exists(activeCatalog))
        {
            ReplaceCatalog(bundledCatalogPath, activeCatalog);
        }
        else if (ShouldInstallBundledCatalog(bundledCatalogPath, activeCatalog))
        {
            var backupDirectory = Path.Combine(data, "BACKUP_BAZY");
            Directory.CreateDirectory(backupDirectory);
            var activeVersion = TryReadCatalogVersion(activeCatalog)?.ToString() ?? "nieznana";
            var backupPath = Path.Combine(
                backupDirectory,
                $"tube_measurements_przed_aktualizacja_{activeVersion.Replace('.', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            File.Copy(activeCatalog, backupPath, overwrite: false);
            ReplaceCatalog(bundledCatalogPath, activeCatalog);
        }

        return new ApplicationDataPaths(
            root,
            activeCatalog,
            Path.Combine(root, "calibration_v2.json"),
            Path.Combine(root, "test_history.db"),
            Path.Combine(logs, "application.log"));
    }

    private static bool ShouldInstallBundledCatalog(string bundledCatalogPath, string activeCatalogPath)
    {
        var bundledVersion = TryReadCatalogVersion(bundledCatalogPath)
            ?? throw new InvalidDataException("Wbudowana baza nie zawiera prawidłowej wersji katalogu.");
        var activeVersion = TryReadCatalogVersion(activeCatalogPath);
        return activeVersion is null || bundledVersion > activeVersion;
    }

    private static Version? TryReadCatalogVersion(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM catalog_info WHERE key='catalog_version' LIMIT 1;";
            var raw = Convert.ToString(command.ExecuteScalar());
            return Version.TryParse(raw, out var version) ? version : null;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private static void ReplaceCatalog(string sourcePath, string destinationPath)
    {
        var temporary = destinationPath + ".new";
        try
        {
            File.Copy(sourcePath, temporary, overwrite: true);
            SqliteConnection.ClearAllPools();
            File.Move(temporary, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
