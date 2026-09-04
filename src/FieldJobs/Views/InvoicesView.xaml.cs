using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class InvoicesView : UserControl
{
    private readonly InvoiceService _invoices = new();
    private List<InvoiceRow> _all = new();

    public InvoicesView()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _all = _invoices.All();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var term = SearchBox.Text?.Trim() ?? "";
        var rows = string.IsNullOrEmpty(term)
            ? _all
            : _all.Where(r =>
                Has(r.JobTitle, term) || Has(r.InvoiceNumber, term) ||
                Has(r.StageLabel, term) || Has(r.Status, term)).ToList();

        Grid.ItemsSource = rows;

        var billed = rows.Sum(r => r.Billed);
        var paid = rows.Sum(r => r.Paid);
        string M(double v) => v.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        TotalsText.Text = $"{rows.Count} invoices   ·   Billed {M(billed)}   ·   Paid {M(paid)}   ·   Outstanding {M(billed - paid)}";
    }

    private static bool Has(string? s, string t)
        => !string.IsNullOrEmpty(s) && s.Contains(t, StringComparison.OrdinalIgnoreCase);

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void Grid_Open(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is InvoiceRow r)
        {
            var dlg = new InvoiceEditWindow(r.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) Reload();
        }
    }
}
