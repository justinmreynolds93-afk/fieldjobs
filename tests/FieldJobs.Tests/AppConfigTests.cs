using FieldJobs.Config;
using Xunit;

namespace FieldJobs.Tests;

public class AppConfigTests
{
    [Fact]
    public void Load_WithNoFiles_ReturnsBuiltInDefaults()
    {
        using var dir = new TestConfigDir(shipped: new { });
        File.Delete(Path.Combine(dir.Path, "appsettings.json")); // simulate neither file existing

        var cfg = AppConfig.Load(dir.Path);

        Assert.Equal("FieldJobs", cfg.Brand.AppName);
        Assert.Equal("Ref #", cfg.Job.Ref1Label);
        Assert.Equal("Agency", cfg.Parties.Agency);
        Assert.Equal(new[] { "Standard", "Priority", "Other" }, cfg.ProjectTypes);
        Assert.Equal("inspection-8", cfg.StageTemplate);
    }

    [Fact]
    public void Load_ShippedFileOnly_OverridesBuiltInDefaults()
    {
        using var dir = new TestConfigDir(shipped: new
        {
            parties = new { agency = "Agency", client = "Client" },
            job = new { ref1Label = "Ref #" },
        });

        var cfg = AppConfig.Load(dir.Path);

        Assert.Equal("Ref #", cfg.Job.Ref1Label);
        // untouched by the shipped file -> falls back to AppConfig's own field initializer
        Assert.Equal("Program #", cfg.Job.Ref2Label);
    }

    [Fact]
    public void Load_LocalFile_OverridesOnlyTheSectionsItSpecifies()
    {
        using var dir = new TestConfigDir(
            shipped: new
            {
                parties = new { agency = "Agency", client = "Client" },
                job = new { ref1Label = "Ref #", ref2Label = "Program #", clientLabel = "Client" },
            },
            local: new
            {
                parties = new { agency = "METEC", client = "Homeowner" },
                // job section intentionally omitted -> should keep the shipped values
            });

        var cfg = AppConfig.Load(dir.Path);

        Assert.Equal("METEC", cfg.Parties.Agency);
        Assert.Equal("Homeowner", cfg.Parties.Client);
        Assert.Equal("Ref #", cfg.Job.Ref1Label);
        Assert.Equal("Program #", cfg.Job.Ref2Label);
    }

    [Fact]
    public void Load_StageTemplate_ReadsThroughFromLocalOverride()
    {
        using var dir = new TestConfigDir(
            shipped: new { stageTemplate = "inspection-8" },
            local: new { stageTemplate = "basic-3" });

        var cfg = AppConfig.Load(dir.Path);

        Assert.Equal("basic-3", cfg.StageTemplate);
    }

    [Fact]
    public void Load_MalformedLocalFile_FallsBackToShippedValuesInsteadOfThrowing()
    {
        using var dir = new TestConfigDir(shipped: new { parties = new { agency = "Agency", client = "Client" } });
        File.WriteAllText(Path.Combine(dir.Path, "appsettings.Local.json"), "{ this is not valid json ");

        var cfg = AppConfig.Load(dir.Path); // must not throw

        Assert.Equal("Agency", cfg.Parties.Agency);
    }
}
