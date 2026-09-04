using FieldJobs.Models;
using FieldJobs.Services;
using MigraDoc.DocumentObjectModel;

namespace FieldJobs.Pdf;

/// <summary>A simple, professional invoice PDF to send to Agency.</summary>
public sealed class InvoicePdf
{
    private readonly JobService _jobs = new();
    private readonly InvoiceService _invoices = new();
    private readonly SettingsService _settings = new();

    public string Build(long invoiceId, string outputPath)
    {
        var inv = _invoices.Get(invoiceId);
        var job = _jobs.Get(inv.JobId);
        var lines = _invoices.Lines(invoiceId);
        var payments = _invoices.Payments(invoiceId);

        var billed = lines.Sum(l => l.Amount);
        var paid = payments.Sum(p => p.Amount);
        var balance = Math.Round(billed - paid, 2);

        var doc = PdfKit.NewDocument($"Invoice {inv.InvoiceNumber}");
        var s = PdfKit.NewSection(doc);

        // ---- header: INVOICE + remit-from block
        var header = s.AddTable();
        header.Borders.Width = 0;
        header.AddColumn(Unit.FromInch(3.5));
        header.AddColumn(Unit.FromInch(3.5));
        var hrow = header.AddRow();

        var left = hrow.Cells[0].AddParagraph("INVOICE");
        left.Format.Font.Size = 24;
        left.Format.Font.Bold = true;
        left.Format.Font.Color = PdfKit.Brand;

        var rp = hrow.Cells[1].AddParagraph();
        rp.Format.Alignment = ParagraphAlignment.Right;
        rp.AddFormattedText(_settings.BusinessName, TextFormat.Bold);
        if (!string.IsNullOrWhiteSpace(_settings.Dba)) { rp.AddLineBreak(); rp.AddText(_settings.Dba); }
        foreach (var l in new[] { _settings.Address, _settings.Phone, _settings.Email }
                     .Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            rp.AddLineBreak();
            rp.AddText(l);
        }

        s.AddParagraph().Format.SpaceAfter = 6;

        // ---- meta grid: bill-to + invoice facts
        var meta = s.AddTable();
        meta.Borders.Width = 0;
        meta.AddColumn(Unit.FromInch(3.7));
        meta.AddColumn(Unit.FromInch(3.3));
        var mrow = meta.AddRow();

        var billTo = mrow.Cells[0].AddParagraph();
        billTo.AddFormattedText("Bill to", TextFormat.Bold);
        foreach (var l in _settings.BillTo.Replace("\r", "").Split('\n'))
        {
            billTo.AddLineBreak();
            billTo.AddText(l);
        }

        var facts = mrow.Cells[1].AddParagraph();
        facts.Format.Alignment = ParagraphAlignment.Right;
        facts.AddFormattedText($"Invoice #: {inv.InvoiceNumber}", TextFormat.Bold);
        facts.AddLineBreak();
        facts.AddText($"Date: {PdfKit.Date(inv.InvoiceDate)}");
        facts.AddLineBreak();
        facts.AddText($"Terms: {_settings.PaymentTerms}");
        facts.AddLineBreak();
        facts.AddText($"Status: {inv.Status}");

        // ---- job reference
        var refP = s.AddParagraph();
        refP.Format.SpaceBefore = 10;
        refP.Format.Font.Size = 9;
        refP.AddFormattedText("Project: ", TextFormat.Bold);
        refP.AddText(job.DisplayAddress);
        var idBits = new[]
        {
            job.ClientName is null ? null : $"Client {job.ClientName}",
            job.JobNumber is null ? null : $"Job {job.JobNumber}",
            job.ExternalRef1 is null ? null : $"Agency {job.ExternalRef1}",
            job.ExternalRef2 is null ? null : $"Agency {job.ExternalRef2}",
            inv.StageLabel is null ? null : $"Stage: {inv.StageLabel}",
        }.Where(x => x != null);
        refP.AddLineBreak();
        refP.AddText(string.Join("   ·   ", idBits));

        // ---- line items
        var t = s.AddTable();
        t.Format.SpaceBefore = 10;
        t.Borders.Width = 0.25;
        t.Borders.Color = PdfKit.Line;
        foreach (var w in new[] { 3.9, 0.8, 1.1, 1.2 }) t.AddColumn(Unit.FromInch(w));

        var head = PdfKit.HeaderRow(t);
        foreach (var (h, i) in new[] { "Description", "Qty", "Rate", "Amount" }.Select((h, i) => (h, i)))
        {
            head.Cells[i].AddParagraph(h);
            if (i > 0) head.Cells[i].Format.Alignment = ParagraphAlignment.Right;
        }

        foreach (var l in lines)
        {
            var r = t.AddRow();
            r.Cells[0].AddParagraph(l.Description);
            r.Cells[1].AddParagraph(l.Qty.ToString("0.##"));
            r.Cells[2].AddParagraph(PdfKit.Money(l.Rate));
            r.Cells[3].AddParagraph(PdfKit.Money(l.Amount));
            for (var c = 1; c <= 3; c++) r.Cells[c].Format.Alignment = ParagraphAlignment.Right;
        }

        AddTotalRow(t, "Total", billed, bold: true);
        if (paid > 0.005)
        {
            AddTotalRow(t, "Payments received", -paid, bold: false);
            AddTotalRow(t, "Balance due", balance, bold: true);
        }

        // ---- payments detail
        if (payments.Count > 0)
        {
            s.AddParagraph("Payments").Format.Font.Bold = true;
            foreach (var p in payments)
            {
                var line = s.AddParagraph($"{PdfKit.Date(p.PaymentDate)} — {PdfKit.Money(p.Amount)}"
                    + (string.IsNullOrWhiteSpace(p.Note) ? "" : $"  ({p.Note})"));
                line.Format.Font.Size = 8.5;
                line.Format.Font.Color = PdfKit.Muted;
            }
        }

        // ---- notes / remittance
        var notes = s.AddParagraph();
        notes.Format.SpaceBefore = 12;
        notes.Format.Font.Size = 8.5;
        if (!string.IsNullOrWhiteSpace(inv.Notes))
        {
            notes.AddFormattedText("Notes: ", TextFormat.Bold);
            notes.AddText(inv.Notes);
            notes.AddLineBreak();
        }
        notes.AddText($"Please remit to {_settings.BusinessName}. Thank you.");

        PdfKit.AddFooter(s, _settings.BusinessName);
        PdfKit.Save(doc, outputPath);
        return outputPath;
    }

    private static void AddTotalRow(MigraDoc.DocumentObjectModel.Tables.Table t, string label, double amount, bool bold)
    {
        var r = t.AddRow();
        r.Cells[0].MergeRight = 2;
        var lp = r.Cells[0].AddParagraph(label);
        lp.Format.Alignment = ParagraphAlignment.Right;
        lp.Format.Font.Bold = bold;
        var ap = r.Cells[3].AddParagraph(PdfKit.Money(amount));
        ap.Format.Alignment = ParagraphAlignment.Right;
        ap.Format.Font.Bold = bold;
        if (bold) r.Shading.Color = PdfKit.HeaderFill;
    }
}
