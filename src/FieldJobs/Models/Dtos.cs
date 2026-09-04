namespace FieldJobs.Models;

/// <summary>Row shown in the Jobs list and search results.</summary>
public sealed class JobListRow
{
    public long Id { get; set; }
    public string? JobNumber { get; set; }
    public string? ExternalRef1 { get; set; }
    public string Address { get; set; } = "";
    public string? ClientName { get; set; }
    public string ProjectType { get; set; } = "";
    public string Status { get; set; } = "";
    public string CurrentStage { get; set; } = "";
    public int PercentComplete { get; set; }
    public double Billed { get; set; }
    public double Paid { get; set; }
    public double Outstanding => Math.Round(Billed - Paid, 2);
    public string? DateAssigned { get; set; }
}

/// <summary>Money roll-up for a single job.</summary>
public sealed class JobTotals
{
    public double Billed { get; set; }
    public double Paid { get; set; }
    public double Outstanding => Math.Round(Billed - Paid, 2);
}

/// <summary>Everything the dashboard needs, computed in one pass.</summary>
public sealed class DashboardStats
{
    public double TotalBilled { get; set; }
    public double TotalPaid { get; set; }
    public double TotalOutstanding => Math.Round(TotalBilled - TotalPaid, 2);

    public double MonthBilled { get; set; }
    public double MonthPaid { get; set; }
    public string MonthLabel { get; set; } = "";

    public int JobsOpen { get; set; }
    public int JobsComplete { get; set; }

    public int WaitingOnMe { get; set; }
    public int WaitingOnAgency { get; set; }
    public int WaitingOnClient { get; set; }

    public List<StageCount> JobsByStage { get; set; } = new();
    public List<ReceivableRow> Receivables { get; set; } = new();
    public List<AttentionItem> Attention { get; set; } = new();
    public List<ActivityEntry> RecentActivity { get; set; } = new();
    public string? BackupWarning { get; set; }
}

/// <summary>One row in the dashboard "Needs attention" panel.</summary>
public sealed class AttentionItem
{
    public string Severity { get; set; } = "info";   // high | warn | info
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public long? JobId { get; set; }
    public long? InvoiceId { get; set; }
    public string Target { get; set; } = "";          // job | invoice | payments | settings | backup

    public int SeverityRank => Severity switch { "high" => 0, "warn" => 1, _ => 2 };
}

public sealed class StageCount
{
    public string Stage { get; set; } = "";
    public int Count { get; set; }
}

