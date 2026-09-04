using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace FieldJobs.Pdf;

/// <summary>Shared MigraDoc helpers so the two documents look like one product.</summary>
internal static class PdfKit
{
    public static readonly Color Brand = new(15, 98, 146);
    public static readonly Color Ink = new(33, 37, 41);
    public static readonly Color Muted = new(110, 118, 129);
    public static readonly Color HeaderFill = new(238, 242, 246);
    public static readonly Color Line = new(206, 212, 218);

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string Money(double v) => v.ToString("C", Us);

    public static string Date(string? iso)
        => DateTime.TryParse(iso, out var d) ? d.ToString("MMM d, yyyy") : (iso ?? "");

    public static Document NewDocument(string title)
    {
        var doc = new Document();
        doc.Info.Title = title;
        doc.Info.Author = "FieldJobs";

        var normal = doc.Styles["Normal"]!;
        normal.Font.Name = "Segoe UI";
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = 3;

        var h1 = doc.Styles["Heading1"]!;
        h1.Font.Name = "Segoe UI";
        h1.Font.Size = 15;
        h1.Font.Bold = true;
        h1.Font.Color = Brand;
        h1.ParagraphFormat.SpaceBefore = 12;
        h1.ParagraphFormat.SpaceAfter = 6;

        var footer = doc.Styles["Footer"]!;
        footer.Font.Size = 7.5;
        footer.Font.Color = Muted;

        return doc;
    }

    public static Section NewSection(Document doc)
    {
        var s = doc.AddSection();
        s.PageSetup = doc.DefaultPageSetup.Clone();
        s.PageSetup.PageFormat = PageFormat.Letter;
        s.PageSetup.TopMargin = Unit.FromInch(0.7);
        s.PageSetup.BottomMargin = Unit.FromInch(0.7);
        s.PageSetup.LeftMargin = Unit.FromInch(0.75);
        s.PageSetup.RightMargin = Unit.FromInch(0.75);
        return s;
    }

    public static void AddFooter(Section s, string businessName)
    {
        var p = s.Footers.Primary.AddParagraph();
        p.Style = "Footer";
        p.AddText($"{businessName}  ·  Generated {DateTime.Now:MMM d, yyyy h:mm tt} by FieldJobs  ·  Page ");
        p.AddPageField();
        p.AddText(" of ");
        p.AddNumPagesField();
    }

    /// <summary>Two-column "label / value" info grid.</summary>
    public static Table InfoGrid(Section s, IEnumerable<(string Label, string Value)> pairs)
    {
        var t = s.AddTable();
        t.Borders.Width = 0;
        t.AddColumn(Unit.FromInch(1.7));
        t.AddColumn(Unit.FromInch(5.3));
        foreach (var (label, value) in pairs)
        {
            var r = t.AddRow();
            var l = r.Cells[0].AddParagraph(label);
            l.Format.Font.Color = Muted;
            l.Format.Font.Size = 8.5;
            r.Cells[1].AddParagraph(string.IsNullOrWhiteSpace(value) ? "—" : value);
        }
        return t;
    }

    public static Row HeaderRow(Table t)
    {
        var r = t.AddRow();
        r.Shading.Color = HeaderFill;
        r.Format.Font.Bold = true;
        r.Format.Font.Size = 8.5;
        return r;
    }

    public static void Save(Document doc, string path)
    {
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(path);
    }
}
