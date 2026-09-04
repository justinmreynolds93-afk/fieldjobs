using System.Windows;
using System.Windows.Controls;
using FieldJobs.Models;
using FieldJobs.Services;

namespace FieldJobs.Views;

public partial class JobsView : UserControl
{
    private readonly JobService _jobs = new();

    public JobsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        var rows = _jobs.Search(SearchBox.Text);
        Grid.ItemsSource = rows;
        CountText.Text = rows.Count == 1 ? "1 job" : $"{rows.Count} jobs";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Reload();

    private void Grid_Open(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is JobListRow r)
            MainWindow.Instance?.ShowJob(r.Id);
    }

    private void NewJob_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new JobEditWindow { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && dlg.SavedJobId is { } id)
            MainWindow.Instance?.ShowJob(id);
    }
}
