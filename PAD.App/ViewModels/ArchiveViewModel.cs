using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAD.Data;

namespace PAD.Desktop.ViewModels;

public sealed record ArchiveLine(int Id, string Name, string Sub, string Status, string StatusColor, string Money);

public sealed partial class ArchiveViewModel : ObservableObject
{
    public ObservableCollection<ArchiveLine> Items { get; } = new();

    [ObservableProperty] private string? error;
    [ObservableProperty] private bool isEmpty = true;

    public ArchiveViewModel() => _ = LoadAsync();

    async Task LoadAsync()
    {
        try
        {
            using var db = App.CreateDb();
            var rows = await ProjectService.ListAsync(db, null, archived: true);
            Items.Clear();
            foreach (var r in rows)
                Items.Add(new ArchiveLine(r.Id, r.Name,
                    (string.IsNullOrWhiteSpace(r.Client) ? "Без заказчика" : r.Client) + " · " + r.CategoryName,
                    Ru.Status(r.Status), Ru.StatusColor(r.Status),
                    "получено " + UiFormat.Fmt(r.Received) + " из " + UiFormat.Fmt(r.Price) + " " + r.Currency));
            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task RestoreAsync(ArchiveLine? l)
    {
        if (l is null) return;
        try
        {
            using var db = App.CreateDb();
            await ProjectService.SetArchivedAsync(db, l.Id, false);
            await LoadAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteAsync(ArchiveLine? l)
    {
        if (l is null) return;
        var ok = MessageBox.Show(
            "Проект «" + l.Name + "» будет удалён навсегда вместе со всеми платежами и сдачами. Заработок по нему пропадёт из статистики.\n\nУдалить?",
            "Удаление проекта", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;
        try
        {
            using var db = App.CreateDb();
            await ProjectService.DeleteAsync(db, l.Id);
            await LoadAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}
