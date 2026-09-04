using System.Text.Json;

namespace FieldJobs.Tests;

/// <summary>A throwaway directory holding appsettings.json (+ optional .Local.json),
/// so tests exercise AppConfig.Load(dir) without touching the real build output.</summary>
internal sealed class TestConfigDir : IDisposable
{
    public string Path { get; }

    public TestConfigDir(object shipped, object? local = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fieldjobs-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        File.WriteAllText(System.IO.Path.Combine(Path, "appsettings.json"), JsonSerializer.Serialize(shipped));
        if (local is not null)
            File.WriteAllText(System.IO.Path.Combine(Path, "appsettings.Local.json"), JsonSerializer.Serialize(local));
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
