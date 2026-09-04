using System.Windows;
using System.Windows.Controls;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Views;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        Title = Vocab.BrandAppName;

        // Org name (if set) is the big line with the app name underneath; otherwise
        // the app name alone carries the branding.
        if (!string.IsNullOrWhiteSpace(Vocab.BrandOrg))
        {
            BrandOrgText.Text = Vocab.BrandOrg;
            BrandAppText.Text = Vocab.BrandAppName;
        }
        else
        {
            BrandOrgText.Text = Vocab.BrandAppName;
            BrandAppText.Text = "Job Tracker";
        }

        DataPathText.Text = "Data folder:\n" + AppPaths.DataRoot;
        ShowDashboard();
    }

    // ---- navigation entry points used across the app ----

    public void ShowDashboard()  { ClearNav(); NavDashboard.IsChecked = true; Host.Content = new DashboardView(); }
    public void ShowJobs()       { ClearNav(); NavJobs.IsChecked = true;      Host.Content = new JobsView(); }
    public void ShowInvoices()   { ClearNav(); NavInvoices.IsChecked = true;  Host.Content = new InvoicesView(); }
    public void ShowPayments()   { ClearNav(); NavPayments.IsChecked = true;  Host.Content = new PaymentsView(); }
    public void ShowReports()    { ClearNav(); NavReports.IsChecked = true;   Host.Content = new ReportsView(); }
    public void ShowSettings()   { ClearNav(); NavSettings.IsChecked = true;  Host.Content = new SettingsView(); }

    /// <summary>Open a job. Clears the nav selection since job detail is not a top-level page.</summary>
    public void ShowJob(long jobId)
    {
        ClearNav();
        Host.Content = new JobDetailView(jobId);
    }

    private void ClearNav()
    {
        foreach (var rb in new[] { NavDashboard, NavJobs, NavInvoices, NavPayments, NavReports, NavSettings })
            rb.IsChecked = false;
    }

    // ---- radio button handlers ----
    private void NavDashboard_Checked(object s, RoutedEventArgs e) { if (IsLoaded) Host.Content = new DashboardView(); }
    private void NavJobs_Checked(object s, RoutedEventArgs e) => Host.Content = new JobsView();
    private void NavInvoices_Checked(object s, RoutedEventArgs e) => Host.Content = new InvoicesView();
    private void NavPayments_Checked(object s, RoutedEventArgs e) => Host.Content = new PaymentsView();
    private void NavReports_Checked(object s, RoutedEventArgs e) => Host.Content = new ReportsView();
    private void NavSettings_Checked(object s, RoutedEventArgs e) => Host.Content = new SettingsView();
}
