using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAD.Data;
using PAD.Desktop.Controls;
using PAD.Desktop.Services;
using PAD.Domain;

namespace PAD.Desktop.ViewModels;

public sealed record PeriodOption(string Title, ChartPeriod Period);

public sealed record LegendItem(int Id, string Name, string Color, string Amount, string Percent);

public sealed record EarningItem(int ProjectId, string Project, string Sub, string CategoryColor,
    string DateText, string TypeText, string AmountText);

public sealed record EventItem(int ProjectId, string Title, string Sub, string DateText, string Color, string Glyph);

public sealed record RecentProject(int Id, string Name, string Client, string StatusText, string StatusColor,
    string CategoryColor, double PaidPercent, string PercentText);

public sealed record TaskItemVm(int Id, string Title, string Sub, bool IsDone, string DueColor);

public sealed record ActivityItem(int? ProjectId, string Text, string Sub, string When, string Glyph, string Color);

public sealed partial class DashboardViewModel : ObservableObject
{
    public ObservableCollection<StatTile> CountTiles { get; } = new();
    public ObservableCollection<StatTile> FinanceTiles { get; } = new();
    public ObservableCollection<LegendItem> Legend { get; } = new();
    public ObservableCollection<EarningItem> Earnings { get; } = new();
    public ObservableCollection<EventItem> Events { get; } = new();
    public ObservableCollection<RecentProject> Recent { get; } = new();
    public ObservableCollection<TaskItemVm> MyTasks { get; } = new();
    public ObservableCollection<ActivityItem> Activity { get; } = new();

    public string Greeting => "Привет, " + ProfileService.Current.Name + "!";

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
    [ObservableProperty] private bool noEvents = true;
    [ObservableProperty] private bool noRecent = true;
    [ObservableProperty] private bool noTasks = true;
    [ObservableProperty] private bool noActivity = true;

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

            var (openTasks, overdueTasks) = await TaskService.CountsAsync(db, today);
            CountTiles.Clear();
            CountTiles.Add(UiFormat.Count("Всего проектов", s.TotalProjects, "\uE8F1", "#8A929E"));
            CountTiles.Add(UiFormat.Count("Активных", s.Active, "\uE768", "#3B9EF0"));
            CountTiles.Add(UiFormat.Count("Завершённых", s.Completed, "\uE73E", "#34D399"));
            CountTiles.Add(UiFormat.Count("На паузе", s.OnHold, "\uE769", "#F5A524"));
            CountTiles.Add(UiFormat.Count("Открытых задач", openTasks, "\uE762", "#9B8AFB"));
            CountTiles.Add(UiFormat.Count("Просрочено", overdueTasks, "\uE7BA", "#F26D6D"));

