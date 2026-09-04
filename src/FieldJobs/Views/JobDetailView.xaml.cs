using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FieldJobs.Data;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class JobDetailView : UserControl
{
    private readonly JobService _jobs = new();
    private readonly StageService _stages = new();
    private readonly InvoiceService _invoices = new();
    private readonly FileService _files = new();

    private readonly long _jobId;
    private Job _job = null!;

    public sealed class FileRow
    {
        public long Id { get; init; }
        public string OriginalName { get; init; } = "";
        public string StageKey { get; init; } = "";   // display: stage name
        public string DateAdded { get; init; } = "";
        public string SizeDisplay { get; init; } = "";
        public string StoredPath { get; init; } = "";
    }

    public JobDetailView(long jobId)
    {
        InitializeComponent();
        _jobId = jobId;
        Loaded += (_, _) => ReloadAll();
    }

    private void ReloadAll()
    {
        _job = _jobs.Get(_jobId);
        var stages = _stages.ForJob(_jobId);
        var totals = _jobs.TotalsFor(_jobId);

        TitleText.Text = _job.Title;
        SubtitleText.Text = string.Join("   ·   ", new[]
        {
            _job.DisplayAddress,
            _job.ClientName,
            _job.ProjectType,
            _job.JobNumber is null ? null : $"Job {_job.JobNumber}",
            _job.ExternalRef1 is null ? null : $"Agency {_job.ExternalRef1}",
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        StatusText.Text = _job.Status;
        var pct = _stages.PercentComplete(stages);
        Progress.Value = pct;
        PercentText.Text = $"{pct}% · {_stages.CurrentStageName(stages)}";

        string M(double v) => v.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        MBilled.Text = M(totals.Billed);
        MPaid.Text = M(totals.Paid);
        MOut.Text = M(totals.Outstanding);

        BuildOverview();
        RoleText.Text = string.IsNullOrWhiteSpace(_job.RoleNotes) ? "—" : _job.RoleNotes;
        NotesText.Text = string.IsNullOrWhiteSpace(_job.Notes) ? "—" : _job.Notes;

        BuildStages(stages);
        ReloadInvoices();
        ReloadFiles(stages);
        ReloadActivity();
    }

    private void ReloadActivity()
    {
        ActivityList.Items.Clear();
        var entries = new ActivityService().ForJob(_jobId);
        foreach (var a in entries)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            var when = new TextBlock
            {
                Text = DateTime.TryParse(a.Ts, out var t) ? t.ToString("MMM d, yyyy h:mm tt") : a.Ts,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedText"),
                FontSize = 11, MinWidth = 150, VerticalAlignment = VerticalAlignment.Top
            };
            DockPanel.SetDock(when, Dock.Right);
            dp.Children.Add(when);
            dp.Children.Add(new TextBlock { Text = a.Summary, TextWrapping = TextWrapping.Wrap });
            ActivityList.Items.Add(dp);
        }
        ActivityEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildOverview()
    {
        OverviewGrid.Children.Clear();
        void Add(string label, string? value)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var l = new TextBlock
            {
                Text = label, Width = 150,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedText")
            };
            dp.Children.Add(l);
            dp.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                TextWrapping = TextWrapping.Wrap
            });
            OverviewGrid.Children.Add(dp);
        }
        Add("Address", _job.DisplayAddress);
        Add("Client", _job.ClientName);
        Add("Project type", _job.ProjectType);
        Add("Job number", _job.JobNumber);
        Add("Ref number", _job.ExternalRef1);
        Add("Program number", _job.ExternalRef2);
        Add("Date assigned", _job.DateAssigned);
        Add("Status", _job.Status);
    }

    private void BuildStages(List<Stage> stages)
    {
        StageList.Items.Clear();
        for (var i = 0; i < stages.Count; i++)
        {
            var unlocked = _stages.IsUnlocked(stages, i);
            var reason = _stages.LockReason(stages, i);
            var card = new StageCard();
            card.Bind(_jobId, stages[i], unlocked, reason, OnStageChanged);
            StageList.Items.Add(card);
        }
    }

    private void OnStageChanged()
    {
        // Re-evaluate gating + progress across all cards when any stage status changes.
        _jobs.Touch(_jobId);
        ReloadAll();
        Tabs.SelectedIndex = 1; // stay on Stages
    }

    private void ReloadInvoices() => InvoicesGrid.ItemsSource = _invoices.ForJob(_jobId);

    private void ReloadFiles(List<Stage>? stages = null)
    {
        stages ??= _stages.ForJob(_jobId);
        var byKey = stages.ToDictionary(s => s.StageKey, s => s.Name);
        FilesGrid.ItemsSource = _files.ForJob(_jobId).Select(f => new FileRow
        {
            Id = f.Id,
            OriginalName = f.OriginalName,
            StageKey = string.IsNullOrEmpty(f.StageKey) ? "General"
                : byKey.TryGetValue(f.StageKey, out var n) ? n : f.StageKey,
            DateAdded = f.DateAdded.Length >= 16 ? f.DateAdded[..16].Replace('T', ' ') : f.DateAdded,
            SizeDisplay = f.SizeDisplay,
            StoredPath = f.StoredPath
        }).ToList();
    }

    // ---- header actions ----

    private void Back_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.ShowJobs();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new JobEditWindow(_jobId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        if (dlg.Deleted) MainWindow.Instance?.ShowJobs();
        else ReloadAll();
    }

    private void SummaryPdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureCreated();
            var name = AppPaths.SafeFolderName($"JobSummary_{_job.Title}");
            var outPath = Path.Combine(AppPaths.OutputFolder, $"{name}.pdf");
            new Pdf.JobSummaryPdf().Build(_jobId, outPath);
            Process.Start(new ProcessStartInfo(outPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Infra.Log.Error("JobSummaryPdf", ex);
            MessageBox.Show("The PDF could not be created:\n" + ex.Message
                + "\n\nDetails: " + Infra.Log.FilePath,
                "Job Summary PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- invoices tab ----

    private void NewInvoice_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InvoiceEditWindow(_jobId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) ReloadAll();
    }

    private void Invoice_Open(object sender, RoutedEventArgs e)
    {
        if (InvoicesGrid.SelectedItem is InvoiceRow r)
        {
            var dlg = new InvoiceEditWindow(r.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) ReloadAll();
        }
    }

    // ---- files tab ----

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Add files to this job",
            Filter = "Documents & images|*.pdf;*.jpg;*.jpeg;*.png;*.gif;*.tif;*.tiff;*.bmp;*.heic;*.doc;*.docx;*.xls;*.xlsx;*.txt;*.csv|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        foreach (var p in dlg.FileNames)
        {
            try { _files.Attach(_jobId, null, p); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not add {Path.GetFileName(p)}:\n{ex.Message}",
                    "Add file", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        ReloadFiles();
    }

    private FileRow? SelectedFile => FilesGrid.SelectedItem as FileRow;

    private void File_Open(object sender, RoutedEventArgs e) => OpenFile(SelectedFile);
    private void File_OpenMenu(object sender, RoutedEventArgs e) => OpenFile(SelectedFile);

    private void OpenFile(FileRow? f)
    {
        if (f is null) return;
        try { _files.OpenWithDefaultApp(f.StoredPath); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open file", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void File_Reveal(object sender, RoutedEventArgs e)
    {
        if (SelectedFile is { } f) _files.RevealInExplorer(f.StoredPath);
    }

    private void File_Remove(object sender, RoutedEventArgs e)
    {
        if (SelectedFile is not { } f) return;
        if (MessageBox.Show($"Remove \"{f.OriginalName}\" from this job?\n(The app's copy is deleted.)",
                "Remove file", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _files.Remove(f.Id);
            ReloadFiles();
        }
    }
}
