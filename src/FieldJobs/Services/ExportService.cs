using System.IO;
using ClosedXML.Excel;
using FieldJobs.Models;

namespace FieldJobs.Services;

/// <summary>Exports every job (with money roll-ups) to CSV or XLSX.</summary>
public sealed class ExportService
{
    private readonly JobService _jobs = new();
    private readonly StageService _stages = new();
    private readonly InvoiceService _invoices = new();

    private IEnumerable<string[]> Rows()
    {
        yield return new[]
        {
            "Job #", Vocab.Ref1Label, Vocab.Ref2Label, "Address", "City", "ZIP", Vocab.ClientFieldLabel, "Project type",
            "Date assigned", "Status", "Current stage", "% complete", "Billed", "Paid", "Outstanding", "Notes"
        };

        foreach (var j in _jobs.All())
        {
            var stages = _stages.ForJob(j.Id);
            var t = _jobs.TotalsFor(j.Id);
            yield return new[]
            {
                j.JobNumber ?? "", j.ExternalRef1 ?? "", j.ExternalRef2 ?? "",
                j.AddressStreet ?? "", j.AddressCity ?? "", j.AddressZip ?? "",
                j.ClientName ?? "", j.ProjectType, j.DateAssigned ?? "", j.Status,
                _stages.CurrentStageName(stages), _stages.PercentComplete(stages).ToString(),
                t.Billed.ToString("0.00"), t.Paid.ToString("0.00"), t.Outstanding.ToString("0.00"),
                (j.Notes ?? "").Replace("\r", " ").Replace("\n", " ")
            };
        }
    }

    public void ToCsv(string path)
    {
        using var w = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        foreach (var row in Rows())
            w.WriteLine(string.Join(",", row.Select(Csv)));

        static string Csv(string v)
            => v.Contains(',') || v.Contains('"') || v.Contains('\n')
                ? "\"" + v.Replace("\"", "\"\"") + "\""
                : v;
    }

    public void ToXlsx(string path)
    {
        using var wb = new XLWorkbook();
        WriteSheet(wb, "Jobs", Rows());
        wb.SaveAs(path);
    }

    // ---------------------------------------------------------------- report exports

    public void ReceivablesToXlsx(ReceivablesReport report, string path)
    {
        using var wb = new XLWorkbook();
        var rows = new List<string[]>
        {
            new[] { "Status", "Job", Vocab.ClientFieldLabel, Vocab.Ref1Label, "Invoice #", "Invoice date", "Date sent",
                    "Stage", "Age (days)", "Age bucket", "Billed", "Paid", "Balance" }
        };
        foreach (var g in report.Groups)
            foreach (var l in g.Lines)
                rows.Add(new[]
                {
                    g.Name, l.Job, l.ClientName ?? "", l.ExternalRef1 ?? "", l.InvoiceNumber,
                    l.InvoiceDate, l.DateSent ?? "", l.Stage ?? "", l.AgeDays.ToString(), l.AgeBucket,
                    l.Billed.ToString("0.00"), l.Paid.ToString("0.00"), l.Balance.ToString("0.00")
                });
        rows.Add(Array.Empty<string>());
        rows.Add(new[] { "TOTAL", "", "", "", "", "", "", "", "", "",
            report.TotalBilled.ToString("0.00"), report.TotalPaid.ToString("0.00"),
            report.TotalOutstanding.ToString("0.00") });
        WriteSheet(wb, "Receivables", rows);

        var aging = new List<string[]>
        {
            new[] { "Current", "1-30", "31-60", "61-90", "90+", "Total" },
            new[]
            {
                report.Aging.Current.ToString("0.00"), report.Aging.D1_30.ToString("0.00"),
                report.Aging.D31_60.ToString("0.00"), report.Aging.D61_90.ToString("0.00"),
                report.Aging.D90Plus.ToString("0.00"), report.Aging.Total.ToString("0.00")
            }
        };
        WriteSheet(wb, "Aging", aging);
        wb.SaveAs(path);
    }

    public void JobStatusToXlsx(JobStatusReport report, string path)
    {
        using var wb = new XLWorkbook();
        var rows = new List<string[]>
        {
            new[] { "Status", "Address", Vocab.ClientFieldLabel, "Job #", Vocab.Ref1Label, "Project type",
                    "Current stage", "% complete", "Billed", "Paid", "Outstanding" }
        };
        foreach (var (status, list) in report.Groups)
            foreach (var r in list)
                rows.Add(new[]
                {
                    status, r.Address, r.ClientName ?? "", r.JobNumber ?? "", r.ExternalRef1 ?? "",
                    r.ProjectType, r.CurrentStage, r.PercentComplete.ToString(),
                    r.Billed.ToString("0.00"), r.Paid.ToString("0.00"), r.Outstanding.ToString("0.00")
                });
        WriteSheet(wb, "Job status", rows);
        wb.SaveAs(path);
    }

    private static void WriteSheet(XLWorkbook wb, string name, IEnumerable<string[]> rows)
    {
        var ws = wb.AddWorksheet(name);
        var r = 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++) ws.Cell(r, c + 1).Value = row[c];
            r++;
        }
        if (r > 1)
        {
            ws.Row(1).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();
        }
    }
}
