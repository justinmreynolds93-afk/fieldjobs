using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using FieldJobs.Data;
using FieldJobs.Infra;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class InvoiceEditWindow : Window
{
    public sealed class LineVm : ObservableObject
    {
        private string _description = "";
        private double _qty = 1;
        private double _rate;

        public string Description { get => _description; set => Set(ref _description, value); }
        public double Qty { get => _qty; set { if (Set(ref _qty, value)) Raise(nameof(Amount)); } }
        public double Rate { get => _rate; set { if (Set(ref _rate, value)) Raise(nameof(Amount)); } }
        public double Amount => Math.Round(Qty * Rate, 2);
    }

    private readonly InvoiceService _invoices = new();
    private readonly JobService _jobs = new();
    private readonly FileService _files = new();
    private readonly SettingsService _settings = new();
    private readonly ObservableCollection<LineVm> _lines = new();

    private readonly long _jobId;
    private long _invoiceId;               // 0 => not yet saved

    public long? SavedInvoiceId { get; private set; }

    // ---- new invoice (optionally seeded from a stage) ----
    public InvoiceEditWindow(long jobId, string? stageKey = null, string? stageLabel = null, double suggestedAmount = 0)
    {
        InitializeComponent();
        _jobId = jobId;
        CommonInit();

        NumberBox.Text = _invoices.NextInvoiceNumber();
        DateBox.SelectedDate = DateTime.Today;
        StatusBox.SelectedItem = "Draft";
        SentToBox.Text = new ContactService().Primary() is { } c ? $"{c.Name} (Agency)" : "";
        if (stageLabel != null) StageBox.Text = stageLabel;

        _lines.Add(new LineVm { Description = stageLabel ?? "", Qty = 1, Rate = suggestedAmount });
        RecalcTotals();
    }

    // ---- edit existing invoice ----
    public InvoiceEditWindow(long invoiceId)
    {
        InitializeComponent();
        var inv = _invoices.Get(invoiceId);
        _jobId = inv.JobId;
        _invoiceId = invoiceId;
        CommonInit();

        HeaderText.Text = $"Invoice {inv.InvoiceNumber}";
        Title = $"Invoice {inv.InvoiceNumber}";
        DeleteButton.Visibility = Visibility.Visible;

        NumberBox.Text = inv.InvoiceNumber;
        DateBox.SelectedDate = DateTime.TryParse(inv.InvoiceDate, out var d) ? d : DateTime.Today;
        StatusBox.SelectedItem = inv.Status;
        SentDateBox.SelectedDate = DateTime.TryParse(inv.DateSent, out var sd) ? sd : null;
        SentToBox.Text = inv.SentTo ?? "";
        StageBox.Text = inv.StageLabel ?? "";
        NotesBox.Text = inv.Notes ?? "";

        foreach (var l in _invoices.Lines(invoiceId))
            _lines.Add(new LineVm { Description = l.Description, Qty = l.Qty, Rate = l.Rate });
        if (_lines.Count == 0) _lines.Add(new LineVm());

        RefreshPayments();
        RefreshDocs();
        RecalcTotals();
        RefreshOverdue();
    }

    private void CommonInit()
    {
        StatusBox.ItemsSource = Vocab.InvoiceStatuses;
        PayMethod.ItemsSource = Vocab.PaymentMethods;
        PayMethod.SelectedIndex = 0;
        LinesGrid.ItemsSource = _lines;
        _lines.CollectionChanged += Lines_CollectionChanged;

        var job = _jobs.Get(_jobId);
        JobText.Text = $"Job: {job.DisplayAddress}"
            + (string.IsNullOrWhiteSpace(job.ClientName) ? "" : $"  ·  {job.ClientName}")
            + (string.IsNullOrWhiteSpace(job.ExternalRef1) ? "" : $"  ·  Agency {job.ExternalRef1}");

        StageBox.ItemsSource = new StageService().ForJob(_jobId).Select(s => s.Name).ToList();
        PayDate.SelectedDate = DateTime.Today;
    }

    private void Lines_CollectionChanged(object? s, NotifyCollectionChangedEventArgs e) => RecalcTotals();

    private void Lines_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        => Dispatcher.BeginInvoke(RecalcTotals, DispatcherPriority.Background);

    private static string M(double v) => v.ToString("C", CultureInfo.GetCultureInfo("en-US"));

    private void RecalcTotals()
    {
        var billed = _lines.Where(l => !string.IsNullOrWhiteSpace(l.Description)).Sum(l => l.Amount);
        var paid = _invoiceId == 0 ? 0 : _invoices.PaidTotal(_invoiceId);
        BilledText.Text = M(billed);
        PaidText.Text = M(paid);
        BalanceText.Text = M(Math.Round(billed - paid, 2));
    }

    private void RefreshOverdue()
    {
        if (_invoiceId == 0) { OverdueText.Visibility = Visibility.Collapsed; return; }
        var inv = _invoices.Get(_invoiceId);
        var days = InvoiceService.DaysOutstanding(inv);
        var owes = inv.Status is not (Vocab.InvoiceVoid or Vocab.InvoicePaid)
                   && _invoices.LineTotal(_invoiceId) - _invoices.PaidTotal(_invoiceId) > 0.005;
        if (owes && days > Vocab.OverdueDays)
        {
            OverdueText.Text = $"Overdue — {days} days since {(inv.DateSent is null ? "the invoice date" : "it was sent")}.";
            OverdueText.Visibility = Visibility.Visible;
        }
        else OverdueText.Visibility = Visibility.Collapsed;
    }

    private void RefreshPayments()
    {
        PaymentsList.Items.Clear();
        if (_invoiceId == 0) return;
        foreach (var p in _invoices.Payments(_invoiceId))
        {
            var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var del = new Button { Content = "Remove", MinWidth = 66 };
            del.Click += (_, _) => { _invoices.DeletePayment(p.Id); ReloadAfterPaymentChange(); };
            DockPanel.SetDock(del, Dock.Right);
            dp.Children.Add(del);
            dp.Children.Add(new TextBlock
            {
                Text = $"{p.PaymentDate}   ·   {M(p.Amount)}"
                       + (string.IsNullOrWhiteSpace(p.Method) ? "" : $"   ·   {p.Method}")
                       + (string.IsNullOrWhiteSpace(p.Reference) ? "" : $"   ·   {p.Reference}")
                       + (string.IsNullOrWhiteSpace(p.Note) ? "" : $"   ·   {p.Note}"),
                VerticalAlignment = VerticalAlignment.Center
            });
            PaymentsList.Items.Add(dp);
        }
    }

    private void RefreshDocs()
    {
        DocsList.Items.Clear();
        var docs = _invoiceId == 0 ? new List<JobFile>() : _files.ForInvoice(_invoiceId);
        foreach (var f in docs)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

            var open = new Button { Content = "Open", MinWidth = 54, Margin = new Thickness(6, 0, 0, 0) };
            open.Click += (_, _) => TryOpen(f.StoredPath);
            DockPanel.SetDock(open, Dock.Right);
            var remove = new Button { Content = "Remove", MinWidth = 66, Margin = new Thickness(6, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                if (MessageBox.Show($"Remove \"{f.OriginalName}\"? (The app's copy is deleted.)",
                        "Remove document", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _files.Remove(f.Id);
                    RefreshDocs();
                }
            };
            DockPanel.SetDock(remove, Dock.Right);
            dp.Children.Add(remove);
            dp.Children.Add(open);
            dp.Children.Add(new TextBlock
            {
                Text = $"[{f.DocTypeLabel}]  {f.OriginalName}   ·   {f.DateAdded[..Math.Min(10, f.DateAdded.Length)]}   ·   {f.SizeDisplay}",
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            });
            DocsList.Items.Add(dp);
        }
        DocsEmpty.Visibility = docs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReloadAfterPaymentChange()
    {
        _invoices.RecalcStatus(_invoiceId);
        StatusBox.SelectedItem = _invoices.Get(_invoiceId).Status;
        RefreshPayments();
        RecalcTotals();
        RefreshOverdue();
    }

    // ---- persistence ----

    private bool PersistHeaderAndLines()
    {
        if (string.IsNullOrWhiteSpace(NumberBox.Text))
        {
            MessageBox.Show("Enter an invoice number.", "Invoice", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var goodLines = _lines.Where(l => !string.IsNullOrWhiteSpace(l.Description)).ToList();
        if (goodLines.Count == 0)
        {
            MessageBox.Show("Add at least one line item with a description.", "Invoice",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var number = NumberBox.Text.Trim();
        var date = DateBox.SelectedDate ?? DateTime.Today;
        var status = StatusBox.SelectedItem as string ?? "Draft";
        var stageLabel = string.IsNullOrWhiteSpace(StageBox.Text) ? null : StageBox.Text.Trim();
        var stageKey = new StageService().ForJob(_jobId).FirstOrDefault(s => s.Name == stageLabel)?.StageKey;
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        if (_invoiceId == 0)
            _invoiceId = _invoices.CreateHeader(_jobId, number, date, stageKey, stageLabel, status, notes);

        _invoices.UpdateHeader(new Invoice
        {
            Id = _invoiceId, JobId = _jobId, InvoiceNumber = number,
            InvoiceDate = date.ToString("yyyy-MM-dd"),
            DateSent = SentDateBox.SelectedDate?.ToString("yyyy-MM-dd"),
            SentTo = string.IsNullOrWhiteSpace(SentToBox.Text) ? null : SentToBox.Text.Trim(),
            StageKey = stageKey, StageLabel = stageLabel, Status = status, Notes = notes
        });

        _invoices.ReplaceLines(_invoiceId,
            goodLines.Select((l, i) => new InvoiceLine { Description = l.Description.Trim(), Qty = l.Qty, Rate = l.Rate, Seq = i }));

        _jobs.Touch(_jobId);
        SavedInvoiceId = _invoiceId;
        return true;
    }

    private bool EnsureSaved() => _invoiceId != 0 || PersistHeaderAndLines();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (PersistHeaderAndLines()) DialogResult = true;
    }

    private void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        if (!PersistHeaderAndLines()) return;
        try
        {
            AppPaths.EnsureCreated();
            var num = NumberBox.Text.Trim().Replace('/', '-');
            var outPath = Path.Combine(AppPaths.OutputFolder, $"Invoice_{AppPaths.SafeFolderName(num)}.pdf");
            new Pdf.InvoicePdf().Build(_invoiceId, outPath);
            Process.Start(new ProcessStartInfo(outPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("InvoicePdf", ex);
            MessageBox.Show("Saved, but the PDF could not be created:\n" + ex.Message,
                "Invoice PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        DialogResult = true;
    }

    private void MarkSent_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSaved()) return;
        var to = string.IsNullOrWhiteSpace(SentToBox.Text) ? null : SentToBox.Text.Trim();
        _invoices.MarkSent(_invoiceId, DateTime.Today, to);
        var inv = _invoices.Get(_invoiceId);
        SentDateBox.SelectedDate = DateTime.TryParse(inv.DateSent, out var sd) ? sd : DateTime.Today;
        StatusBox.SelectedItem = inv.Status;
        RefreshOverdue();
    }

    private void AddPayment_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSaved()) return;

        if (!double.TryParse(PayAmount.Text, NumberStyles.Currency, CultureInfo.CurrentCulture, out var amt) || amt <= 0)
        {
            MessageBox.Show("Enter a payment amount greater than zero.", "Payment",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _invoices.AddPayment(_invoiceId, PayDate.SelectedDate ?? DateTime.Today, Math.Round(amt, 2),
            PayMethod.SelectedItem as string,
            string.IsNullOrWhiteSpace(PayReference.Text) ? null : PayReference.Text.Trim(),
            string.IsNullOrWhiteSpace(PayNote.Text) ? null : PayNote.Text.Trim());
        PayAmount.Clear();
        PayReference.Clear();
        PayNote.Clear();
        ReloadAfterPaymentChange();
    }

    private void MarkPaid_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSaved()) return;
        _invoices.MarkPaidInFull(_invoiceId, DateTime.Today, PayMethod.SelectedItem as string, null, "Paid in full");
        ReloadAfterPaymentChange();
    }

    // ---- documents ----

    private void AttachSent_Click(object sender, RoutedEventArgs e) => AttachDoc(Vocab.DocInvoiceSent, "the invoice you sent");
    private void AttachProof_Click(object sender, RoutedEventArgs e) => AttachDoc(Vocab.DocPaymentProof, "the proof of payment");
    private void AttachOther_Click(object sender, RoutedEventArgs e) => AttachDoc(Vocab.DocFile, "a file for this invoice");

    private void AttachDoc(string docType, string what)
    {
        if (!EnsureSaved()) return;
        var dlg = new OpenFileDialog
        {
            Title = $"Attach {what}",
            Multiselect = true,
            Filter = "Documents & images|*.pdf;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.doc;*.docx;*.xls;*.xlsx;*.txt|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var path in dlg.FileNames)
        {
            try { _files.AttachToInvoice(_jobId, _invoiceId, docType, path); }
            catch (Exception ex)
            {
                Log.Error("AttachToInvoice", ex);
                MessageBox.Show($"Could not attach {Path.GetFileName(path)}:\n{ex.Message}",
                    "Attach", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        RefreshDocs();
    }

    private void TryOpen(string path)
    {
        try { _files.OpenWithDefaultApp(path); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open file", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_invoiceId == 0) { DialogResult = false; return; }
        if (MessageBox.Show("Delete this invoice, its payments, and its attached documents? This cannot be undone.",
                "Delete invoice", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _invoices.Delete(_invoiceId);
        _jobs.Touch(_jobId);
        SavedInvoiceId = null;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
