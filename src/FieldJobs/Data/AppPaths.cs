using System.IO;

namespace FieldJobs.Data;

/// <summary>
/// Central definition of where the app keeps its data. Everything lives under
/// %LOCALAPPDATA%\FieldJobs so an uninstall never deletes the user's records.
/// </summary>
public static class AppPaths
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FieldJobs");

    /// <summary>SQLite database file.</summary>
    public static string DatabaseFile => Path.Combine(DataRoot, "fieldjobs.db");

    /// <summary>Root folder that holds one sub-folder per job for attached files.</summary>
    public static string FilesRoot => Path.Combine(DataRoot, "Files");

    /// <summary>Default folder for backup zips (user can change in Settings).</summary>
    public static string DefaultBackupFolder => Path.Combine(DataRoot, "Backups");

    /// <summary>Folder where generated PDFs are dropped by default.</summary>
    public static string OutputFolder => Path.Combine(DataRoot, "Output");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(FilesRoot);
        Directory.CreateDirectory(DefaultBackupFolder);
        Directory.CreateDirectory(OutputFolder);
    }

    /// <summary>Turns "123 Main St" / "Anytown" into a filesystem-safe folder name.</summary>
    public static string SafeFolderName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(raw.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        cleaned = cleaned.Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "job";
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
