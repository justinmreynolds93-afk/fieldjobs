using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class DashboardView : UserControl
{
    private readonly DashboardService _svc = new();

    public DashboardView()
    {
        InitializeComponent();
        ReportButton.Content = $"Report for {Vocab.AgencyPartyLabel}";
        WaitAgencyLabel.Text = $"Waiting on {Vocab.AgencyPartyLabel.ToLowerInvariant()}";
        WaitClientLabel.Text = $"Waiting on {Vocab.ClientPartyLabel.ToLowerInvariant()}";
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var d = _svc.Build();
        string M(double v) => v.ToString("C0", CultureInfo.GetCultureInfo("en-US"));

        StatOutstanding.Text = M(d.TotalOutstanding);
        StatBilled.Text = M(d.TotalBilled);
        StatPaid.Text = M(d.TotalPaid);
        MonthLabel.Text = d.MonthLabel + " — billed";
        StatMonth.Text = M(d.MonthBilled);
        StatMonthSub.Text = $"{M(d.MonthPaid)} received";

        StatOpen.Text = d.JobsOpen.ToString();
        StatWaitMe.Text = d.WaitingOnMe.ToString();
        StatWaitAgency.Text = d.WaitingOnAgency.ToString();
        StatWaitHo.Text = d.WaitingOnClient.ToString();

        ReceivablesGrid.ItemsSource = d.Receivables;
        ReceivablesEmpty.Visibility = d.Receivables.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        BuildAttention(d.Attention);
        BuildActivity(d.RecentActivity);

        StageList.Items.Clear();
        foreach (var sc in d.JobsByStage)
            StageList.Items.Add(new Border { Padding = new Thickness(0, 6, 0, 6), Child = Row(sc.Stage, sc.Count) });
        StageEmpty.Visibility = d.JobsByStage.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildAttention(List<AttentionItem> items)
    {
        AttentionList.Items.Clear();
        if (items.Count == 0)
        {
            AttentionCard.Visibility = Visibility.Collapsed;
            return;
        }
        AttentionCard.Visibility = Visibility.Visible;

        foreach (var it in items)
        {
            var dot = new Border
            {
                Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                Background = (Brush)FindResource(it.Severity switch
                {
                    "high" => "BadBrush",
                    "warn" => "WarnBrush",
                    _ => "MutedText"
                })
            };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = it.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(it.Detail))
                text.Children.Add(new TextBlock
                {
                    Text = it.Detail, Foreground = (Brush)FindResource("MutedText"),
                    FontSize = 12, TextWrapping = TextWrapping.Wrap
                });

            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5), Cursor = Cursors.Hand };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(text);
            row.MouseLeftButtonUp += (_, _) => Navigate(it);
            AttentionList.Items.Add(row);
        }
    }

    private void BuildActivity(List<ActivityEntry> entries)
    {
        ActivityList.Items.Clear();
        foreach (var a in entries)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var when = new TextBlock
            {
                Text = ActivityService.Ago(a.Ts), Foreground = (Brush)FindResource("MutedText"),
                FontSize = 11, MinWidth = 64, VerticalAlignment = VerticalAlignment.Top
            };
            DockPanel.SetDock(when, Dock.Right);
            dp.Children.Add(when);
            dp.Children.Add(new TextBlock { Text = a.Summary, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            ActivityList.Items.Add(dp);
        }
        ActivityEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void Navigate(AttentionItem it)
    {
        switch (it.Target)
        {
            case "invoice" when it.JobId is { } jid: MainWindow.Instance?.ShowJob(jid); break;
            case "job" when it.JobId is { } j: MainWindow.Instance?.ShowJob(j); break;
            case "backup": MainWindow.Instance?.ShowSettings(); break;
            case "payments": MainWindow.Instance?.ShowPayments(); break;
        }
    }

    private DockPanel Row(string label, int count)
    {
        var dp = new DockPanel();
        var c = new TextBlock { Text = count.ToString(), FontWeight = FontWeights.Bold, MinWidth = 28 };
        DockPanel.SetDock(c, Dock.Right);
        dp.Children.Add(c);
        dp.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis });
        return dp;
    }

    private void Receivables_Open(object sender, RoutedEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is ReceivableRow r)
            MainWindow.Instance?.ShowJob(r.JobId);
    }

    private void NewJob_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new JobEditWindow { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.SavedJobId is { } id) MainWindow.Instance?.ShowJob(id);
    }

    private void RecordPayment_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RecordPaymentWindow { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) Refresh();
    }

    private void Report_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.ShowReports();
}
