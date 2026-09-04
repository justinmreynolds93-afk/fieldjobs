using FieldJobs.Config;

namespace FieldJobs.Models;

/// <summary>
/// The app stores status/type values as plain text in SQLite. These lists (and
/// the field labels below) are the single source of truth for the dropdowns,
/// validation, and every place a label is shown.
///
/// <see cref="Configure"/> must run once at startup, before <c>Db.Initialize()</c>
/// — see App.xaml.cs — so it applies appsettings.json / appsettings.Local.json
/// before anything (including SeedData) reads from here. Everything not driven
/// by config (stage/invoice status keywords, payment methods) stays a plain
/// const/readonly, same as before there was a config layer at all.
/// </summary>
public static class Vocab
{
    public static string[] JobStatuses { get; private set; } = BuildJobStatuses("Agency", "Client");
    public static string[] ProjectTypes { get; private set; } = { "Standard", "Priority", "Other" };

    /// <summary>Field label for Job.ExternalRef1 (default "Ref #").</summary>
    public static string Ref1Label { get; private set; } = "Ref #";
    /// <summary>Field label for Job.ExternalRef2 (default "Program #").</summary>
    public static string Ref2Label { get; private set; } = "Program #";
    /// <summary>Field label for Job.ClientName (default "Client").</summary>
    public static string ClientFieldLabel { get; private set; } = "Client";
    /// <summary>Display name for the agency/program party (default "Agency").</summary>
    public static string AgencyPartyLabel { get; private set; } = "Agency";
    /// <summary>Display name for the client/homeowner party (default "Client").</summary>
    public static string ClientPartyLabel { get; private set; } = "Client";
    /// <summary>App name shown in the nav rail / window chrome (default "FieldJobs").</summary>
    public static string BrandAppName { get; private set; } = "FieldJobs";
    /// <summary>Your company/agency name, shown above BrandAppName. Blank hides that line.</summary>
    public static string BrandOrg { get; private set; } = "";

    public static readonly string[] StageStatuses =
    {
        "Not started",
        "In progress",
        "Complete",
        "N/A"
    };

    public static readonly string[] InvoiceStatuses =
    {
        "Draft",
        "Sent",
        "Paid",
        "Partial",
        "Void"
    };

    public const string StageComplete = "Complete";
    public const string StageNa = "N/A";
    public const string StageInProgress = "In progress";
    public const string StageNotStarted = "Not started";

    public const string InvoiceVoid = "Void";
    public const string InvoiceDraft = "Draft";
    public const string InvoiceSent = "Sent";
    public const string InvoicePaid = "Paid";
    public const string InvoicePartial = "Partial";

    /// <summary>Invoice statuses that still owe money, worst-first, for the receivables report.</summary>
    public static readonly string[] OutstandingStatuses = { "Partial", "Sent", "Draft" };

    public static readonly string[] PaymentMethods =
    {
        "ACH / direct deposit",
        "Check",
        "Card",
        "Other"
    };

    public const string DocInvoiceSent = "invoice_sent";
    public const string DocPaymentProof = "payment_proof";
    public const string DocFile = "file";

    /// <summary>How many days after an invoice is sent (or dated) it counts as overdue.</summary>
    public static int OverdueDays { get; private set; } = 30;

    /// <summary>A job with no activity in this many days is flagged as stale.</summary>
    public static int StaleJobDays { get; private set; } = 21;

    /// <summary>Nag for a backup after this many days.</summary>
    public static int BackupNagDays { get; private set; } = 10;

    private static string _waitingOnAgency = "Waiting on Agency";
    private static string _waitingOnClient = "Waiting on Client";

    /// <summary>Applies appsettings.json / appsettings.Local.json. Call once, at startup.</summary>
    public static void Configure(AppConfig config)
    {
        AgencyPartyLabel = NonBlank(config.Parties.Agency, AgencyPartyLabel);
        ClientPartyLabel = NonBlank(config.Parties.Client, ClientPartyLabel);
        _waitingOnAgency = $"Waiting on {AgencyPartyLabel}";
        _waitingOnClient = $"Waiting on {ClientPartyLabel}";
        JobStatuses = BuildJobStatuses(AgencyPartyLabel, ClientPartyLabel);

        if (config.ProjectTypes is { Count: > 0 }) ProjectTypes = config.ProjectTypes.ToArray();

        Ref1Label = NonBlank(config.Job.Ref1Label, Ref1Label);
        Ref2Label = NonBlank(config.Job.Ref2Label, Ref2Label);
        ClientFieldLabel = NonBlank(config.Job.ClientLabel, ClientFieldLabel);

        BrandAppName = NonBlank(config.Brand.AppName, BrandAppName);
        BrandOrg = config.Brand.Org ?? BrandOrg;

        if (config.Rules.OverdueDays > 0) OverdueDays = config.Rules.OverdueDays;
        if (config.Rules.StaleJobDays > 0) StaleJobDays = config.Rules.StaleJobDays;
        if (config.Rules.BackupNagDays > 0) BackupNagDays = config.Rules.BackupNagDays;
    }

    private static string NonBlank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string[] BuildJobStatuses(string agency, string client) => new[]
    {
        "Not started",
        "In progress",
        $"Waiting on {agency}",
        $"Waiting on {client}",
        "Complete",
        "On hold"
    };

    /// <summary>
    /// Which side a job is "waiting on", for the dashboard split. Returns a fixed
    /// internal key ("Agency"/"Client"/"Me"/"—"), not the display label — callers
    /// that need the label use AgencyPartyLabel/ClientPartyLabel directly.
    /// </summary>
    public static string WaitingOn(string jobStatus)
    {
        if (jobStatus == _waitingOnAgency) return "Agency";
        if (jobStatus == _waitingOnClient) return "Client";
        return jobStatus switch
        {
            "Not started" => "Me",
            "In progress" => "Me",
            _ => "—"
        };
    }
}
