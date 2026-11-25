using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAD.Data;
using PAD.Desktop.Controls;
using PAD.Domain;

namespace PAD.Desktop.ViewModels;

public sealed record MoneyLine(string Amount, string Unit);

public sealed record StatTile(string Label, IReadOnlyList<MoneyLine> Lines, string Glyph, string Accent, string Tint);

public sealed record PeriodOption(string Title, ChartPeriod Period);

public sealed record LegendItem(int Id, string Name, string Color, string Amount, string Percent);

public sealed record EarningItem(int ProjectId, string Project, string Sub, string CategoryColor,
    string DateText, string TypeText, string AmountText);

/// <summary>Переход между разделами из любого экрана.</summary>
public static class Navigator
{
    public static Action<int>? OpenProject;
}

public sealed partial class DashboardViewModel : ObservableObject
{
    public ObservableCollection<StatTile> CountTiles { get; } = new();
    public ObservableCollection<StatTile> FinanceTiles { get; } = new();
    public ObservableCollection<LegendItem> Legend { get; } = new();
    public ObservableCollection<EarningItem> Earnings { get; } = new();

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
    [ObservableProperty] private IReadOnlyList<BarItem> bars = Array.Empty<BarItem>();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFilters))] private int? selectedCategoryId;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFilters))] private int? selectedMonthKey;
    [ObservableProperty] private string centerTitle = "Заработано";
    [ObservableProperty] private string centerValue = "0 EUR";
    [ObservableProperty] private string centerSub = "";
    [ObservableProperty] private string newCategoryName = "";
    [ObservableProperty] private string earningsTitle = "Поступления";
    [ObservableProperty] private bool noEarnings = true;

    public bool HasFilters => SelectedCategoryId.HasValue || SelectedMonthKey.HasValue;

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

    partial void OnSelectedCategoryIdChanged(int? value)
    {
        UpdateCenter();
        _ = RefreshEarningsAsync();
    }

    partial void OnSelectedMonthKeyChanged(int? value) => _ = RefreshEarningsAsync();

    public async Task LoadAsync()
    {
        try
        {
            Error = null;
            var today = DateOnly.FromDateTime(DateTime.Today);
            var period = SelectedPeriod?.Period ?? ChartPeriod.All;
            using var db = App.CreateDb();
            var s = await DashboardService.LoadAsync(db, today, period);

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

            var keepCategory = SelectedCategoryId;
            var keepMonth = SelectedMonthKey;
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

            var months = await FinanceService.MonthlyAsync(db, _currency, today, period);
            Bars = months.Select(m =>
            {
                var d = new DateTime(m.Year, m.Month, 1);
                return new BarItem(m.Year * 100 + m.Month, d.ToString("MMM", CultureInfo.CurrentCulture),
                    (double)Math.Max(m.Amount, 0m),
                    d.ToString("MMMM yyyy", CultureInfo.CurrentCulture) + ": " + Fmt(m.Amount) + " " + _currency);
            }).ToList();

            SelectedCategoryId = keepCategory.HasValue && _shares.Any(c => c.Id == keepCategory) ? keepCategory : null;
            SelectedMonthKey = keepMonth.HasValue && Bars.Any(b => b.Key == keepMonth) ? keepMonth : null;
            UpdateCenter();
            await RefreshEarningsAsync();
        }
        catch (Exception ex)
        {
            Error = "Не удалось загрузить данные: " + ex.Message;
        }
    }

    async Task RefreshEarningsAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var period = SelectedPeriod?.Period ?? ChartPeriod.All;
            int? year = SelectedMonthKey.HasValue ? SelectedMonthKey / 100 : null;
            int? month = SelectedMonthKey.HasValue ? SelectedMonthKey % 100 : null;

            using var db = App.CreateDb();
            var rows = await FinanceService.EarningsAsync(db, _currency, DashboardService.PeriodStart(today, period),
                SelectedCategoryId, year, month);

            Earnings.Clear();
            foreach (var r in rows)
                Earnings.Add(new EarningItem(r.ProjectId, r.Project,
                    (string.IsNullOrWhiteSpace(r.Client) ? "Без заказчика" : r.Client) + " · " + r.Category,
                    r.CategoryColor, r.Date.ToString("dd.MM.yyyy"), Ru.PayType(r.Type),
                    (r.Amount < 0 ? "−" : "+") + Fmt(Math.Abs(r.Amount)) + " " + r.Currency));
            NoEarnings = Earnings.Count == 0;

            var parts = new List<string> { "Поступления" };
            var cat = SelectedCategoryId.HasValue ? _shares.FirstOrDefault(c => c.Id == SelectedCategoryId) : null;
            if (cat is not null) parts.Add(cat.Name);
            if (year.HasValue) parts.Add(new DateTime(year.Value, month!.Value, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture));
            EarningsTitle = string.Join(" · ", parts);
        }
        catch (Exception ex)
        {
            Error = "Не удалось загрузить поступления: " + ex.Message;
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
    private void ClearFilters()
    {
        SelectedCategoryId = null;
        SelectedMonthKey = null;
    }

    [RelayCommand]
    private void OpenProject(int projectId) => Navigator.OpenProject?.Invoke(projectId);

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
