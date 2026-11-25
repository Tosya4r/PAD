using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PAD.Data;

namespace PAD.Desktop.ViewModels;

public sealed record StatTile(string Label, string Value, string Accent);

public sealed partial class DashboardViewModel : ObservableObject
{
    public ObservableCollection<StatTile> CountTiles { get; } = new();
    public ObservableCollection<StatTile> FinanceTiles { get; } = new();

    [ObservableProperty] private string? error;

    public async Task LoadAsync()
    {
        try
        {
            using var db = App.CreateDb();
            var s = await DashboardService.LoadAsync(db, DateOnly.FromDateTime(DateTime.Today));

            CountTiles.Clear();
            CountTiles.Add(new("Всего проектов", s.TotalProjects.ToString(), "#6B7280"));
            CountTiles.Add(new("Активные", s.Active.ToString(), "#2F5BEA"));
            CountTiles.Add(new("Завершённые", s.Completed.ToString(), "#1E9E5A"));
            CountTiles.Add(new("На паузе", s.OnHold.ToString(), "#D98A00"));

            FinanceTiles.Clear();
            FinanceTiles.Add(new("Потенциальный доход", Money(s.Potential), "#2F5BEA"));
            FinanceTiles.Add(new("Заработано", Money(s.Earned), "#1E9E5A"));
            FinanceTiles.Add(new("Ожидает оплаты", Money(s.Pending), "#D98A00"));
            FinanceTiles.Add(new("Доход за месяц", Money(s.MonthIncome), "#1E9E5A"));
        }
        catch (Exception ex)
        {
            Error = "Не удалось загрузить данные: " + ex.Message;
        }
    }

    /// <summary>Валюты не смешиваются: каждая показывается отдельно.</summary>
    static string Money(IReadOnlyDictionary<string, decimal> d) =>
        d.Count == 0
            ? "0 EUR"
            : string.Join(" · ", d.OrderBy(kv => kv.Key)
                .Select(kv => kv.Value.ToString("#,##0.##", CultureInfo.CurrentCulture) + " " + kv.Key));
}
