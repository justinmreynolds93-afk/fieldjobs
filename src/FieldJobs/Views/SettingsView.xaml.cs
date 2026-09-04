using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FieldJobs.Data;
using FieldJobs.Infra;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class SettingsView : UserControl
{
    public sealed class FeeVm : ObservableObject
    {
        private string _label = "";
        private double _amount;
        public string Label { get => _label; set => Set(ref _label, value); }
        public double Amount { get => _amount; set => Set(ref _amount, value); }
    }

    public sealed class ContactVm : ObservableObject
    {
        private string _name = "", _role = "", _email = "", _phone = "";
        public string Name { get => _name; set => Set(ref _name, value); }
        public string Role { get => _role; set => Set(ref _role, value); }
        public string Email { get => _email; set => Set(ref _email, value); }
        public string Phone { get => _phone; set => Set(ref _phone, value); }
    }

    private readonly SettingsService _settings = new();
    private readonly StageService _stages = new();
    private readonly ContactService _contacts = new();
    private readonly ObservableCollection<FeeVm> _fees = new();
    private readonly ObservableCollection<ContactVm> _contactRows = new();
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        _loading = true;

        BizName.Text = _settings.BusinessName;
        BizDba.Text = _settings.Dba;
        BizAddress.Text = _settings.Address;
        BizEmail.Text = _settings.Email;
        BizPhone.Text = _settings.Phone;
        InvPrefix.Text = _settings.InvoicePrefix;
        InvTerms.Text = _settings.PaymentTerms;
        BillTo.Text = _settings.BillTo;
        BackupFolder.Text = _settings.BackupFolder;

        ThemeBox.ItemsSource = new[] { "System", "Light", "Dark" };
        ThemeBox.SelectedItem = _settings.Theme;

        _fees.Clear();
        foreach (var f in _settings.FeeSchedule())
            _fees.Add(new FeeVm { Label = f.Label, Amount = f.Amount });
        FeeGrid.ItemsSource = _fees;

        _contactRows.Clear();
        foreach (var c in _contacts.All())
            _contactRows.Add(new ContactVm { Name = c.Name, Role = c.Role ?? "", Email = c.Email ?? "", Phone = c.Phone ?? "" });
        ContactGrid.ItemsSource = _contactRows;

        TemplateStageBox.ItemsSource = _stages.StageTemplates();
        TemplateStageBox.DisplayMemberPath = "Name";
        TemplateStageBox.SelectedIndex = 0;

        DataPath.Text = $"Database: {AppPaths.DatabaseFile}\nAttached files: {AppPaths.FilesRoot}";

        _loading = false;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not string mode) return;
        _settings.Set("theme", mode);
        (Application.Current as App)?.ApplyTheme(mode);
    }

    private void TemplateStage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateStageBox.SelectedItem is not StageTemplate st) return;
        var lines = _stages.ChecklistTemplates(st.StageKey).Select(t => t.Text);
        TemplateText.Text = string.Join(Environment.NewLine, lines);
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateStageBox.SelectedItem is not StageTemplate st) return;
        var lines = TemplateText.Text.Split('\n').Select(l => l.Trim('\r', ' ')).Where(l => l.Length > 0);
        _stages.ReplaceChecklistTemplate(st.StageKey, lines);
        Flash("Checklist template saved.");
    }

    private void BrowseBackup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a backup folder" };
        if (Directory.Exists(BackupFolder.Text)) dlg.InitialDirectory = BackupFolder.Text;
        if (dlg.ShowDialog() == true) BackupFolder.Text = dlg.FolderName;
    }

    private void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveCore();
            var path = new BackupService().CreateBackup(BackupFolder.Text);
            if (MessageBox.Show($"Backup saved:\n{path}\n\nOpen the folder?", "Backup",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Backup failed:\n" + ex.Message, "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Choose a backup zip", Filter = "Backup zip (*.zip)|*.zip" };
        if (Directory.Exists(BackupFolder.Text)) dlg.InitialDirectory = BackupFolder.Text;
        if (dlg.ShowDialog() != true) return;

        if (MessageBox.Show(
                "Restoring replaces the current database and files with the contents of the backup.\n\n"
                + "A safety copy of the current data is made first. Continue?",
                "Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            new BackupService().RestoreFromBackup(dlg.FileName);
            MessageBox.Show("Restore complete. The app will reload.", "Restore",
                MessageBoxButton.OK, MessageBoxImage.Information);
            MainWindow.Instance?.ShowDashboard();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Restore failed:\n" + ex.Message, "Restore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = $"fieldjobs-{DateTime.Now:yyyy-MM-dd}.csv",
            Filter = "CSV (*.csv)|*.csv" };
        if (dlg.ShowDialog() != true) return;
        try { new ExportService().ToCsv(dlg.FileName); OfferOpen(dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = $"fieldjobs-{DateTime.Now:yyyy-MM-dd}.xlsx",
            Filter = "Excel workbook (*.xlsx)|*.xlsx" };
        if (dlg.ShowDialog() != true) return;
        try { new ExportService().ToXlsx(dlg.FileName); OfferOpen(dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static void OfferOpen(string path)
    {
        if (MessageBox.Show($"Saved:\n{path}\n\nOpen it now?", "Export",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.DataRoot) { UseShellExecute = true });

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveCore();
        Flash("Settings saved.");
    }

    private void SaveCore()
    {
        _settings.Set("business_name", BizName.Text.Trim());
        _settings.Set("dba", BizDba.Text.Trim());
        _settings.Set("address", BizAddress.Text.Trim());
        _settings.Set("email", BizEmail.Text.Trim());
        _settings.Set("phone", BizPhone.Text.Trim());
        _settings.Set("invoice_prefix", string.IsNullOrWhiteSpace(InvPrefix.Text) ? "FJ-" : InvPrefix.Text.Trim());
        _settings.Set("payment_terms", InvTerms.Text.Trim());
        _settings.Set("bill_to", BillTo.Text.Trim());

        var folder = string.IsNullOrWhiteSpace(BackupFolder.Text)
            ? AppPaths.DefaultBackupFolder : BackupFolder.Text.Trim();
        Directory.CreateDirectory(folder);
        _settings.Set("backup_folder", folder);

        _settings.ReplaceFeeSchedule(_fees
            .Where(f => !string.IsNullOrWhiteSpace(f.Label))
            .Select((f, i) => new FeeScheduleItem { Label = f.Label.Trim(), Amount = f.Amount, SortOrder = i }));

        _contacts.ReplaceAll(_contactRows
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select((c, i) => new Contact
            {
                Name = c.Name.Trim(),
                Role = string.IsNullOrWhiteSpace(c.Role) ? null : c.Role.Trim(),
                Email = string.IsNullOrWhiteSpace(c.Email) ? null : c.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(c.Phone) ? null : c.Phone.Trim(),
                IsPrimary = i == 0
            }));
    }

    private async void Flash(string message)
    {
        SavedNote.Text = message;
        await Task.Delay(2500);
        SavedNote.Text = "";
    }
}
