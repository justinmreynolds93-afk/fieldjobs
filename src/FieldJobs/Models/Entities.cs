namespace FieldJobs.Models;

// Plain data holders that map 1:1 to database rows (Dapper fills them by column name).

public sealed class Job
{
    public long Id { get; set; }
    public string? JobNumber { get; set; }
    public string? ExternalRef1 { get; set; }
    public string? ExternalRef2 { get; set; }
    public string? AddressStreet { get; set; }
    public string? AddressCity { get; set; }
    public string? AddressZip { get; set; }
    public string? ClientName { get; set; }
    public string ProjectType { get; set; } = "Standard";
    public string? DateAssigned { get; set; }      // ISO yyyy-MM-dd
    public string? RoleNotes { get; set; }
    public string Status { get; set; } = "Not started";
    public string? Notes { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    public string DisplayAddress =>
        string.Join(", ", new[] { AddressStreet, AddressCity, AddressZip }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Title =>
        string.IsNullOrWhiteSpace(AddressStreet)
            ? (string.IsNullOrWhiteSpace(ClientName) ? $"Job #{Id}" : ClientName!)
            : AddressStreet!;
}

public sealed class Stage
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public int Seq { get; set; }
    public string StageKey { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsOptional { get; set; }
    public string Status { get; set; } = "Not started";
    public string? DateCompleted { get; set; }
    public string? Notes { get; set; }
}

public sealed class ChecklistItem
{
    public long Id { get; set; }
    public long StageId { get; set; }
    public string Text { get; set; } = "";
    public bool IsChecked { get; set; }
    public int Seq { get; set; }
}

public sealed class JobFile
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public long? StageId { get; set; }
    public string? StageKey { get; set; }
    public long? InvoiceId { get; set; }
    public string DocType { get; set; } = "file";   // file | invoice_sent | payment_proof
    public string OriginalName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string DateAdded { get; set; } = "";
    public string? Note { get; set; }

    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:0.0} KB",
        _ => $"{SizeBytes / (1024.0 * 1024.0):0.0} MB"
    };

    public string DocTypeLabel => DocType switch
    {
        "invoice_sent" => "Invoice sent",
        "payment_proof" => "Proof of payment",
        _ => "File"
    };
}

public sealed class Invoice
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string? DateSent { get; set; }
    public string? SentTo { get; set; }
    public string? StageKey { get; set; }
    public string? StageLabel { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class InvoiceLine
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string Description { get; set; } = "";
    public double Qty { get; set; } = 1;
    public double Rate { get; set; }
    public int Seq { get; set; }
    public double Amount => Math.Round(Qty * Rate, 2);
}

public sealed class Payment
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string PaymentDate { get; set; } = "";
    public double Amount { get; set; }
    public string? Method { get; set; }      // ACH / Check / Card / Other
    public string? Reference { get; set; }   // check number, ACH trace, etc.
    public string? Note { get; set; }
}

public sealed class Contact
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }

    public string Display => string.IsNullOrWhiteSpace(Role) ? Name : $"{Name} — {Role}";
    public override string ToString() => Display;
}

public sealed class ActivityEntry
{
    public long Id { get; set; }
    public long? JobId { get; set; }
    public string Ts { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Summary { get; set; } = "";
}

public sealed class FeeScheduleItem
{
    public long Id { get; set; }
    public string Label { get; set; } = "";
    public double Amount { get; set; }
    public int SortOrder { get; set; }
}

public sealed class StageTemplate
{
    public long Id { get; set; }
    public int Seq { get; set; }
    public string StageKey { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsOptional { get; set; }
    public override string ToString() => Name;
}

public sealed class ChecklistTemplate
{
    public long Id { get; set; }
    public string StageKey { get; set; } = "";
    public string Text { get; set; } = "";
    public int Seq { get; set; }
}
