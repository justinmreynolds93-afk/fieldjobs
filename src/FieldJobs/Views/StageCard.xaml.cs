using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class StageCard : UserControl
{
    private readonly StageService _stages = new();
    private readonly FileService _files = new();
    private readonly InvoiceService _invoices = new();
    private readonly SettingsService _settings = new();

    private Stage _stage = null!;
    private long _jobId;
    private bool _unlocked;
    private bool _loading;
    private Action _onChanged = () => { };

    public StageCard() => InitializeComponent();

    public void Bind(long jobId, Stage stage, bool unlocked, string lockReason, Action onChanged)
    {
        _loading = true;
        _jobId = jobId;
        _stage = stage;
        _unlocked = unlocked;
        _onChanged = onChanged;

        TitleText.Text = $"{stage.Seq}. {stage.Name}";
        OptionalTag.Visibility = stage.IsOptional ? Visibility.Visible : Visibility.Collapsed;

        StatusBox.ItemsSource = stage.IsOptional
            ? Vocab.StageStatuses
            : Vocab.StageStatuses.Where(s => s != Vocab.StageNa).ToArray();
        StatusBox.SelectedItem = stage.Status;

        DoneDate.SelectedDate = DateTime.TryParse(stage.DateCompleted, out var d) ? d : null;
        NotesBox.Text = stage.Notes ?? "";

        LockGlyph.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;
        LockText.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;
        LockText.Text = lockReason;
        StatusBox.IsEnabled = unlocked;
        DoneDate.IsEnabled = unlocked;
        Body.IsEnabled = unlocked;
        Body.Opacity = unlocked ? 1.0 : 0.5;

        LoadChecklist();
        LoadFiles();
        UpdateWarnings();

        _loading = false;
    }

    private void LoadChecklist()
    {
        ChecklistItems.Items.Clear();
        foreach (var item in _stages.Checklist(_stage.Id))
        {
            var cb = new CheckBox
            {
                Content = item.Text,
                IsChecked = item.IsChecked,
                Tag = item.Id,
                Margin = new Thickness(0, 2, 0, 2)
            };
            cb.Checked += ChecklistToggled;
            cb.Unchecked += ChecklistToggled;

            var menu = new ContextMenu();
            var rename = new MenuItem { Header = "Rename item" };
            rename.Click += (_, _) =>
            {
                var v = PromptDialog.Ask(Window.GetWindow(this), "Rename checklist item:", item.Text);
                if (v != null) { _stages.RenameChecklistItem(item.Id, v); LoadChecklist(); }
            };
            var del = new MenuItem { Header = "Delete item" };
            del.Click += (_, _) => { _stages.DeleteChecklistItem(item.Id); LoadChecklist(); };
            menu.Items.Add(rename);
            menu.Items.Add(del);
            cb.ContextMenu = menu;

            ChecklistItems.Items.Add(cb);
        }
    }

    private void LoadFiles()
    {
        StageFiles.Items.Clear();
        foreach (var f in _files.ForStage(_stage.Id))
        {
            var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

            var open = new Button { Content = "Open", MinWidth = 54, Margin = new Thickness(6, 0, 0, 0) };
            open.Click += (_, _) => TryOpen(f.StoredPath);
            DockPanel.SetDock(open, Dock.Right);

            var remove = new Button { Content = "Remove", MinWidth = 66, Margin = new Thickness(6, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                if (MessageBox.Show($"Remove \"{f.OriginalName}\" from this job?\n(The app's copy is deleted.)",
                        "Remove file", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _files.Remove(f.Id);
                    LoadFiles();
                    UpdateWarnings();
                }
            };
            DockPanel.SetDock(remove, Dock.Right);

            dp.Children.Add(remove);
            dp.Children.Add(open);
            dp.Children.Add(new TextBlock
            {
                Text = $"{f.OriginalName}   ·   {f.DateAdded[..Math.Min(10, f.DateAdded.Length)]}   ·   {f.SizeDisplay}",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            StageFiles.Items.Add(dp);
        }
    }

    private void UpdateWarnings()
    {
        FileWarn.Visibility = _stage.Status == Vocab.StageComplete && !_stages.HasFiles(_stage.Id)
            ? Visibility.Visible : Visibility.Collapsed;

        var invoiced = _invoices.ForJob(_jobId)
            .FirstOrDefault(i => i.StageLabel == _stage.Name && i.Status != Vocab.InvoiceVoid);
        if (invoiced != null)
        {
            InvoicedHint.Visibility = Visibility.Visible;
            InvoicedHint.Text = $"Already invoiced: {invoiced.InvoiceNumber} "
                + $"({invoiced.Billed:C0} billed, {invoiced.Paid:C0} paid)";
        }
        else InvoicedHint.Visibility = Visibility.Collapsed;
    }

    // ---- event handlers ----

    private void StatusBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StatusBox.SelectedItem is not string status) return;
        _stages.SetStatus(_stage.Id, status);
        _stage.Status = status;
        var updated = _stages.Get(_stage.Id);
        DoneDate.SelectedDate = DateTime.TryParse(updated.DateCompleted, out var d) ? d : null;
        UpdateWarnings();
        _onChanged();
    }

    private void DoneDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _stages.SetDateCompleted(_stage.Id, DoneDate.SelectedDate);
    }

    private void ChecklistToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: long id } cb) return;
        _stages.SetChecklistItem(id, cb.IsChecked == true);
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var text = NewItemBox.Text.Trim();
        if (text.Length == 0) return;
        _stages.AddChecklistItem(_stage.Id, text);
        NewItemBox.Clear();
        LoadChecklist();
    }

    private void NotesBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _stages.SetNotes(_stage.Id, string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim());
        _stage.Notes = NotesBox.Text;
    }

    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Attach file to “{_stage.Name}”",
            Multiselect = true,
            Filter = "Documents & images|*.pdf;*.jpg;*.jpeg;*.png;*.gif;*.tif;*.tiff;*.bmp;*.heic;"
                     + "*.doc;*.docx;*.xls;*.xlsx;*.txt;*.csv|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
        {
            try { _files.Attach(_jobId, _stage.Id, path); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not attach {System.IO.Path.GetFileName(path)}:\n{ex.Message}",
                    "Attach file", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        LoadFiles();
        UpdateWarnings();
        _onChanged();
    }

    private void Invoice_Click(object sender, RoutedEventArgs e)
    {
        var suggested = _settings.FeeForStage(_stage.StageKey);
        var dlg = new InvoiceEditWindow(_jobId, _stage.StageKey, _stage.Name, suggested)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() == true)
        {
            UpdateWarnings();
            _onChanged();
        }
    }

    private void TryOpen(string path)
    {
        try { _files.OpenWithDefaultApp(path); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open file", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
