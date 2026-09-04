using FieldJobs.Models;
using FieldJobs.Services;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

namespace FieldJobs.Pdf;

/// <summary>Renders the Reports screen output: receivables status, job status, monthly statement.</summary>
public sealed class ReportsPdf
{
    private readonly SettingsService _settings = new();

    // ---------------------------------------------------------------- shared header

    private Section StartDoc(string title, ReportFilter filter, string generatedAt, string? subtitle = null)
    {
        var doc = PdfKit.NewDocument(title);
        var s = PdfKit.NewSection(doc);
        s.PageSetup.Orientation = Orientation.Landscape;
        s.PageSetup.PageWidth = Unit.FromInch(11);
        s.PageSetup.PageHeight = Unit.FromInch(8.5);
        PdfKit.AddFooter(s, _settings.BusinessName);

        var head = s.AddParagraph(title);
        head.Format.Font.Size = 18;
        head.Format.Font.Bold = true;
        head.Format.Font.Color = PdfKit.Brand;
        head.Format.SpaceAfter = 1;

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var sub = s.AddParagraph(subtitle);
            sub.Format.Font.Size = 9;
            sub.Format.Font.Color = PdfKit.Muted;
        }

        var meta = s.AddTable();
        meta.Borders.Width = 0;
        meta.AddColumn(Unit.FromInch(5));
        meta.AddColumn(Unit.FromInch(5));
        var r = meta.AddRow();

        var left = r.Cells[0].AddParagraph();
        left.Format.Font.Size = 8.5;
        left.AddFormattedText("From: ", TextFormat.Bold);
        left.AddText(_settings.BusinessName + (string.IsNullOrWhiteSpace(_settings.Dba) ? "" : $" ({_settings.Dba})"));
        var contactLine = string.Join("   ", new[] { _settings.Phone, _settings.Email }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (contactLine.Length > 0) { left.AddLineBreak(); left.AddText(contactLine); }

        var right = r.Cells[1].AddParagraph();
        right.Format.Alignment = ParagraphAlignment.Right;
        right.Format.Font.Size = 8.5;
        var contact = filter.PreparedForContactId is { } cid ? new ContactService().Get(cid) : new ContactService().Primary();
        if (contact != null)
        {
            right.AddFormattedText("Prepared for: ", TextFormat.Bold);
            right.AddText(contact.Display);
            if (!string.IsNullOrWhiteSpace(contact.Email)) { right.AddLineBreak(); right.AddText(contact.Email); }
        }
        right.AddLineBreak();
        right.AddText($"{filter.RangeLabel}   ·   Generated {generatedAt}");

        var proj = filter.ProjectTypes.Count == 0 ? "All project types" : string.Join(", ", filter.ProjectTypes);
        var pp = s.AddParagraph(proj);
        pp.Format.Font.Size = 8;
        pp.Format.Font.Color = PdfKit.Muted;
        pp.Format.SpaceAfter = 8;

        return s;
    }

    private static Table MoneyTable(Section s, params double[] colInches)
    {
        var t = s.AddTable();
        t.Borders.Width = 0.25;
        t.Borders.Color = PdfKit.Line;
        foreach (var w in colInches) t.AddColumn(Unit.FromInch(w));
        return t;
    }

    private static void RightAlign(Row r, params int[] cells)
    {
        foreach (var c in cells) r.Cells[c].Format.Alignment = ParagraphAlignment.Right;
    }

    // ---------------------------------------------------------------- receivables

