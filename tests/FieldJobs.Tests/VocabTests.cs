using FieldJobs.Config;
using FieldJobs.Models;
using Xunit;

namespace FieldJobs.Tests;

public class VocabTests
{
    [Fact]
    public void Configure_BuildsJobStatusesFromConfiguredParties()
    {
        Vocab.Configure(new AppConfig
        {
            Parties = new PartiesConfig { Agency = "METEC", Client = "Homeowner" },
        });

        Assert.Contains("Waiting on METEC", Vocab.JobStatuses);
        Assert.Contains("Waiting on Homeowner", Vocab.JobStatuses);
        Assert.Equal("METEC", Vocab.AgencyPartyLabel);
        Assert.Equal("Homeowner", Vocab.ClientPartyLabel);
    }

    [Fact]
    public void WaitingOn_MatchesTheCurrentlyConfiguredPartyStrings()
    {
        Vocab.Configure(new AppConfig
        {
            Parties = new PartiesConfig { Agency = "METEC", Client = "Homeowner" },
        });

        Assert.Equal("Agency", Vocab.WaitingOn("Waiting on METEC"));
        Assert.Equal("Client", Vocab.WaitingOn("Waiting on Homeowner"));
        Assert.Equal("Me", Vocab.WaitingOn("Not started"));
        Assert.Equal("Me", Vocab.WaitingOn("In progress"));
        Assert.Equal("—", Vocab.WaitingOn("Complete"));

        // regression: after Configure() the OLD default strings must no longer match —
        // this is the exact bug class M2 shipped with (a stale literal outliving a
        // config change) and then fixed once in SeedData; this test guards it generally.
        Assert.NotEqual("Agency", Vocab.WaitingOn("Waiting on Agency"));
    }

    [Fact]
    public void Configure_BlankOverrides_FallBackRatherThanProducingEmptyLabels()
    {
        Vocab.Configure(new AppConfig
        {
            Parties = new PartiesConfig { Agency = "   ", Client = "Client" },
            Job = new JobLabelsConfig { Ref1Label = "" },
        });

        Assert.False(string.IsNullOrWhiteSpace(Vocab.AgencyPartyLabel));
        Assert.False(string.IsNullOrWhiteSpace(Vocab.Ref1Label));
    }

    [Fact]
    public void Configure_AppliesProjectTypesAndRuleDayCounts()
    {
        Vocab.Configure(new AppConfig
        {
            ProjectTypes = new List<string> { "Full Rehabilitation", "Accessibility", "Roof-Only", "Other" },
            Rules = new RulesConfig { OverdueDays = 45, StaleJobDays = 14, BackupNagDays = 3 },
        });

        Assert.Equal(new[] { "Full Rehabilitation", "Accessibility", "Roof-Only", "Other" }, Vocab.ProjectTypes);
        Assert.Equal(45, Vocab.OverdueDays);
        Assert.Equal(14, Vocab.StaleJobDays);
        Assert.Equal(3, Vocab.BackupNagDays);
    }
}
