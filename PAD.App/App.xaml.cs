using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using PAD.Data;

namespace PAD.Desktop;

public partial class App : Application
{
    static DbContextOptions<PadDbContext>? _options;

    public static string DbPath { get; private set; } = "";

    public static PadDbContext CreateDb() => new(_options!);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) => { LogAndShow(a.Exception); a.Handled = true; };

        try
        {
            DbPath = PadDatabase.DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            _options = PadDatabase.Options(DbPath);
            using var db = CreateDb();
            PadDatabase.Initialize(db);
        }
        catch (Exception ex)
        {
            LogAndShow(ex);
            Shutdown(1);
            return;
        }

        new MainWindow().Show();
    }

    static void LogAndShow(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PAD");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTime.Now:s} {ex}\n\n");
        }
        catch { /* лог не должен ронять приложение */ }
        MessageBox.Show(ex.Message, "PAD — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