    public string Receivables(ReceivablesReport report, string outputPath, string titleOverride = "Receivables Status Report")
    {
        var s = StartDoc(titleOverride, report.Filter, report.GeneratedAt,
            "What has been paid vs. what is still pending, by invoice status.");

        foreach (var g in report.Groups)
        {
            var gh = s.AddParagraph($"{StatusHeading(g.Name)}   ({g.Lines.Count})");
            gh.Format.Font.Size = 11;
            gh.Format.Font.Bold = true;
            gh.Format.SpaceBefore = 10;
            gh.Format.SpaceAfter = 3;
            gh.Format.Font.Color = g.Name == "Paid" ? new Color(30, 125, 70) : PdfKit.Ink;

            var t = MoneyTable(s, 1.9, 1.3, 0.85, 0.85, 1.6, 0.55, 0.8, 0.8, 0.85);
            var hr = PdfKit.HeaderRow(t);
            string[] heads = { "Job", Vocab.ClientFieldLabel, "Invoice #", "Date", "Stage / section", "Age", "Billed", "Paid", "Balance" };
            for (var i = 0; i < heads.Length; i++) hr.Cells[i].AddParagraph(heads[i]);
            RightAlign(hr, 5, 6, 7, 8);

            foreach (var l in g.Lines)
            {
                var r = t.AddRow();
                r.Cells[0].AddParagraph(l.Job);
                r.Cells[1].AddParagraph(l.ClientName ?? "");
                r.Cells[2].AddParagraph(l.InvoiceNumber);
                r.Cells[3].AddParagraph(PdfKit.Date(l.InvoiceDate));
                r.Cells[4].AddParagraph(l.Stage ?? "—");
                r.Cells[5].AddParagraph(l.Balance > 0.005 ? $"{l.AgeDays}d" : "—");
                r.Cells[6].AddParagraph(PdfKit.Money(l.Billed));
                r.Cells[7].AddParagraph(PdfKit.Money(l.Paid));
                r.Cells[8].AddParagraph(PdfKit.Money(l.Balance));
                RightAlign(r, 5, 6, 7, 8);
            }

            var sub = t.AddRow();
            sub.Shading.Color = PdfKit.HeaderFill;
            sub.Format.Font.Bold = true;
            sub.Cells[0].MergeRight = 5;
            sub.Cells[0].AddParagraph($"{g.Name} subtotal");
            sub.Cells[6].AddParagraph(PdfKit.Money(g.Billed));
            sub.Cells[7].AddParagraph(PdfKit.Money(g.Paid));
            sub.Cells[8].AddParagraph(PdfKit.Money(g.Balance));
            RightAlign(sub, 6, 7, 8);
        }

        // Grand totals + aging
        var gt = s.AddParagraph("Totals");
        gt.Format.Font.Size = 12;
        gt.Format.Font.Bold = true;
        gt.Format.SpaceBefore = 14;
        gt.Format.SpaceAfter = 3;

        var tt = MoneyTable(s, 3.0, 1.5, 1.5, 1.5);
        var tth = PdfKit.HeaderRow(tt);
        foreach (var (h, i) in new[] { "", "Billed", "Paid", "Outstanding" }.Select((h, i) => (h, i)))
            tth.Cells[i].AddParagraph(h);
        RightAlign(tth, 1, 2, 3);
        var trow = tt.AddRow();
        trow.Format.Font.Bold = true;
        trow.Cells[0].AddParagraph("All invoices in range");
        trow.Cells[1].AddParagraph(PdfKit.Money(report.TotalBilled));
        trow.Cells[2].AddParagraph(PdfKit.Money(report.TotalPaid));
        trow.Cells[3].AddParagraph(PdfKit.Money(report.TotalOutstanding));
        RightAlign(trow, 1, 2, 3);

        var ah = s.AddParagraph("Outstanding balance by age");
        ah.Format.Font.Size = 11;
        ah.Format.Font.Bold = true;
        ah.Format.SpaceBefore = 12;
        ah.Format.SpaceAfter = 3;

        var at = MoneyTable(s, 1.6, 1.4, 1.4, 1.4, 1.4, 1.6);
        var ath = PdfKit.HeaderRow(at);
        foreach (var (h, i) in new[] { "Current", "1–30 days", "31–60 days", "61–90 days", "90+ days", "Total" }
                     .Select((h, i) => (h, i)))
        {
            ath.Cells[i].AddParagraph(h);
            ath.Cells[i].Format.Alignment = ParagraphAlignment.Right;
        }
        var arow = at.AddRow();
        arow.Cells[0].AddParagraph(PdfKit.Money(report.Aging.Current));
        arow.Cells[1].AddParagraph(PdfKit.Money(report.Aging.D1_30));
        arow.Cells[2].AddParagraph(PdfKit.Money(report.Aging.D31_60));
        arow.Cells[3].AddParagraph(PdfKit.Money(report.Aging.D61_90));
        arow.Cells[4].AddParagraph(PdfKit.Money(report.Aging.D90Plus));
        arow.Cells[5].AddParagraph(PdfKit.Money(report.Aging.Total));
        arow.Format.Font.Bold = true;
        for (var i = 0; i < 6; i++) arow.Cells[i].Format.Alignment = ParagraphAlignment.Right;

        Disclaimer(s);
        PdfKit.Save(SectionDoc(s), outputPath);
        return outputPath;
    }

    // ---------------------------------------------------------------- job status

