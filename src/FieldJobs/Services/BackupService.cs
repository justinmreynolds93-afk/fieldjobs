using System.IO;
using System.IO.Compression;
using Dapper;
using Microsoft.Data.Sqlite;
using FieldJobs.Data;

namespace FieldJobs.Services;

/// <summary>
/// One-click backup = zip of the database + the Files folder. Restore unpacks a
/// backup zip over the data folder (after saving a safety copy of the current data).
/// </summary>
public sealed class BackupService
{
    public string CreateBackup(string? targetFolder = null)
    {
        var folder = string.IsNullOrWhiteSpace(targetFolder)
            ? new SettingsService().BackupFolder
            : targetFolder!;
        Directory.CreateDirectory(folder);

        var zipPath = Path.Combine(folder, $"fieldjobs-backup-{DateTime.Now:yyyy-MM-dd_HHmmss}.zip");
        var tempDb = Path.Combine(Path.GetTempPath(), $"fieldjobs-backup-{Guid.NewGuid():N}.db");

        try
        {
            // SQLite online backup: safe to run while the app has the db open.
            Db.Connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
            using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = tempDb,
                       Mode = SqliteOpenMode.ReadWriteCreate,
                       Pooling = false
                   }.ToString()))
            {
                dest.Open();
                Db.Connection.BackupDatabase(dest);
            }
            SqliteConnection.ClearAllPools();

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(tempDb, "fieldjobs.db");

            if (Directory.Exists(AppPaths.FilesRoot))
            {
                foreach (var file in Directory.EnumerateFiles(AppPaths.FilesRoot, "*", SearchOption.AllDirectories))
                {
                    var rel = "Files/" + Path.GetRelativePath(AppPaths.FilesRoot, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, rel);
                }
            }
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { /* temp file */ }
        }

        try
        {
            Db.Connection.Execute(
                "INSERT OR REPLACE INTO settings(key,value) VALUES ('last_backup_at', @now)",
                new { now = DateTime.Now.ToString("s") });
        }
        catch { /* non-fatal */ }

        return zipPath;
    }

    public void RestoreFromBackup(string zipPath)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException("Backup zip not found.", zipPath);

        // Safety copy of the current state before we overwrite anything.
        try { CreateBackup(Path.Combine(AppPaths.DefaultBackupFolder, "before-restore")); }
        catch { /* best effort */ }

        Db.Close();
        SqliteConnection.ClearAllPools();

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (Directory.Exists(AppPaths.FilesRoot))
                Directory.Delete(AppPaths.FilesRoot, recursive: true);
            Directory.CreateDirectory(AppPaths.FilesRoot);

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

                var destination = entry.FullName == "fieldjobs.db"
                    ? AppPaths.DatabaseFile
                    : Path.Combine(AppPaths.DataRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }

        // Drop stale WAL/SHM so SQLite re-opens cleanly against the restored db.
        foreach (var ext in new[] { "-wal", "-shm" })
        {
            var p = AppPaths.DatabaseFile + ext;
            if (File.Exists(p)) File.Delete(p);
        }

        Db.Initialize();
    }
}
