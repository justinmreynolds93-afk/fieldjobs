using System.Windows;

namespace FieldJobs.Views;

public partial class PromptDialog : Window
{
    public string Value => Input.Text.Trim();

    private PromptDialog(string message, string initial)
    {
        InitializeComponent();
        MessageText.Text = message;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    /// <summary>Returns the entered text, or null if cancelled / empty.</summary>
    public static string? Ask(Window? owner, string message, string initial = "")
    {
        var dlg = new PromptDialog(message, initial);
        if (owner != null) dlg.Owner = owner;
        return dlg.ShowDialog() == true && dlg.Value.Length > 0 ? dlg.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
