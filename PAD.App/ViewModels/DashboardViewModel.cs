using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAD.Data;
using PAD.Desktop.Controls;

namespace PAD.Desktop.ViewModels;

public sealed record MoneyLine(string Amount, string Unit);

public sealed record StatTile(string Label, IReadOnlyList<MoneyLine> Lines, string Glyph, string Accent, string Tint);

public sealed record PeriodOption(string Title, ChartPeriod Period);

public sealed record LegendItem(int Id, string Name, string Color, string Amount, string Percent);

public sealed partial class DashboardViewModel : ObservableObject
{
    public ObservableCollection<StatTile> CountTiles { get; } = new();
    public ObservableCollection<StatTile> FinanceTiles { get; } = new();
    public ObservableCollection<LegendItem> Legend { get; } = new();

    public IReadOnlyList<PeriodOption> Periods { get; } = new[]
    {
        new PeriodOption("Месяц", ChartPeriod.Month),
        new PeriodOption("6 мес.", ChartPeriod.Last6),
        new PeriodOption("12 мес.", ChartPeriod.Last12),
        new PeriodOption("Всё время", ChartPeriod.All),
    };

    public string DateLine { get; } = Capitalize(DateTime.Today.ToString("dddd, d MMMM", CultureInfo.CurrentCulture));

    [ObservableProperty] private string? error;
    [ObservableProperty] private PeriodOption? selectedPeriod;
    [ObservableProperty] private IReadOnlyList<ChartSlice> slices = Array.Empty<ChartSlice>();
    [ObservableProperty] private int? selectedCategoryId;
    [ObservableProperty] private string centerTitle = "Заработано";
    [ObservableProperty] private string centerValue = "0 EUR";
    [ObservableProperty] private string centerSub = "";
    [ObservableProperty] private string newCategoryName = "";

    IReadOnlyList<CategoryShare> _shares = Array.Empty<CategoryShare>();
    string _currency = "EUR";

    public DashboardViewModel()
    {
        selectedPeriod = Periods[3]; // поле, без запуска загрузки: её вызывает главное окно
    }

    partial void OnSelectedPeriodChanged(PeriodOption? value)
    {
        if (value is not null) _ = LoadAsync();
    }

    partial void OnSelectedCategoryIdChanged(int? value) => UpdateCenter();

    public async Task LoadAsync()
    {
        try
        {
            Error = null;
            using var db = App.CreateDb();
            var s = await DashboardService.LoadAsync(db, DateOnly.FromDateTime(DateTime.Today),
                SelectedPeriod?.Period ?? ChartPeriod.All);

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

            var keep = SelectedCategoryId;
            _shares = s.Categories;
            _currency = s.ChartCurrency;

            Legend.Clear();
            foreach (var c in _shares)
                Legend.Add(new LegendItem(c.Id, c.Name, c.Color, Fmt(c.Amount) + " " + _currency, Pct(c.Percent)));

            var total = _shares.Sum(c => c.Amount);
            Slices = total > 0
                ? _shares.Where(c => c.Amount > 0)
                         .Select(c => new ChartSlice(c.Id, c.Color, (double)(c.Amount / total))).ToList()
                : Array.Empty<ChartSlice>();

            SelectedCategoryId = keep.HasValue && _shares.Any(c => c.Id == keep) ? keep : null;
            UpdateCenter();
        }
        catch (Exception ex)
        {
            Error = "Не удалось загрузить данные: " + ex.Message;
        }
    }

    void UpdateCenter()
    {
        var total = _shares.Sum(c => c.Amount);
        var sel = SelectedCategoryId.HasValue ? _shares.FirstOrDefault(c => c.Id == SelectedCategoryId) : null;
        if (sel is not null)
        {
            CenterTitle = sel.Name;
            CenterValue = Fmt(sel.Amount) + " " + _currency;
            CenterSub = Pct(sel.Percent) + " от заработка";
        }
        else
        {
            CenterTitle = "Заработано";
            CenterValue = Fmt(total) + " " + _currency;
            CenterSub = SelectedPeriod is null ? "" : "за период: " + SelectedPeriod.Title.ToLowerInvariant();
        }
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        try
        {
            using var db = App.CreateDb();
            await CategoryService.AddAsync(db, NewCategoryName);
            NewCategoryName = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    static StatTile Count(string label, int n, string accent) =>
        new(label, new[] { new MoneyLine(n.ToString(), "") }, "", accent, "#FFFFFF");

    /// <summary>Валюты не смешиваются: каждая показывается отдельной строкой.</summary>
    static IReadOnlyList<MoneyLine> Lines(IReadOnlyDictionary<string, decimal> d) =>
        d.Count == 0
            ? new[] { new MoneyLine("0", "EUR") }
            : d.OrderBy(kv => kv.Key).Select(kv => new MoneyLine(Fmt(kv.Value), kv.Key)).ToList();

    static string Fmt(decimal v) => v.ToString("#,##0.##", CultureInfo.CurrentCulture);

    static string Pct(decimal p) => p.ToString("0.#", CultureInfo.CurrentCulture) + " %";

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];
}
