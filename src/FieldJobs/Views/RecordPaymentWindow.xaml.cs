using System.Globalization;
using System.Windows;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class RecordPaymentWindow : Window
{
    private readonly InvoiceService _invoices = new();
    private List<InvoiceRow> _open = new();

    /// <param name="invoiceId">Preselect this invoice (from an invoice screen), else null.</param>
    public RecordPaymentWindow(long? invoiceId = null)
    {
        InitializeComponent();

        MethodBox.ItemsSource = Vocab.PaymentMethods;
        MethodBox.SelectedIndex = 0;
        DateBox.SelectedDate = DateTime.Today;

        _open = _invoices.OpenInvoices();
        if (_open.Count == 0) // still allow recording against any non-void invoice
            _open = _invoices.All().Where(r => r.Status != Vocab.InvoiceVoid).ToList();

        var choices = _open.Select(r => new InvoiceChoice(r)).ToList();
        InvoiceBox.ItemsSource = choices;
        InvoiceBox.SelectedItem = choices.FirstOrDefault(c => invoiceId != null && c.Row.Id == invoiceId)
                                  ?? choices.FirstOrDefault();
    }

    private sealed record InvoiceChoice(InvoiceRow Row)
    {
        public override string ToString() =>
            $"{Row.InvoiceNumber} — {Row.JobTitle}  ·  balance {Row.Balance.ToString("C", CultureInfo.GetCultureInfo("en-US"))}";
    }

    private InvoiceRow? Selected => (InvoiceBox.SelectedItem as InvoiceChoice)?.Row;

    private void Invoice_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Selected is not { } r) { BalanceText.Text = ""; return; }
        BalanceText.Text = $"{r.JobTitle} · {r.Status} · billed {Money(r.Billed)}, paid {Money(r.Paid)}, balance {Money(r.Balance)}";
        if (FullBalanceBox.IsChecked == true) AmountBox.Text = r.Balance.ToString("0.00");
    }

    private void FullBalance_Toggled(object sender, RoutedEventArgs e)
    {
        AmountBox.IsEnabled = FullBalanceBox.IsChecked != true;
        if (FullBalanceBox.IsChecked == true && Selected is { } r)
            AmountBox.Text = r.Balance.ToString("0.00");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } r)
        {
            MessageBox.Show("Choose an invoice.", "Record payment", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        double amount;
        if (FullBalanceBox.IsChecked == true)
            amount = r.Balance;
        else if (!double.TryParse(AmountBox.Text, NumberStyles.Currency, CultureInfo.CurrentCulture, out amount) || amount <= 0)
        {
            MessageBox.Show("Enter a payment amount greater than zero.", "Record payment",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var date = DateBox.SelectedDate ?? DateTime.Today;
        var method = MethodBox.SelectedItem as string;
        var reference = string.IsNullOrWhiteSpace(ReferenceBox.Text) ? null : ReferenceBox.Text.Trim();
        var note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

        if (FullBalanceBox.IsChecked == true)
            _invoices.MarkPaidInFull(r.Id, date, method, reference, note);
        else
            _invoices.AddPayment(r.Id, date, Math.Round(amount, 2), method, reference, note);

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string Money(double v) => v.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
