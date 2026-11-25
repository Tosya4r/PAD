using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PAD.Data;

namespace PAD.Desktop.ViewModels;

public sealed record MoneyLine(string Amount, string Unit);

public sealed record StatTile(string Label, IReadOnlyList<MoneyLine> Lines, string Glyph, string Accent, string Tint);

public sealed partial class DashboardViewModel : ObservableObject
{
    public ObservableCollection<StatTile> CountTiles { get; } = new();
    public ObservableCollection<StatTile> FinanceTiles { get; } = new();

    public string DateLine { get; } = Capitalize(DateTime.Today.ToString("dddd, d MMMM", CultureInfo.CurrentCulture));

    [ObservableProperty] private string? error;

    public async Task LoadAsync()
    {
        try
        {
            using var db = App.CreateDb();
            var s = await DashboardService.LoadAsync(db, DateOnly.FromDateTime(DateTime.Today));

            CountTiles.Clear();
            CountTiles.Add(Count("Всего проектов", s.TotalProjects, "#6B7385"));
            CountTiles.Add(Count("Активные", s.Active, "#2F5BEA"));
            CountTiles.Add(Count("Завершённые", s.Completed, "#1E9E5A"));
            CountTiles.Add(Count("На паузе", s.OnHold, "#D98A00"));

            FinanceTiles.Clear();
            FinanceTiles.Add(new("Потенциальный доход", Lines(s.Potential), "", "#2F5BEA", "#E6EDFF"));
            FinanceTiles.Add(new("Заработано", Lines(s.Earned), "", "#1E9E5A", "#E3F5EA"));
            FinanceTiles.Add(new("Ожидает оплаты", Lines(s.Pending), "", "#D98A00", "#FCF0D9"));
            FinanceTiles.Add(new("Доход за месяц", Lines(s.MonthIncome), "", "#7A4DE0", "#EEE8FC"));
        }
        catch (Exception ex)
        {
            Error = "Не удалось загрузить данные: " + ex.Message;
        }
    }

    static StatTile Count(string label, int n, string accent) =>
        new(label, new[] { new MoneyLine(n.ToString(), "") }, "", accent, "#FFFFFF");

    /// <summary>Валюты не смешиваются: каждая показывается отдельной строкой.</summary>
    static IReadOnlyList<MoneyLine> Lines(IReadOnlyDictionary<string, decimal> d) =>
        d.Count == 0
            ? new[] { new MoneyLine("0", "EUR") }
            : d.OrderBy(kv => kv.Key)
               .Select(kv => new MoneyLine(kv.Value.ToString("#,##0.##", CultureInfo.CurrentCulture), kv.Key))
               .ToList();

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];
}
