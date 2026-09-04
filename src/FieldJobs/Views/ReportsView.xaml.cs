using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FieldJobs.Data;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class ReportsView : UserControl
{
    private const string Receivables = "Receivables status report";
    private const string JobStatus = "Job status summary";
    private const string Monthly = "Monthly statement";

    private readonly ReportService _reports = new();
    private readonly ExportService _export = new();
    private readonly ContactService _contacts = new();
    private bool _ready;

    public ReportsView()
    {
        InitializeComponent();

        TypeBox.ItemsSource = new[] { Receivables, JobStatus, Monthly };
        PresetBox.ItemsSource = new[] { "All dates", "This month", "This quarter", "This year", "Last 90 days", "Custom" };

        MonthBox.ItemsSource = Enumerable.Range(1, 12).Select(m => new DateTime(2000, m, 1).ToString("MMMM")).ToList();
        YearBox.ItemsSource = Enumerable.Range(DateTime.Today.Year - 3, 4).Reverse().ToList();
        MonthBox.SelectedIndex = DateTime.Today.Month - 1;
        YearBox.SelectedItem = DateTime.Today.Year;

        foreach (var pt in Vocab.ProjectTypes)
            ProjectTypesPanel.Children.Add(new CheckBox { Content = pt, IsChecked = true, Margin = new Thickness(0, 2, 0, 2) });

        var contacts = _contacts.All();
        ContactBox.ItemsSource = contacts;
        ContactBox.SelectedItem = contacts.FirstOrDefault(c => c.IsPrimary) ?? contacts.FirstOrDefault();

        TypeBox.SelectedIndex = 0;
        PresetBox.SelectedItem = "All dates";
        _ready = true;
        Loaded += (_, _) => Rebuild();
    }

    // ---------------------------------------------------------------- filter <- UI

    private ReportFilter CurrentFilter()
    {
        var f = new ReportFilter();
        if (PresetBox.SelectedItem as string != "All dates" || TypeBox.SelectedItem as string == Monthly)
        {
            f.From = FromBox.SelectedDate;
            f.To = ToBox.SelectedDate;
        }
        f.ProjectTypes = ProjectTypesPanel.Children.OfType<CheckBox>()
            .Where(cb => cb.IsChecked == true).Select(cb => (string)cb.Content).ToList();
        if (f.ProjectTypes.Count == Vocab.ProjectTypes.Length) f.ProjectTypes.Clear(); // all = no filter
        if (ContactBox.SelectedItem is Contact c) f.PreparedForContactId = c.Id;
        return f;
    }

    private void ApplyPreset(string preset)
    {
        var today = DateTime.Today;
        (DateTime? from, DateTime? to) = preset switch
        {
            "This month" => (new DateTime(today.Year, today.Month, 1), today),
            "This quarter" => (new DateTime(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1), today),
            "This year" => (new DateTime(today.Year, 1, 1), today),
            "Last 90 days" => (today.AddDays(-90), today),
            "All dates" => ((DateTime?)null, (DateTime?)null),
            _ => (FromBox.SelectedDate, ToBox.SelectedDate)
        };
        _ready = false;
        FromBox.SelectedDate = from;
        ToBox.SelectedDate = to;
        _ready = true;
    }

    // ---------------------------------------------------------------- UI events

    private void Type_Changed(object sender, SelectionChangedEventArgs e)
    {
        var t = TypeBox.SelectedItem as string;
        var isMonthly = t == Monthly;
        RangePanel.Visibility = isMonthly ? Visibility.Collapsed : Visibility.Visible;
        MonthPanel.Visibility = isMonthly ? Visibility.Visible : Visibility.Collapsed;
        PerStatusButton.Visibility = t == Receivables ? Visibility.Visible : Visibility.Collapsed;
        if (_ready) Rebuild();
    }

    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyPreset(PresetBox.SelectedItem as string ?? "All dates");
        Rebuild();
    }

    private void Dates_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && PresetBox.SelectedItem as string != "Custom" && PresetBox.SelectedItem as string != "All dates")
        { /* leave preset label alone; user tweak still respected on Rebuild */ }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Rebuild();

    // ---------------------------------------------------------------- preview

    private void Rebuild()
    {
        try
        {
            switch (TypeBox.SelectedItem as string)
            {
                case Receivables: PreviewReceivables(); break;
                case JobStatus: PreviewJobStatus(); break;
                case Monthly: PreviewMonthly(); break;
            }
        }
        catch (Exception ex)
        {
            Infra.Log.Error("ReportsView.Rebuild", ex);
            MessageBox.Show(ex.Message, "Reports", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PreviewReceivables()
    {
        var r = _reports.Receivables(CurrentFilter());
        PreviewTitle.Text = "Receivables Status Report";
        PreviewSummary.Text =
            $"{r.Filter.RangeLabel} · Billed {M(r.TotalBilled)} · Paid {M(r.TotalPaid)} · "
            + $"Outstanding {M(r.TotalOutstanding)}  (aging: 1–30 {M(r.Aging.D1_30)}, 31–60 {M(r.Aging.D31_60)}, "
            + $"61–90 {M(r.Aging.D61_90)}, 90+ {M(r.Aging.D90Plus)})";
        PreviewGrid.ItemsSource = r.Groups.SelectMany(g => g.Lines.Select(l => new
        {
            Status = g.Name,
            l.Job,
            Client = l.ClientName,
            Invoice = l.InvoiceNumber,
            Date = l.InvoiceDate,
            Sent = l.DateSent,
            l.Stage,
            Age = l.Balance > 0.005 ? $"{l.AgeDays}d ({l.AgeBucket})" : "",
            Billed = l.Billed.ToString("C2"),
            Paid = l.Paid.ToString("C2"),
            Balance = l.Balance.ToString("C2")
        })).ToList();
    }

    private void PreviewJobStatus()
    {
        var r = _reports.JobStatus(CurrentFilter());
        PreviewTitle.Text = "Job Status Summary";
        PreviewSummary.Text =
            $"Billed {M(r.TotalBilled)} · Paid {M(r.TotalPaid)} · Outstanding {M(r.TotalOutstanding)}";
        PreviewGrid.ItemsSource = r.Groups.SelectMany(g => g.Rows.Select(row => new
        {
            Status = g.Status,
            row.Address,
            Client = row.ClientName,
            Job = row.JobNumber,
            Agency = row.ExternalRef1,
            Type = row.ProjectType,
            Stage = row.CurrentStage,
            Pct = $"{row.PercentComplete}%",
            Billed = row.Billed.ToString("C2"),
            Paid = row.Paid.ToString("C2"),
            Outstanding = row.Outstanding.ToString("C2")
        })).ToList();
    }

    private void PreviewMonthly()
    {
        var stmt = CurrentStatement();
        PreviewTitle.Text = $"Monthly Statement — {stmt.MonthLabel}";
        PreviewSummary.Text =
            $"Invoiced {M(stmt.InvoicedTotal)} · Received {M(stmt.ReceivedTotal)} · "
            + $"Outstanding across all jobs today {M(stmt.OutstandingNow)}";
        var rows = stmt.Invoiced.Select(l => new
        {
            Kind = "Invoiced", Date = l.InvoiceDate, Ref = l.InvoiceNumber,
            Job = l.Job, Detail = l.Stage ?? "", Amount = l.Billed.ToString("C2")
        }).Concat(stmt.Received.Select(p => new
        {
            Kind = "Payment", Date = p.PaymentDate, Ref = p.InvoiceNumber,
            Job = p.JobTitle, Detail = p.Method ?? "", Amount = p.Amount.ToString("C2")
        })).OrderBy(x => x.Date).ToList();
        PreviewGrid.ItemsSource = rows;
    }

    private string MonthlyPdf(Pdf.ReportsPdf pdf)
    {
        var stmt = CurrentStatement();
        return pdf.MonthlyStatement(stmt, OutPath($"Statement_{stmt.MonthLabel}", "pdf"));
    }

    private MonthlyStatement CurrentStatement()
    {
        var year = (int)(YearBox.SelectedItem ?? DateTime.Today.Year);
        var month = MonthBox.SelectedIndex + 1;
        return _reports.Monthly(year, month, CurrentFilter());
    }

    // ---------------------------------------------------------------- generate

    private string OutPath(string stem, string ext)
    {
        AppPaths.EnsureCreated();
        var name = AppPaths.SafeFolderName($"{stem}_{DateTime.Now:yyyy-MM-dd}");
        return Path.Combine(AppPaths.OutputFolder, $"{name}.{ext}");
    }

    private static void OpenAfterSave(string path)
    {
        if (MessageBox.Show($"Saved:\n{path}\n\nOpen it now?", "Report",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void Pdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pdf = new Pdf.ReportsPdf();
            string path = (TypeBox.SelectedItem as string) switch
            {
                Receivables => pdf.Receivables(_reports.Receivables(CurrentFilter()), OutPath("Receivables_Status", "pdf")),
                JobStatus => pdf.JobStatus(_reports.JobStatus(CurrentFilter()), OutPath("Job_Status_Summary", "pdf")),
                Monthly => MonthlyPdf(pdf),
                _ => throw new InvalidOperationException()
            };
            OpenAfterSave(path);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Excel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path;
            switch (TypeBox.SelectedItem as string)
            {
                case Receivables:
                    path = OutPath("Receivables_Status", "xlsx");
                    _export.ReceivablesToXlsx(_reports.Receivables(CurrentFilter()), path);
                    break;
                case JobStatus:
                    path = OutPath("Job_Status_Summary", "xlsx");
                    _export.JobStatusToXlsx(_reports.JobStatus(CurrentFilter()), path);
                    break;
                default:
                    MessageBox.Show("Excel export is available for the Receivables and Job Status reports. "
                        + "Use Generate PDF for the monthly statement.", "Reports",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
            }
            OpenAfterSave(path);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void PerStatus_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var filter = CurrentFilter();
            var full = _reports.Receivables(filter);
            if (full.Groups.Count == 0) { MessageBox.Show("No invoices in range.", "Reports"); return; }

            var pdf = new Pdf.ReportsPdf();
            var made = new List<string>();
            foreach (var g in full.Groups)
            {
                var one = _reports.ReceivablesForStatus(filter, g.Name);
                var path = pdf.Receivables(one, OutPath($"Receivables_{g.Name}", "pdf"),
                    $"Receivables — {g.Name}");
                made.Add(Path.GetFileName(path));
            }
            if (MessageBox.Show($"Created {made.Count} files in the Output folder:\n\n"
                                + string.Join("\n", made) + "\n\nOpen the folder?", "Reports",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.OutputFolder) { UseShellExecute = true });
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static void Fail(Exception ex)
    {
        Infra.Log.Error("ReportsView", ex);
        MessageBox.Show("The report could not be produced:\n" + ex.Message, "Reports",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string M(double v) => v.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
}
