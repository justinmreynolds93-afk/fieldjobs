using System.Windows;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class JobEditWindow : Window
{
    private readonly JobService _jobs = new();
    private readonly Job _job;
    private readonly bool _isNew;

    public long? SavedJobId { get; private set; }

    /// <summary>Set when the user deleted the job from this dialog.</summary>
    public bool Deleted { get; private set; }

    public JobEditWindow(long? jobId = null)
    {
        InitializeComponent();

        Ref1LabelText.Text = Vocab.Ref1Label;
        Ref2LabelText.Text = Vocab.Ref2Label;
        ClientLabelText.Text = $"{Vocab.ClientFieldLabel} name";

        ProjectType.ItemsSource = Vocab.ProjectTypes;
        Status.ItemsSource = Vocab.JobStatuses;

        _isNew = jobId is null;
        _job = _isNew ? new Job() : _jobs.Get(jobId!.Value);

        if (_isNew)
        {
            HeaderText.Text = "New job";
            ProjectType.SelectedItem = Vocab.ProjectTypes.FirstOrDefault();
            Status.SelectedItem = "Not started";
            DateAssigned.SelectedDate = DateTime.Today;
        }
        else
        {
            HeaderText.Text = "Edit job";
            Title = "Edit job";
            DeleteButton.Visibility = Visibility.Visible;
            JobNumber.Text = _job.JobNumber ?? "";
            ExternalRef1.Text = _job.ExternalRef1 ?? "";
            ExternalRef2.Text = _job.ExternalRef2 ?? "";
            Street.Text = _job.AddressStreet ?? "";
            City.Text = _job.AddressCity ?? "";
            Zip.Text = _job.AddressZip ?? "";
            Client.Text = _job.ClientName ?? "";
            ProjectType.SelectedItem = _job.ProjectType;
            Status.SelectedItem = _job.Status;
            RoleNotes.Text = _job.RoleNotes ?? "";
            Notes.Text = _job.Notes ?? "";
            if (DateTime.TryParse(_job.DateAssigned, out var d)) DateAssigned.SelectedDate = d;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Street.Text) && string.IsNullOrWhiteSpace(Client.Text))
        {
            MessageBox.Show($"Enter at least a street address or a {Vocab.ClientFieldLabel.ToLowerInvariant()} name.",
                "Job", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _job.JobNumber = Trim(JobNumber.Text);
        _job.ExternalRef1 = Trim(ExternalRef1.Text);
        _job.ExternalRef2 = Trim(ExternalRef2.Text);
        _job.AddressStreet = Trim(Street.Text);
        _job.AddressCity = Trim(City.Text);
        _job.AddressZip = Trim(Zip.Text);
        _job.ClientName = Trim(Client.Text);
        _job.ProjectType = ProjectType.SelectedItem as string ?? Vocab.ProjectTypes.FirstOrDefault() ?? "Standard";
        _job.Status = Status.SelectedItem as string ?? "Not started";
        _job.RoleNotes = Trim(RoleNotes.Text);
        _job.Notes = Trim(Notes.Text);
        _job.DateAssigned = DateAssigned.SelectedDate?.ToString("yyyy-MM-dd");

        if (_isNew)
            SavedJobId = _jobs.Create(_job);
        else
        {
            _jobs.Update(_job);
            SavedJobId = _job.Id;
        }

        DialogResult = true;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_isNew) { DialogResult = false; return; }
        var msg = $"Delete “{_job.Title}” and everything attached to it "
                  + "(stages, checklists, invoices, payments, and the app's copies of its files)?\n\n"
                  + "This cannot be undone.";
        if (MessageBox.Show(msg, "Delete job", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;

        _jobs.Delete(_job.Id);
        Deleted = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