public sealed class ReceivableRow
{
    public long JobId { get; set; }
    public string Job { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string Status { get; set; } = "";
    public double Billed { get; set; }
    public double Paid { get; set; }
    public double Balance => Math.Round(Billed - Paid, 2);
}

/// <summary>Invoice + computed money columns for tables.</summary>
public sealed class InvoiceRow
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public string JobTitle { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string? DateSent { get; set; }
    public string? StageLabel { get; set; }
    public string Status { get; set; } = "";
    public double Billed { get; set; }
    public double Paid { get; set; }
    public double Balance => Math.Round(Billed - Paid, 2);
    public string? PaymentDate { get; set; }
    public int DocCount { get; set; }
    public bool HasPaymentProof { get; set; }
    public int DaysOutstanding { get; set; }
    public bool IsOverdue { get; set; }
}

/// <summary>One payment, flattened with its invoice + job, for the global payments ledger.</summary>
public sealed class PaymentLedgerRow
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public long JobId { get; set; }
    public string JobTitle { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string PaymentDate { get; set; } = "";
    public double Amount { get; set; }
    public string? Method { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
}

// ---------------------------------------------------------------- reports

public sealed class ReportFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<string> ProjectTypes { get; set; } = new();   // empty = all
    public long? PreparedForContactId { get; set; }

    public bool MatchesProject(string projectType)
        => ProjectTypes.Count == 0 || ProjectTypes.Contains(projectType);

    public bool MatchesDate(string? iso)
    {
        if (From is null && To is null) return true;
        if (!DateTime.TryParse(iso, out var d)) return false;
        if (From is { } f && d.Date < f.Date) return false;
        if (To is { } t && d.Date > t.Date) return false;
        return true;
    }

    public string RangeLabel =>
        (From, To) switch
        {
            (null, null) => "All dates",
            ({ } f, null) => $"From {f:MMM d, yyyy}",
            (null, { } t) => $"Through {t:MMM d, yyyy}",
            ({ } f, { } t) => $"{f:MMM d, yyyy} – {t:MMM d, yyyy}"
        };
}

public sealed class ReportLine
{
    public long JobId { get; set; }
    public string Job { get; set; } = "";
    public string? ClientName { get; set; }
    public string? ExternalRef1 { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string? DateSent { get; set; }
    public string? Stage { get; set; }
    public string Status { get; set; } = "";
    public double Billed { get; set; }
    public double Paid { get; set; }
    public double Balance => Math.Round(Billed - Paid, 2);
    public int AgeDays { get; set; }
    public string AgeBucket { get; set; } = "";
}

public sealed class ReportGroup
{
    public string Name { get; set; } = "";
    public List<ReportLine> Lines { get; set; } = new();
    public double Billed => Math.Round(Lines.Sum(l => l.Billed), 2);
    public double Paid => Math.Round(Lines.Sum(l => l.Paid), 2);
    public double Balance => Math.Round(Lines.Sum(l => l.Balance), 2);
}

public sealed class AgingSummary
{
    public double Current { get; set; }   // not yet due / 0 days
    public double D1_30 { get; set; }
    public double D31_60 { get; set; }
    public double D61_90 { get; set; }
    public double D90Plus { get; set; }
    public double Total => Math.Round(Current + D1_30 + D31_60 + D61_90 + D90Plus, 2);
}

public sealed class ReceivablesReport
{
    public ReportFilter Filter { get; set; } = new();
    public string GeneratedAt { get; set; } = "";
    public List<ReportGroup> Groups { get; set; } = new();     // by invoice status
    public AgingSummary Aging { get; set; } = new();
    public double TotalBilled => Math.Round(Groups.Sum(g => g.Billed), 2);
    public double TotalPaid => Math.Round(Groups.Sum(g => g.Paid), 2);
    public double TotalOutstanding => Math.Round(Groups.Where(g => g.Name != "Paid").Sum(g => g.Balance), 2);
}

public sealed class JobStatusReportRow
{
    public long JobId { get; set; }
    public string Address { get; set; } = "";
    public string? ClientName { get; set; }
    public string? JobNumber { get; set; }
    public string? ExternalRef1 { get; set; }
    public string ProjectType { get; set; } = "";
    public string CurrentStage { get; set; } = "";
    public int PercentComplete { get; set; }
    public double Billed { get; set; }
    public double Paid { get; set; }
    public double Outstanding => Math.Round(Billed - Paid, 2);
}

public sealed class JobStatusReport
{
    public ReportFilter Filter { get; set; } = new();
    public string GeneratedAt { get; set; } = "";
    public List<(string Status, List<JobStatusReportRow> Rows)> Groups { get; set; } = new();
    public double TotalBilled => Math.Round(Groups.SelectMany(g => g.Rows).Sum(r => r.Billed), 2);
    public double TotalPaid => Math.Round(Groups.SelectMany(g => g.Rows).Sum(r => r.Paid), 2);
    public double TotalOutstanding => Math.Round(TotalBilled - TotalPaid, 2);
}

public sealed class MonthlyStatement
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthLabel => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    public string GeneratedAt { get; set; } = "";
    public ReportFilter Filter { get; set; } = new();

    public List<ReportLine> Invoiced { get; set; } = new();     // invoice_date in month
    public List<PaymentLedgerRow> Received { get; set; } = new(); // payment_date in month

    public double InvoicedTotal => Math.Round(Invoiced.Sum(l => l.Billed), 2);
    public double ReceivedTotal => Math.Round(Received.Sum(p => p.Amount), 2);
    public double OutstandingNow { get; set; }
}
