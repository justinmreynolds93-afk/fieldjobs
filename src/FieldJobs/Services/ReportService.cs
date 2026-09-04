using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

/// <summary>Builds the data behind the Reports screen (PDF/Excel rendering lives elsewhere).</summary>
public sealed class ReportService
{
    private readonly JobService _jobs = new();
    private readonly StageService _stages = new();
    private readonly InvoiceService _invoices = new();

    // Order groups worst-owing first, Paid last.
    private static readonly string[] StatusOrder = { "Partial", "Sent", "Draft", "Paid", "Void" };

    private static string Now() => DateTime.Now.ToString("MMMM d, yyyy h:mm tt");

    // ---------------------------------------------------------------- receivables

    public ReceivablesReport Receivables(ReportFilter filter)
    {
        var report = new ReceivablesReport { Filter = filter, GeneratedAt = Now() };
        var lines = new List<ReportLine>();

        foreach (var job in _jobs.All())
        {
            if (!filter.MatchesProject(job.ProjectType)) continue;

            foreach (var inv in Db.Connection.Query<Invoice>(
                         "SELECT * FROM invoices WHERE job_id=@id", new { id = job.Id }))
            {
                if (inv.Status == Vocab.InvoiceVoid) continue;
                if (!filter.MatchesDate(inv.InvoiceDate)) continue;

                var billed = _invoices.LineTotal(inv.Id);
                var paid = _invoices.PaidTotal(inv.Id);
                var days = InvoiceService.DaysOutstanding(inv);

                lines.Add(new ReportLine
                {
                    JobId = job.Id,
                    Job = job.Title,
                    ClientName = job.ClientName,
                    ExternalRef1 = job.ExternalRef1,
                    InvoiceNumber = inv.InvoiceNumber,
                    InvoiceDate = inv.InvoiceDate,
                    DateSent = inv.DateSent,
                    Stage = inv.StageLabel,
                    Status = inv.Status,
                    Billed = billed,
                    Paid = paid,
                    AgeDays = days,
                    AgeBucket = InvoiceService.AgeBucket(days)
                });
            }
        }

        report.Groups = lines
            .GroupBy(l => l.Status)
            .OrderBy(g => Array.IndexOf(StatusOrder, g.Key) is var i && i >= 0 ? i : 99)
            .Select(g => new ReportGroup
            {
                Name = g.Key,
                Lines = g.OrderByDescending(l => l.Balance).ThenBy(l => l.Job).ToList()
            })
            .ToList();

        // Aging is over unpaid balance only.
        var aging = new AgingSummary();
        foreach (var l in lines.Where(l => l.Balance > 0.005))
        {
            switch (l.AgeBucket)
            {
                case "Current": aging.Current += l.Balance; break;
                case "1–30": aging.D1_30 += l.Balance; break;
                case "31–60": aging.D31_60 += l.Balance; break;
                case "61–90": aging.D61_90 += l.Balance; break;
                default: aging.D90Plus += l.Balance; break;
            }
        }
        aging.Current = Math.Round(aging.Current, 2);
        aging.D1_30 = Math.Round(aging.D1_30, 2);
        aging.D31_60 = Math.Round(aging.D31_60, 2);
        aging.D61_90 = Math.Round(aging.D61_90, 2);
        aging.D90Plus = Math.Round(aging.D90Plus, 2);
        report.Aging = aging;

        return report;
    }

    /// <summary>The same report narrowed to a single status — for "separate PDF per status".</summary>
    public ReceivablesReport ReceivablesForStatus(ReportFilter filter, string status)
    {
        var full = Receivables(filter);
        full.Groups = full.Groups.Where(g => g.Name == status).ToList();
        return full;
    }

    // ---------------------------------------------------------------- job status summary

    public JobStatusReport JobStatus(ReportFilter filter)
    {
        var report = new JobStatusReport { Filter = filter, GeneratedAt = Now() };

        var rows = new List<(string status, JobStatusReportRow row)>();
        foreach (var job in _jobs.All())
        {
            if (!filter.MatchesProject(job.ProjectType)) continue;
            var stages = _stages.ForJob(job.Id);
            var totals = _jobs.TotalsFor(job.Id);
            rows.Add((job.Status, new JobStatusReportRow
            {
                JobId = job.Id,
                Address = job.DisplayAddress,
                ClientName = job.ClientName,
                JobNumber = job.JobNumber,
                ExternalRef1 = job.ExternalRef1,
                ProjectType = job.ProjectType,
                CurrentStage = _stages.CurrentStageName(stages),
                PercentComplete = _stages.PercentComplete(stages),
                Billed = totals.Billed,
                Paid = totals.Paid
            }));
        }

        report.Groups = rows
            .GroupBy(x => x.status)
            .OrderBy(g => Array.IndexOf(Vocab.JobStatuses, g.Key) is var i && i >= 0 ? i : 99)
            .Select(g => (g.Key, g.Select(x => x.row).OrderBy(r => r.Address).ToList()))
            .ToList();

        return report;
    }

    // ---------------------------------------------------------------- monthly statement

    public MonthlyStatement Monthly(int year, int month, ReportFilter filter)
    {
        var stmt = new MonthlyStatement
        {
            Year = year, Month = month, Filter = filter, GeneratedAt = Now()
        };
        var prefix = $"{year:0000}-{month:00}";

        foreach (var job in _jobs.All())
        {
            if (!filter.MatchesProject(job.ProjectType)) continue;

            foreach (var inv in Db.Connection.Query<Invoice>(
                         "SELECT * FROM invoices WHERE job_id=@id", new { id = job.Id }))
            {
                if (inv.Status == Vocab.InvoiceVoid) continue;

                if (inv.InvoiceDate.StartsWith(prefix))
                    stmt.Invoiced.Add(new ReportLine
                    {
                        JobId = job.Id, Job = job.Title, ExternalRef1 = job.ExternalRef1,
                        InvoiceNumber = inv.InvoiceNumber, InvoiceDate = inv.InvoiceDate,
                        Stage = inv.StageLabel, Status = inv.Status,
                        Billed = _invoices.LineTotal(inv.Id), Paid = _invoices.PaidTotal(inv.Id)
                    });

                foreach (var p in _invoices.Payments(inv.Id).Where(p => p.PaymentDate.StartsWith(prefix)))
                    stmt.Received.Add(new PaymentLedgerRow
                    {
                        Id = p.Id, InvoiceId = inv.Id, JobId = job.Id, JobTitle = job.Title,
                        InvoiceNumber = inv.InvoiceNumber, PaymentDate = p.PaymentDate,
                        Amount = p.Amount, Method = p.Method, Reference = p.Reference, Note = p.Note
                    });
            }
        }

        stmt.Invoiced = stmt.Invoiced.OrderBy(l => l.InvoiceDate).ToList();
        stmt.Received = stmt.Received.OrderBy(p => p.PaymentDate).ToList();

        var billed = _jobs.All().Where(j => filter.MatchesProject(j.ProjectType))
            .Sum(j => _jobs.TotalsFor(j.Id).Outstanding);
        stmt.OutstandingNow = Math.Round(billed, 2);

        return stmt;
    }
}
