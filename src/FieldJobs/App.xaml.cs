using System.Windows;
using System.Windows.Threading;
using FieldJobs.Data;
using FieldJobs.Services;
using FieldJobs.Views;
using Microsoft.Win32;

namespace FieldJobs;

public partial class App : Application
{
    public static SettingsService Settings { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Infra.Log.Error("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                "Something went wrong:\n\n" + args.Exception.Message
                + "\n\nDetails were written to:\n" + Infra.Log.FilePath,
                "FieldJobs", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        AppPaths.EnsureCreated();
        Db.Initialize();

        ApplyTheme(Settings.Theme);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Db.Close();
        base.OnExit(e);
    }

    /// <summary>mode is "System", "Light", or "Dark".</summary>
    public void ApplyTheme(string mode)
    {
        var dark = mode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => SystemUsesDarkTheme()
        };

        var uri = new Uri(
            $"/FieldJobs;component/Theme/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative);
        var newDict = new ResourceDictionary { Source = uri };

        var merged = Resources.MergedDictionaries;
        // Theme dictionary is always slot 0 (Styles.xaml stays after it).
        if (merged.Count > 0) merged[0] = newDict;
        else merged.Insert(0, newDict);
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
