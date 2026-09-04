using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

/// <summary>Key/value application settings plus the editable fee schedule.</summary>
public sealed class SettingsService
{
    public string Get(string key, string fallback = "")
        => Db.Connection.ExecuteScalar<string?>("SELECT value FROM settings WHERE key=@key", new { key }) ?? fallback;

    public void Set(string key, string? value)
        => Db.Connection.Execute("INSERT OR REPLACE INTO settings(key,value) VALUES (@key,@value)",
            new { key, value = value ?? "" });

    // Convenience accessors used by the PDF headers and invoice defaults.
    public string BusinessName => Get("business_name", "Your Name / Business");
    public string Dba => Get("dba");
    public string Address => Get("address");
    public string Email => Get("email");
    public string Phone => Get("phone");
    public string BackupFolder => Get("backup_folder", AppPaths.DefaultBackupFolder);
    public string Theme => Get("theme", "System");
    public string InvoicePrefix => Get("invoice_prefix", "FJ-");
    public string PaymentTerms => Get("payment_terms", "Net 30");
    public string BillTo => Get("bill_to", "Agency\nthe agency's assistance program");

    public List<FeeScheduleItem> FeeSchedule()
        => Db.Connection.Query<FeeScheduleItem>(
            "SELECT id, label, amount, sort_order FROM fee_schedule ORDER BY sort_order, id").ToList();

    public void ReplaceFeeSchedule(IEnumerable<FeeScheduleItem> items)
    {
        Db.InTransaction(_ =>
        {
            Db.Connection.Execute("DELETE FROM fee_schedule");
            var i = 0;
            foreach (var it in items)
            {
                Db.Connection.Execute(
                    "INSERT INTO fee_schedule(label, amount, sort_order) VALUES (@Label,@Amount,@sort)",
                    new { it.Label, it.Amount, sort = i++ });
            }
        });
    }

    /// <summary>Suggested fee for a stage key, used to pre-fill new invoices.</summary>
    public double FeeForStage(string stageKey)
    {
        var label = stageKey switch
        {
            "preview" => "Preview / initial inspection",
            "final_walkthrough" => "Final walkthrough",
            "scope_writing" => "Scope writing",
            _ => null
        };
        if (label is null) return 0;
        return Db.Connection.ExecuteScalar<double?>(
            "SELECT amount FROM fee_schedule WHERE label=@label LIMIT 1", new { label }) ?? 0;
    }
}
