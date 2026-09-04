using System.IO;
using FieldJobs.Data;

namespace FieldJobs.Infra;

/// <summary>Dead-simple rolling error log at %LOCALAPPDATA%\FieldJobs\error.log.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.DataRoot, "error.log");

    public static void Error(string context, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureCreated();
                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\n{ex}\n\n");
            }
        }
        catch { /* logging must never throw */ }
    }
}