    public string JobStatus(JobStatusReport report, string outputPath)
    {
        var s = StartDoc("Job Status Summary", report.Filter, report.GeneratedAt,
            "Every job by status — stage reached, progress, and money.");

        foreach (var (status, rows) in report.Groups)
        {
            var gh = s.AddParagraph($"{status}   ({rows.Count})");
            gh.Format.Font.Size = 11;
            gh.Format.Font.Bold = true;
            gh.Format.SpaceBefore = 10;
            gh.Format.SpaceAfter = 3;

            var t = MoneyTable(s, 2.0, 1.3, 0.75, 1.0, 1.5, 0.45, 0.8, 0.8, 0.9);
            var hr = PdfKit.HeaderRow(t);
            string[] heads = { "Address", Vocab.ClientFieldLabel, "Job #", Vocab.Ref1Label, "Current stage", "%", "Billed", "Paid", "Outstanding" };
            for (var i = 0; i < heads.Length; i++) hr.Cells[i].AddParagraph(heads[i]);
            RightAlign(hr, 5, 6, 7, 8);

            foreach (var row in rows)
            {
                var r = t.AddRow();
                r.Cells[0].AddParagraph(row.Address);
                r.Cells[1].AddParagraph(row.ClientName ?? "");
                r.Cells[2].AddParagraph(row.JobNumber ?? "");
                r.Cells[3].AddParagraph(row.ExternalRef1 ?? "");
                r.Cells[4].AddParagraph(row.CurrentStage);
                r.Cells[5].AddParagraph($"{row.PercentComplete}%");
                r.Cells[6].AddParagraph(PdfKit.Money(row.Billed));
                r.Cells[7].AddParagraph(PdfKit.Money(row.Paid));
                r.Cells[8].AddParagraph(PdfKit.Money(row.Outstanding));
                RightAlign(r, 5, 6, 7, 8);
            }

            var sub = t.AddRow();
            sub.Shading.Color = PdfKit.HeaderFill;
            sub.Format.Font.Bold = true;
            sub.Cells[0].MergeRight = 5;
            sub.Cells[0].AddParagraph($"{status} subtotal");
            sub.Cells[6].AddParagraph(PdfKit.Money(rows.Sum(x => x.Billed)));
            sub.Cells[7].AddParagraph(PdfKit.Money(rows.Sum(x => x.Paid)));
            sub.Cells[8].AddParagraph(PdfKit.Money(rows.Sum(x => x.Outstanding)));
            RightAlign(sub, 6, 7, 8);
        }

        var gt = s.AddParagraph(
            $"All jobs — Billed {PdfKit.Money(report.TotalBilled)}   ·   "
            + $"Paid {PdfKit.Money(report.TotalPaid)}   ·   Outstanding {PdfKit.Money(report.TotalOutstanding)}");
        gt.Format.Font.Bold = true;
        gt.Format.SpaceBefore = 12;

        Disclaimer(s);
        PdfKit.Save(SectionDoc(s), outputPath);
        return outputPath;
    }

    // ---------------------------------------------------------------- monthly statement

