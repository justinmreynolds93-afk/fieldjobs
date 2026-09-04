using System.IO;
using System.Text.Json;

namespace FieldJobs.Config;

public sealed class BrandConfig
{
    public string AppName { get; set; } = "FieldJobs";
    /// <summary>Your company/agency name, shown above AppName in the nav rail. Blank hides the line.</summary>
    public string Org { get; set; } = "";
}

public sealed class JobLabelsConfig
{
    /// <summary>Label for Job.ExternalRef1 — your own tracking number, or a program's.</summary>
    public string Ref1Label { get; set; } = "Ref #";
    /// <summary>Label for Job.ExternalRef2 — a second reference number, if you need one.</summary>
    public string Ref2Label { get; set; } = "Program #";
    /// <summary>Label for Job.ClientName — "Client", "Homeowner", "Tenant", whatever fits.</summary>
    public string ClientLabel { get; set; } = "Client";
}

/// <summary>Who a job is "waiting on" — drives the dashboard split and status text.</summary>
public sealed class PartiesConfig
{
    public string Agency { get; set; } = "Agency";
    public string Client { get; set; } = "Client";
}

public sealed class RulesConfig
{
    /// <summary>Days after an invoice is sent (or dated) before it counts as overdue.</summary>
    public int OverdueDays { get; set; } = 30;
    /// <summary>Days with no logged activity before a job is flagged stale.</summary>
    public int StaleJobDays { get; set; } = 21;
    /// <summary>Days since the last backup before the dashboard nags about it.</summary>
    public int BackupNagDays { get; set; } = 10;
}

/// <summary>
/// Everything that used to be hard-coded METEC/IHDA-specific text now lives here.
/// Loaded from appsettings.json (shipped, generic) with appsettings.Local.json
/// (gitignored) layered on top — your real labels and rates never leave your
/// machine. See appsettings.Local.json.example.
/// </summary>
public sealed class AppConfig
{
    public BrandConfig Brand { get; set; } = new();
    public JobLabelsConfig Job { get; set; } = new();
    public PartiesConfig Parties { get; set; } = new();
    public List<string> ProjectTypes { get; set; } = new() { "Standard", "Priority", "Other" };
    public RulesConfig Rules { get; set; } = new();
    /// <summary>Which stage template SeedData installs on first run: "inspection-8" or "basic-3".</summary>
    public string StageTemplate { get; set; } = "inspection-8";

    public static AppConfig Instance { get; private set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocOpts = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads appsettings.json then appsettings.Local.json (both next to the exe).
    /// Each top-level section present in a file replaces that whole section — a
    /// Local file only needs to specify what it's overriding.
    /// </summary>
    public static AppConfig Load()
    {
        var baseDir = AppContext.BaseDirectory;
        var cfg = new AppConfig();
        ApplyFile(cfg, Path.Combine(baseDir, "appsettings.json"));
        ApplyFile(cfg, Path.Combine(baseDir, "appsettings.Local.json"));
        Instance = cfg;
        return cfg;
    }

    private static void ApplyFile(AppConfig cfg, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), DocOpts);
            var root = doc.RootElement;

            if (root.TryGetProperty("brand", out var brand))
                cfg.Brand = brand.Deserialize<BrandConfig>(JsonOpts) ?? cfg.Brand;
            if (root.TryGetProperty("job", out var job))
                cfg.Job = job.Deserialize<JobLabelsConfig>(JsonOpts) ?? cfg.Job;
            if (root.TryGetProperty("parties", out var parties))
                cfg.Parties = parties.Deserialize<PartiesConfig>(JsonOpts) ?? cfg.Parties;
            if (root.TryGetProperty("projectTypes", out var types))
                cfg.ProjectTypes = types.Deserialize<List<string>>(JsonOpts) ?? cfg.ProjectTypes;
            if (root.TryGetProperty("rules", out var rules))
                cfg.Rules = rules.Deserialize<RulesConfig>(JsonOpts) ?? cfg.Rules;
            if (root.TryGetProperty("stageTemplate", out var tmpl) && tmpl.GetString() is { } s)
                cfg.StageTemplate = s;
        }
        catch (Exception ex)
        {
            // A bad config file should never stop the app from starting — fall back
            // to whatever was already loaded and let the user see the real error via Settings.
            Infra.Log.Error($"config: failed to read {path}", ex);
        }
    }
}
