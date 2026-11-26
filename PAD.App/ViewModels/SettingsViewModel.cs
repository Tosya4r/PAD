using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PAD.Data;
using PAD.Desktop.Services;

namespace PAD.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public string DbPath => App.DbPath;
    public string BackupsDir => Path.Combine(Path.GetDirectoryName(App.DbPath)!, "backups");
    public string Version { get; } = "PAD " + (typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0");

    public ObservableCollection<string> AutoBackups { get; } = new();

    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool isError;
    [ObservableProperty] private string dbSize = "";

    [ObservableProperty] private bool isDark = ProfileService.Current.Dark;

    partial void OnIsDarkChanged(bool value)
    {
        ProfileService.Current.Dark = value;
        ProfileService.Save();
        ThemeService.Apply(value);
    }

    public SettingsViewModel() => RefreshInfo();

    void RefreshInfo()
    {
        try
        {
            DbSize = File.Exists(DbPath) ? Size(new FileInfo(DbPath).Length) : "—";
            AutoBackups.Clear();
            if (Directory.Exists(BackupsDir))
                foreach (var f in Directory.GetFiles(BackupsDir, "*.zip").OrderByDescending(Path.GetFileName))
                    AutoBackups.Add(Path.GetFileName(f) + "  ·  " + Size(new FileInfo(f).Length));
        }
        catch { /* информационный блок не должен ронять экран */ }
    }

    async Task Run(Func<Task<string>> action)
    {
        try { IsError = false; Status = await action(); }
        catch (Exception ex) { IsError = true; Status = ex.Message; }
        RefreshInfo();
    }

    [RelayCommand]
    private Task CreateBackupAsync() => Run(async () =>
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Резервная копия PAD (*.zip)|*.zip",
            FileName = $"PAD-backup-{DateTime.Now:yyyy-MM-dd_HH-mm}.zip",
        };
        if (dlg.ShowDialog() != true) return "Создание копии отменено.";
        var path = dlg.FileName;
        await Task.Run(() => BackupService.CreateBackup(App.DbPath, path, App.FilesDir));
        return "Копия создана: " + path + " (" + Size(new FileInfo(path).Length) + ")";
    });

    [RelayCommand]
    private Task RestoreAsync() => Run(async () =>
    {
        var dlg = new OpenFileDialog { Filter = "Резервная копия PAD (*.zip)|*.zip", InitialDirectory = Directory.Exists(BackupsDir) ? BackupsDir : "" };
        if (dlg.ShowDialog() != true) return "Восстановление отменено.";

        var ok = MessageBox.Show(
            "Текущие данные будут заменены данными из выбранной копии.\nПеред этим PAD сам сохранит копию текущих данных в папку backups.\n\nПродолжить?",
            "Восстановление из копии", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return "Восстановление отменено.";

        var path = dlg.FileName;
        var safety = await Task.Run(() => BackupService.Restore(path, App.DbPath, App.FilesDir));
        using (var db = App.CreateDb()) PadDatabase.Initialize(db);
        ProfileService.Load();
        ThemeService.Apply(ProfileService.Current.Dark);
        IsDark = ProfileService.Current.Dark;
        return "Данные восстановлены. Прежние данные сохранены в: " + safety;
    });

    [RelayCommand]
    private Task CheckIntegrityAsync() => Run(async () =>
    {
        var r = await Task.Run(() => BackupService.IntegrityCheck(App.DbPath));
        if (r != "ok") throw new InvalidOperationException("Найдены проблемы в базе: " + r + ". Восстановите данные из резервной копии.");
        return "Проверка целостности пройдена: база данных в порядке.";
    });

    [RelayCommand]
    private Task ExportPaymentsAsync() => Run(async () =>
    {
        var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"PAD-payments-{DateTime.Now:yyyy-MM-dd}.csv" };
        if (dlg.ShowDialog() != true) return "Экспорт отменён.";
        using var db = App.CreateDb();
        var csv = await ExportService.PaymentsCsvAsync(db);
        await File.WriteAllTextAsync(dlg.FileName, csv, new UTF8Encoding(true));
        return "История платежей сохранена: " + dlg.FileName;
    });

    [RelayCommand]
    private void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetDirectoryName(DbPath) + "\"") { UseShellExecute = true }); }
        catch (Exception ex) { IsError = true; Status = ex.Message; }
    }

    static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes + " Б",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#") + " КБ",
        _ => (bytes / 1048576.0).ToString("0.#") + " МБ",
    };
}