    public string MonthlyStatement(MonthlyStatement stmt, string outputPath)
    {
        var doc = PdfKit.NewDocument($"Statement — {stmt.MonthLabel}");
        var s = PdfKit.NewSection(doc);
        PdfKit.AddFooter(s, _settings.BusinessName);

        var head = s.AddParagraph($"Monthly Statement — {stmt.MonthLabel}");
        head.Format.Font.Size = 18;
        head.Format.Font.Bold = true;
        head.Format.Font.Color = PdfKit.Brand;

        var meta = s.AddParagraph();
        meta.Format.Font.Size = 8.5;
        meta.Format.Font.Color = PdfKit.Muted;
        var contact = stmt.Filter.PreparedForContactId is { } cid
            ? new ContactService().Get(cid) : new ContactService().Primary();
        meta.AddText($"{_settings.BusinessName}   ·   Generated {stmt.GeneratedAt}");
        if (contact != null) { meta.AddLineBreak(); meta.AddText($"Prepared for {contact.Display}"); }
        meta.Format.SpaceAfter = 10;

        s.AddParagraph("Invoiced this month").Style = "Heading1";
        if (stmt.Invoiced.Count == 0)
            s.AddParagraph("Nothing invoiced this month.").Format.Font.Color = PdfKit.Muted;
        else
        {
            var t = MoneyTable(s, 0.9, 0.85, 2.3, 1.5, 0.55, 0.9);
            var hr = PdfKit.HeaderRow(t);
            foreach (var (h, i) in new[] { "Invoice #", "Date", "Job", "Stage", "Status", "Amount" }.Select((h, i) => (h, i)))
                hr.Cells[i].AddParagraph(h);
            hr.Cells[5].Format.Alignment = ParagraphAlignment.Right;
            foreach (var l in stmt.Invoiced)
            {
                var r = t.AddRow();
                r.Cells[0].AddParagraph(l.InvoiceNumber);
                r.Cells[1].AddParagraph(PdfKit.Date(l.InvoiceDate));
                r.Cells[2].AddParagraph(l.Job);
                r.Cells[3].AddParagraph(l.Stage ?? "—");
                r.Cells[4].AddParagraph(l.Status);
                r.Cells[5].AddParagraph(PdfKit.Money(l.Billed));
                r.Cells[5].Format.Alignment = ParagraphAlignment.Right;
            }
            var tr = t.AddRow();
            tr.Shading.Color = PdfKit.HeaderFill;
            tr.Format.Font.Bold = true;
            tr.Cells[0].MergeRight = 4;
            tr.Cells[0].AddParagraph("Total invoiced");
            tr.Cells[5].AddParagraph(PdfKit.Money(stmt.InvoicedTotal));
            tr.Cells[5].Format.Alignment = ParagraphAlignment.Right;
        }

        s.AddParagraph("Payments received this month").Style = "Heading1";
        if (stmt.Received.Count == 0)
            s.AddParagraph("No payments received this month.").Format.Font.Color = PdfKit.Muted;
        else
        {
            var t = MoneyTable(s, 0.85, 0.9, 2.0, 1.35, 1.0, 0.9);
            var hr = PdfKit.HeaderRow(t);
            foreach (var (h, i) in new[] { "Date", "Invoice #", "Job", "Method", "Reference", "Amount" }.Select((h, i) => (h, i)))
                hr.Cells[i].AddParagraph(h);
            hr.Cells[5].Format.Alignment = ParagraphAlignment.Right;
            foreach (var p in stmt.Received)
            {
                var r = t.AddRow();
                r.Cells[0].AddParagraph(PdfKit.Date(p.PaymentDate));
                r.Cells[1].AddParagraph(p.InvoiceNumber);
                r.Cells[2].AddParagraph(p.JobTitle);
                r.Cells[3].AddParagraph(p.Method ?? "—");
                r.Cells[4].AddParagraph(p.Reference ?? "");
                r.Cells[5].AddParagraph(PdfKit.Money(p.Amount));
                r.Cells[5].Format.Alignment = ParagraphAlignment.Right;
            }
            var tr = t.AddRow();
            tr.Shading.Color = PdfKit.HeaderFill;
            tr.Format.Font.Bold = true;
            tr.Cells[0].MergeRight = 4;
            tr.Cells[0].AddParagraph("Total received");
            tr.Cells[5].AddParagraph(PdfKit.Money(stmt.ReceivedTotal));
            tr.Cells[5].Format.Alignment = ParagraphAlignment.Right;
        }

        var summary = s.AddParagraph();
        summary.Format.SpaceBefore = 14;
        summary.Format.Font.Bold = true;
        summary.AddText($"Invoiced this month: {PdfKit.Money(stmt.InvoicedTotal)}");
        summary.AddLineBreak();
        summary.AddText($"Received this month: {PdfKit.Money(stmt.ReceivedTotal)}");
        summary.AddLineBreak();
        summary.AddText($"Outstanding across all jobs as of today: {PdfKit.Money(stmt.OutstandingNow)}");

        Disclaimer(s);
        PdfKit.Save(doc, outputPath);
        return outputPath;
    }

    // ---------------------------------------------------------------- helpers

    private static string StatusHeading(string status) => status switch
    {
        "Paid" => "PAID",
        "Partial" => "PARTIALLY PAID",
        "Sent" => "SENT — awaiting payment",
        "Draft" => "DRAFT — not yet sent",
        _ => status.ToUpperInvariant()
    };

    private static void Disclaimer(Section s)
    {
        var d = s.AddParagraph(
            "This is a personal records / billing summary produced by FieldJobs. "
            + "It is not the official grant system of record.");
        d.Format.SpaceBefore = 16;
        d.Format.Font.Size = 7.5;
        d.Format.Font.Color = PdfKit.Muted;
    }

    private static Document SectionDoc(Section s) => (Document)s.Document!;
}
