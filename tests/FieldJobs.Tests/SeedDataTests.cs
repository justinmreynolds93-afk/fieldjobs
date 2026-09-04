using Dapper;
using FieldJobs.Config;
using FieldJobs.Data;
using FieldJobs.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FieldJobs.Tests;

/// <summary>
/// Runs SeedData against a throwaway in-memory SQLite connection — no real
/// AppPaths/Db singleton involved, so these can't touch (or be affected by)
/// your actual %LOCALAPPDATA%\FieldJobs data.
/// </summary>
public class SeedDataTests
{
    private static SqliteConnection FreshSchema()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        conn.Execute(Db.Schema);
        return conn;
    }

    [Fact]
    public void EnsureDefaults_Inspection8_SeedsAllEightStages()
    {
        using var dir = new TestConfigDir(shipped: new { stageTemplate = "inspection-8" });
        Vocab.Configure(AppConfig.Load(dir.Path));

        using var conn = FreshSchema();
        SeedData.EnsureDefaults(conn);

        var keys = conn.Query<string>("select stage_key from stage_templates order by seq").ToList();
        Assert.Equal(
            new[] { "assignment", "preview", "scope_writing", "scope_submitted",
                     "work_in_progress", "final_walkthrough", "punch", "closeout" },
            keys);
    }

    [Fact]
    public void EnsureDefaults_Basic3_SeedsExactlyThreeStages()
    {
        using var dir = new TestConfigDir(shipped: new { stageTemplate = "basic-3" });
        Vocab.Configure(AppConfig.Load(dir.Path));

        using var conn = FreshSchema();
        SeedData.EnsureDefaults(conn);

        var keys = conn.Query<string>("select stage_key from stage_templates order by seq").ToList();
        Assert.Equal(new[] { "assignment", "work", "closeout" }, keys);
    }

    [Fact]
    public void EnsureDefaults_FeeScheduleDefaultsToZero()
    {
        using var dir = new TestConfigDir(shipped: new { });
        Vocab.Configure(AppConfig.Load(dir.Path));

        using var conn = FreshSchema();
        SeedData.EnsureDefaults(conn);

        var amounts = conn.Query<double>("select amount from fee_schedule").ToList();
        Assert.NotEmpty(amounts);
        Assert.All(amounts, a => Assert.Equal(0, a));
    }

    /// <summary>
    /// Regression test for the M2 bug: SeedSampleJobs used to hard-code job 2's
    /// status as the literal 'Waiting on agency', which stopped matching once
    /// Vocab.JobStatuses became config-driven (it produces "Waiting on Agency",
    /// Title Case, by default). Every seeded job's status must always be one of
    /// the values the Status dropdown actually offers, under ANY party config.
    /// </summary>
    [Theory]
    [InlineData("Agency", "Client")]
    [InlineData("METEC", "Homeowner")]
    [InlineData("Property Manager", "Tenant")]
    public void EnsureDefaults_SeededJobStatuses_AlwaysMatchConfiguredJobStatuses(string agency, string client)
    {
        using var dir = new TestConfigDir(shipped: new { parties = new { agency, client } });
        Vocab.Configure(AppConfig.Load(dir.Path));

        using var conn = FreshSchema();
        SeedData.EnsureDefaults(conn);

        var statuses = conn.Query<string>("select status from jobs").ToList();
        Assert.NotEmpty(statuses);
        Assert.All(statuses, s => Assert.Contains(s, Vocab.JobStatuses));
    }

    [Fact]
    public void EnsureDefaults_IsIdempotent_SecondRunDoesNotDuplicateRows()
    {
        using var dir = new TestConfigDir(shipped: new { });
        Vocab.Configure(AppConfig.Load(dir.Path));

        using var conn = FreshSchema();
        SeedData.EnsureDefaults(conn);
        var before = conn.ExecuteScalar<long>("select count(*) from jobs");

        SeedData.EnsureDefaults(conn); // same connection, run again
        var after = conn.ExecuteScalar<long>("select count(*) from jobs");

        Assert.Equal(before, after);
    }
}
