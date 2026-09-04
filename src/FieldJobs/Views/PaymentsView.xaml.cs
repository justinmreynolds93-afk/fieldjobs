using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class PaymentsView : UserControl
{
    private readonly InvoiceService _invoices = new();

    private sealed record Period(string Label, int? Year, int? Month)
    {
        public override string ToString() => Label;
    }

    private List<PaymentLedgerRow> _all = new();

    public PaymentsView()
    {
        InitializeComponent();
        BuildPeriods();
        Loaded += (_, _) => Reload();
    }

    private void BuildPeriods()
    {
        var periods = new List<Period> { new("All time", null, null) };
        var today = DateTime.Today;
        for (var i = 0; i < 12; i++)
        {
            var d = today.AddMonths(-i);
            periods.Add(new Period(d.ToString("MMMM yyyy"), d.Year, d.Month));
        }
        for (var y = today.Year; y >= today.Year - 2; y--)
            periods.Add(new Period($"All of {y}", y, null));

        PeriodBox.ItemsSource = periods;
        PeriodBox.SelectedIndex = 0;
    }

    private void Reload()
    {
        var p = PeriodBox.SelectedItem as Period ?? new Period("All time", null, null);
        _all = _invoices.PaymentLedger(p.Year, p.Month);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var term = SearchBox.Text?.Trim() ?? "";
        var rows = string.IsNullOrEmpty(term)
            ? _all
            : _all.Where(r =>
                Has(r.JobTitle, term) || Has(r.InvoiceNumber, term) ||
                Has(r.Method, term) || Has(r.Reference, term) || Has(r.Note, term)).ToList();

        Grid.ItemsSource = rows;
        var total = _invoices.PaymentLedgerTotal(rows);
        TotalsText.Text = $"{rows.Count} payments   ·   {total.ToString("C", CultureInfo.GetCultureInfo("en-US"))} received";
    }

    private static bool Has(string? s, string t)
        => !string.IsNullOrEmpty(s) && s.Contains(t, StringComparison.OrdinalIgnoreCase);

    private void Period_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Reload(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void Grid_Open(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is PaymentLedgerRow r)
        {
            var dlg = new InvoiceEditWindow(r.InvoiceId) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) Reload();
        }
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RecordPaymentWindow { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) Reload();
    }
}