            FinanceTiles.Clear();
            FinanceTiles.Add(UiFormat.Money("Потенциальный доход", s.Potential, "\uE1D0", "#3B9EF0"));
            FinanceTiles.Add(UiFormat.Money("Заработано", s.Earned, "\uE73E", "#34D399"));
            FinanceTiles.Add(UiFormat.Money("Ожидает оплаты", s.Pending, "\uE823", "#F5A524"));
            FinanceTiles.Add(UiFormat.Money("Доход за месяц", s.MonthIncome, "\uE787", "#9B8AFB"));

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
            await LoadSideAsync(db, today);
        }
        catch (Exception ex)
        {
            Error = "Не удалось загрузить данные: " + ex.Message;
        }
    }


    async Task LoadSideAsync(PadDbContext db, DateOnly today)
    {
        var projects = await ProjectService.ListAsync(db);
        var finished = new[] { ProjectStatus.Delivered, ProjectStatus.Completed, ProjectStatus.Cancelled };

        Events.Clear();
        var items = new List<(DateOnly Date, EventItem Item)>();
        foreach (var p in projects.Where(p => p.Deadline.HasValue && !finished.Contains(p.Status)))
        {
            var d = p.Deadline!.Value;
            var late = d < today;
            items.Add((d, new EventItem(p.Id, "Срок: " + p.Name, string.IsNullOrWhiteSpace(p.Client) ? "Без заказчика" : p.Client,
                DayText(d, today), late ? "#F26D6D" : "#3B9EF0", "\uE787")));
        }
        var overview = await FinanceOverviewService.LoadAsync(db, today);
        foreach (var e in overview.Expected.Where(e => e.Due.HasValue))
            items.Add((e.Due!.Value, new EventItem(e.ProjectId, "Оплата: " + e.Project,
                "Ожидается " + UiFormat.Fmt(e.Remaining) + " " + e.Currency, DayText(e.Due.Value, today),
                e.Status == ExpectedStatus.Overdue ? "#F26D6D" : "#F5A524", "\uE1D0")));
        foreach (var (_, item) in items.OrderBy(i => i.Date).Take(5)) Events.Add(item);
        NoEvents = Events.Count == 0;

        Recent.Clear();
        foreach (var r in projects.Take(5))
        {
            var pct = r.Price > 0 ? (double)Math.Clamp(r.Received / r.Price * 100m, 0m, 100m) : 0;
            Recent.Add(new RecentProject(r.Id, r.Name, string.IsNullOrWhiteSpace(r.Client) ? "Без заказчика" : r.Client,
                Ru.Status(r.Status), Ru.StatusColor(r.Status), r.CategoryColor, pct, Math.Round(pct) + " %"));
        }
        NoRecent = Recent.Count == 0;

        await LoadTasksAsync(db, today);

        Activity.Clear();
        foreach (var a in await ActivityService.RecentAsync(db, 6))
        {
            var (glyph, color) = a.Kind switch
            {
                ActivityKind.Payment => ("\uE1D0", "#34D399"),
                ActivityKind.Delivery => ("\uE73E", "#19C3A6"),
                ActivityKind.Task => ("\uE762", "#9B8AFB"),
                ActivityKind.Status => ("\uE72C", "#3B9EF0"),
                _ => ("\uE710", "#8A929E"),
            };
            Activity.Add(new ActivityItem(a.ProjectId, a.Text, a.Sub, a.When.ToString("dd.MM HH:mm"), glyph, color));
        }
        NoActivity = Activity.Count == 0;
    }

    async Task LoadTasksAsync(PadDbContext db, DateOnly today)
    {
        MyTasks.Clear();
        foreach (var t in (await TaskService.ListAsync(db, today, TaskFilter.Open)).Take(5))
            MyTasks.Add(new TaskItemVm(t.Id, t.Title,
                (t.Project is null ? "" : t.Project + " · ") + (t.Due.HasValue ? "до " + t.Due.Value.ToString("dd.MM") : "без срока"),
                false, t.IsOverdue ? "#F26D6D" : "#8A929E"));
        NoTasks = MyTasks.Count == 0;
    }

    [RelayCommand]
    private async Task CompleteTaskAsync(int id)
    {
        try
        {
            using var db = App.CreateDb();
            await TaskService.SetStatusAsync(db, id, WorkStatus.Done);
            var today = DateOnly.FromDateTime(DateTime.Today);
            await LoadTasksAsync(db, today);
            var (open, overdue) = await TaskService.CountsAsync(db, today);
            CountTiles[4] = UiFormat.Count("Открытых задач", open, "\uE762", "#9B8AFB");
            CountTiles[5] = UiFormat.Count("Просрочено", overdue, "\uE7BA", "#F26D6D");
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    static string DayText(DateOnly d, DateOnly today)
    {
        var n = d.DayNumber - today.DayNumber;
        var date = d.ToString("d MMM", CultureInfo.CurrentCulture);
        return n switch
        {
            0 => "Сегодня",
            1 => "Завтра",
            < 0 => date + " · просрочено на " + (-n) + " дн.",
            _ => date,
        };
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

    static string Fmt(decimal v) => UiFormat.Fmt(v);

    static string Pct(decimal p) => UiFormat.Pct(p);

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];
}
