using FieldJobs.Models;
using FieldJobs.Services;
using MigraDoc.DocumentObjectModel;

namespace FieldJobs.Pdf;

/// <summary>One-click "Job Summary" PDF — the printable snapshot of a job.</summary>
public sealed class JobSummaryPdf
{
    private readonly JobService _jobs = new();
    private readonly StageService _stages = new();
    private readonly InvoiceService _invoices = new();
    private readonly FileService _files = new();
    private readonly SettingsService _settings = new();

    public string Build(long jobId, string outputPath)
    {
        var job = _jobs.Get(jobId);
        var stages = _stages.ForJob(jobId);
        var totals = _jobs.TotalsFor(jobId);
        var invoices = _invoices.ForJob(jobId);
        var files = _files.ForJob(jobId);

        var doc = PdfKit.NewDocument($"Job Summary — {job.Title}");
        var s = PdfKit.NewSection(doc);
        PdfKit.AddFooter(s, _settings.BusinessName);

        // ---- title bar
        var title = s.AddParagraph("Job Summary");
        title.Format.Font.Size = 20;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = PdfKit.Brand;
        title.Format.SpaceAfter = 1;

        var who = s.AddParagraph($"{_settings.BusinessName}"
            + (string.IsNullOrWhiteSpace(_settings.Dba) ? "" : $"  ·  {_settings.Dba}"));
        who.Format.Font.Color = PdfKit.Muted;
        who.Format.Font.Size = 8.5;
        var contact = s.AddParagraph(string.Join("   ",
            new[] { _settings.Phone, _settings.Email, _settings.Address }
                .Where(x => !string.IsNullOrWhiteSpace(x))));
        contact.Format.Font.Color = PdfKit.Muted;
        contact.Format.Font.Size = 8.5;
        contact.Format.SpaceAfter = 10;

        // ---- property / references
        s.AddParagraph("Property & references").Style = "Heading1";
        PdfKit.InfoGrid(s, new (string, string)[]
        {
            ("Address", job.DisplayAddress),
            (Vocab.ClientFieldLabel, job.ClientName ?? ""),
            ("Project type", job.ProjectType),
            ("Job number", job.JobNumber ?? ""),
            (Vocab.Ref1Label, job.ExternalRef1 ?? ""),
            (Vocab.Ref2Label, job.ExternalRef2 ?? ""),
            ("Date assigned", PdfKit.Date(job.DateAssigned)),
            ("Status", job.Status),
            ("My role", job.RoleNotes ?? ""),
        });

        var pct = _stages.PercentComplete(stages);
        var prog = s.AddParagraph($"Current stage: {_stages.CurrentStageName(stages)}     "
                                  + $"Overall: {pct}% of required stages complete");
        prog.Format.SpaceBefore = 6;
        prog.Format.Font.Bold = true;

        // ---- stages + checklists
        s.AddParagraph("Stages & checklists").Style = "Heading1";
        foreach (var st in stages)
        {
            var head = s.AddParagraph();
            head.Format.SpaceBefore = 6;
            head.Format.SpaceAfter = 1;
            var name = head.AddFormattedText($"{st.Seq}. {st.Name}", TextFormat.Bold);
            head.AddText($"   —   {st.Status}");
            if (!string.IsNullOrWhiteSpace(st.DateCompleted))
                head.AddText($"  ({PdfKit.Date(st.DateCompleted)})");
            if (st.IsOptional) head.AddText("   [optional]");

            foreach (var item in _stages.Checklist(st.Id))
            {
                var line = s.AddParagraph($"[{(item.IsChecked ? "x" : " ")}]  {item.Text}");
                line.Format.LeftIndent = Unit.FromInch(0.25);
                line.Format.SpaceAfter = 0;
                line.Format.Font.Size = 9;
                line.Format.Font.Color = item.IsChecked ? PdfKit.Ink : PdfKit.Muted;
            }

            if (!string.IsNullOrWhiteSpace(st.Notes))
            {
                var n = s.AddParagraph($"Notes: {st.Notes}");
                n.Format.LeftIndent = Unit.FromInch(0.25);
                n.Format.Font.Italic = true;
                n.Format.Font.Size = 8.5;
                n.Format.Font.Color = PdfKit.Muted;
            }
        }

        // ---- invoices
        s.AddParagraph("Invoices").Style = "Heading1";
        if (invoices.Count == 0)
        {
            s.AddParagraph("No invoices recorded.").Format.Font.Color = PdfKit.Muted;
        }
        else
        {
            var t = s.AddTable();
            t.Borders.Width = 0.25;
            t.Borders.Color = PdfKit.Line;
            foreach (var w in new[] { 1.1, 1.0, 1.9, 1.0, 1.0, 1.0 }) t.AddColumn(Unit.FromInch(w));

            var hr = PdfKit.HeaderRow(t);
            string[] heads = { "Invoice #", "Date", "Stage", "Billed", "Paid", "Balance" };
            for (var i = 0; i < heads.Length; i++) hr.Cells[i].AddParagraph(heads[i]);

            foreach (var inv in invoices)
            {
                var r = t.AddRow();
                r.Cells[0].AddParagraph(inv.InvoiceNumber);
                r.Cells[1].AddParagraph(PdfKit.Date(inv.InvoiceDate));
                r.Cells[2].AddParagraph(inv.StageLabel ?? "—");
                r.Cells[3].AddParagraph(PdfKit.Money(inv.Billed));
                r.Cells[4].AddParagraph(PdfKit.Money(inv.Paid));
                r.Cells[5].AddParagraph(PdfKit.Money(inv.Balance));
                if (inv.Status == Vocab.InvoiceVoid)
                    r.Format.Font.Color = PdfKit.Muted;
                for (var c = 3; c <= 5; c++) r.Cells[c].Format.Alignment = ParagraphAlignment.Right;
            }

            var tr = t.AddRow();
            tr.Format.Font.Bold = true;
            tr.Shading.Color = PdfKit.HeaderFill;
            tr.Cells[0].MergeRight = 2;
            tr.Cells[0].AddParagraph("Totals");
            tr.Cells[3].AddParagraph(PdfKit.Money(totals.Billed));
            tr.Cells[4].AddParagraph(PdfKit.Money(totals.Paid));
            tr.Cells[5].AddParagraph(PdfKit.Money(totals.Outstanding));
            for (var c = 3; c <= 5; c++) tr.Cells[c].Format.Alignment = ParagraphAlignment.Right;
        }

        var bal = s.AddParagraph($"Outstanding balance for this job: {PdfKit.Money(totals.Outstanding)}");
        bal.Format.SpaceBefore = 6;
        bal.Format.Font.Bold = true;
        bal.Format.Font.Color = totals.Outstanding > 0 ? new Color(176, 0, 32) : PdfKit.Ink;

        // ---- files
        s.AddParagraph("Files").Style = "Heading1";
        if (files.Count == 0)
        {
            s.AddParagraph("No files attached.").Format.Font.Color = PdfKit.Muted;
        }
        else
        {
            var t = s.AddTable();
            t.Borders.Width = 0.25;
            t.Borders.Color = PdfKit.Line;
            foreach (var w in new[] { 3.4, 1.3, 1.9, 0.4 }) t.AddColumn(Unit.FromInch(w));
            var hr = PdfKit.HeaderRow(t);
            foreach (var (h, i) in new[] { "File", "Added", "Stage", "Size" }.Select((h, i) => (h, i)))
                hr.Cells[i].AddParagraph(h);
            foreach (var f in files)
            {
                var r = t.AddRow();
                r.Cells[0].AddParagraph(f.OriginalName);
                r.Cells[1].AddParagraph(PdfKit.Date(f.DateAdded));
                r.Cells[2].AddParagraph(StageLabelFor(stages, f.StageKey));
                r.Cells[3].AddParagraph(f.SizeDisplay);
                r.Cells[3].Format.Alignment = ParagraphAlignment.Right;
            }
        }

        // ---- notes
        if (!string.IsNullOrWhiteSpace(job.Notes))
        {
            s.AddParagraph("Notes").Style = "Heading1";
            s.AddParagraph(job.Notes!);
        }

        var gen = s.AddParagraph($"Generated {DateTime.Now:MMMM d, yyyy h:mm tt}. "
            + "This is a personal records / billing summary, not the official grant system of record.");
        gen.Format.SpaceBefore = 14;
        gen.Format.Font.Size = 7.5;
        gen.Format.Font.Color = PdfKit.Muted;

        PdfKit.Save(doc, outputPath);
        return outputPath;
    }

    private static string StageLabelFor(IEnumerable<Stage> stages, string? key)
        => string.IsNullOrEmpty(key) ? "General"
            : stages.FirstOrDefault(s => s.StageKey == key)?.Name ?? key;
}
