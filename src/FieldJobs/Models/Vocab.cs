namespace FieldJobs.Models;

/// <summary>
/// The app stores status/type values as plain text in SQLite. These lists are the
/// single source of truth for the dropdowns and for validation.
/// </summary>
public static class Vocab
{
    public static readonly string[] JobStatuses =
    {
        "Not started",
        "In progress",
        "Waiting on agency",
        "Waiting on client",
        "Complete",
        "On hold"
    };

    public static readonly string[] ProjectTypes =
    {
        "Standard",
        "Priority",
        "Other"
    };

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
    public const int OverdueDays = 30;

    /// <summary>A job with no activity in this many days is flagged as stale.</summary>
    public const int StaleJobDays = 21;

    /// <summary>Nag for a backup after this many days.</summary>
    public const int BackupNagDays = 10;

    /// <summary>Which side a job is "waiting on", for the dashboard split.</summary>
    public static string WaitingOn(string jobStatus) => jobStatus switch
    {
        "Waiting on agency" => "Agency",
        "Waiting on client" => "Client",
        "Not started" => "Me",
        "In progress" => "Me",
        _ => "—"
    };
}
