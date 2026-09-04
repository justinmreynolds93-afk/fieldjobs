using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

public sealed class DashboardService
{
    private readonly JobService _jobs = new();
    private readonly StageService _stages = new();
    private readonly InvoiceService _invoices = new();
    private readonly AttentionService _attention = new();
    private readonly ActivityService _activity = new();

    public DashboardStats Build()
    {
        var s = new DashboardStats { MonthLabel = DateTime.Today.ToString("MMMM yyyy") };
        var monthPrefix = DateTime.Today.ToString("yyyy-MM");

        foreach (var job in _jobs.All())
        {
            var stages = _stages.ForJob(job.Id);
            var pct = _stages.PercentComplete(stages);
            if (job.Status == "Complete" || pct == 100) s.JobsComplete++;
            else s.JobsOpen++;

            switch (Vocab.WaitingOn(job.Status))
            {
                case "Me": s.WaitingOnMe++; break;
                case "Agency": s.WaitingOnAgency++; break;
                case "Client": s.WaitingOnClient++; break;
            }

            if (job.Status != "Complete" && pct < 100)
            {
                var stageName = _stages.CurrentStageName(stages);
                var bucket = s.JobsByStage.FirstOrDefault(x => x.Stage == stageName);
                if (bucket is null) s.JobsByStage.Add(new StageCount { Stage = stageName, Count = 1 });
                else bucket.Count++;
            }

            foreach (var inv in _invoices.ForJob(job.Id))
            {
                if (inv.Status == Vocab.InvoiceVoid) continue;
                s.TotalBilled += inv.Billed;
                s.TotalPaid += inv.Paid;
                if (inv.InvoiceDate.StartsWith(monthPrefix)) s.MonthBilled += inv.Billed;

                if (inv.Balance > 0.005)
                    s.Receivables.Add(new ReceivableRow
                    {
                        JobId = job.Id,
                        Job = job.Title,
                        InvoiceNumber = inv.InvoiceNumber,
                        InvoiceDate = inv.InvoiceDate,
                        Status = inv.Status,
                        Billed = inv.Billed,
                        Paid = inv.Paid
                    });
            }
        }

        // This-month payments (by payment date, not invoice date).
        s.MonthPaid = Db.Connection.ExecuteScalar<double?>("""
            SELECT COALESCE(SUM(p.amount),0)
            FROM payments p JOIN invoices i ON i.id = p.invoice_id
            WHERE i.status <> 'Void' AND substr(p.payment_date,1,7) = @m
            """, new { m = monthPrefix }) ?? 0;

        s.TotalBilled = Math.Round(s.TotalBilled, 2);
        s.TotalPaid = Math.Round(s.TotalPaid, 2);
        s.MonthBilled = Math.Round(s.MonthBilled, 2);
        s.MonthPaid = Math.Round(s.MonthPaid, 2);
        s.Receivables = s.Receivables.OrderByDescending(r => r.Balance).ToList();
        s.JobsByStage = s.JobsByStage.OrderBy(x => x.Stage).ToList();

        s.Attention = _attention.Build();
        s.RecentActivity = _activity.Recent(10);

        return s;
    }
}
