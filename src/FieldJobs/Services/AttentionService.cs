using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

/// <summary>Builds the dashboard "Needs attention" list: overdue money, stale jobs, gaps.</summary>
public sealed class AttentionService
{
    private readonly JobService _jobs = new();
    private readonly StageService _stages = new();
    private readonly InvoiceService _invoices = new();

    public List<AttentionItem> Build()
    {
        var items = new List<AttentionItem>();

        // 1. Overdue / aged invoices
        foreach (var inv in _invoices.All())
        {
            if (inv.Status is Vocab.InvoiceVoid or Vocab.InvoicePaid || inv.Balance <= 0.005) continue;
            if (!inv.IsOverdue) continue;
            items.Add(new AttentionItem
            {
                Severity = inv.DaysOutstanding > 60 ? "high" : "warn",
                Title = $"{inv.InvoiceNumber} is {inv.DaysOutstanding} days out — {inv.Balance:C0} unpaid",
                Detail = $"{inv.JobTitle} · {inv.Status}"
                         + (string.IsNullOrEmpty(inv.DateSent) ? " · no send date recorded" : $" · sent {inv.DateSent}"),
                JobId = inv.JobId,
                InvoiceId = inv.Id,
                Target = "invoice"
            });
        }

        // 2. Stale jobs — no logged activity in a while and not complete
        foreach (var job in _jobs.All())
        {
            var stages = _stages.ForJob(job.Id);
            var pct = _stages.PercentComplete(stages);
            if (job.Status == "Complete" || pct == 100) continue;

            var last = Db.Connection.ExecuteScalar<string?>(
                "SELECT MAX(ts) FROM activity WHERE job_id=@id", new { id = job.Id }) ?? job.UpdatedAt;
            if (DateTime.TryParse(last, out var lastDt))
            {
                var days = (int)(DateTime.Now - lastDt).TotalDays;
                if (days >= Vocab.StaleJobDays)
                    items.Add(new AttentionItem
                    {
                        Severity = days >= Vocab.StaleJobDays * 2 ? "warn" : "info",
                        Title = $"{job.Title} — no activity in {days} days",
                        Detail = $"{job.Status} · {_stages.CurrentStageName(stages)} · {pct}%",
                        JobId = job.Id,
                        Target = "job"
                    });
            }

            // 3. The most-advanced completed stage on this job has no file attached
            var lastComplete = stages
                .Where(s => s.Status == Vocab.StageComplete)
                .OrderByDescending(s => s.Seq)
                .FirstOrDefault();
            if (lastComplete != null && !_stages.HasFiles(lastComplete.Id))
                items.Add(new AttentionItem
                {
                    Severity = "info",
                    Title = $"{job.Title} — “{lastComplete.Name}” complete with no file",
                    Detail = "Attach the report or photos for your records.",
                    JobId = job.Id,
                    Target = "job"
                });
        }

        // 4. Backup nag
        var lastBackup = Db.Connection.ExecuteScalar<string?>(
            "SELECT value FROM settings WHERE key='last_backup_at'");
        if (string.IsNullOrWhiteSpace(lastBackup))
            items.Add(new AttentionItem
            {
                Severity = "warn", Title = "No backup has been made yet",
                Detail = "Settings → Back up now (point it at OneDrive).", Target = "backup"
            });
        else if (DateTime.TryParse(lastBackup, out var lb) &&
                 (DateTime.Now - lb).TotalDays >= Vocab.BackupNagDays)
            items.Add(new AttentionItem
            {
                Severity = "info",
                Title = $"Last backup was {(int)(DateTime.Now - lb).TotalDays} days ago",
                Detail = "Settings → Back up now.", Target = "backup"
            });

        return items
            .OrderBy(i => i.SeverityRank)
            .ThenByDescending(i => i.InvoiceId.HasValue)
            .ToList();
    }
}
