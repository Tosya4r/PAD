using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;

namespace PAD.Desktop.ViewModels;

public sealed record CalEvent(DateOnly Date, string Text, string Color, int? ProjectId);

public sealed partial class CalDay : ObservableObject
{
    public DateOnly Date { get; init; }
    public bool InMonth { get; init; }
    public bool IsToday { get; init; }
    public string Number => Date.Day.ToString();
    public IReadOnlyList<CalEvent> Events { get; init; } = Array.Empty<CalEvent>();
    public IReadOnlyList<string> Dots => Events.Select(e => e.Color).Distinct().Take(4).ToList();
    public double Opacity => InMonth ? 1 : 0.35;
    [ObservableProperty] private bool isSelected;
}

public sealed partial class CalendarViewModel : ObservableObject
{
    public ObservableCollection<CalDay> Days { get; } = new();
    public ObservableCollection<CalEvent> DayEvents { get; } = new();
    public IReadOnlyList<string> WeekDays { get; } = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };

    [ObservableProperty] private string monthTitle = "";
    [ObservableProperty] private string selectedTitle = "";
    [ObservableProperty] private bool noDayEvents = true;
    [ObservableProperty] private string? error;

    DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    DateOnly _selected = DateOnly.FromDateTime(DateTime.Today);

    public CalendarViewModel() => _ = LoadAsync();

    async Task LoadAsync()
    {
        try
        {
            var first = _month;
            var offset = ((int)first.DayOfWeek + 6) % 7;
            var start = first.AddDays(-offset);
            var end = start.AddDays(41);
            var today = DateOnly.FromDateTime(DateTime.Today);

            using var db = App.CreateDb();
            var events = new List<CalEvent>();

            var finished = new[] { ProjectStatus.Delivered, ProjectStatus.Completed, ProjectStatus.Cancelled };
            foreach (var p in await ProjectService.ListAsync(db))
                if (p.Deadline.HasValue && !finished.Contains(p.Status))
                    events.Add(new CalEvent(p.Deadline.Value, "Срок сдачи: " + p.Name, p.Deadline < today ? "#F26D6D" : "#3B9EF0", p.Id));

            foreach (var t in await TaskService.ListAsync(db, today, TaskFilter.Open))
                if (t.Due.HasValue) events.Add(new CalEvent(t.Due.Value, "Задача: " + t.Title, t.IsOverdue ? "#F26D6D" : "#9B8AFB", t.ProjectId));

            var pays = await db.Payments.AsNoTracking().Include(x => x.Project).ToListAsync();
            foreach (var x in pays.Where(x => x.IsReceived && x.Type != PaymentType.Refund))
                events.Add(new CalEvent(x.ReceivedDate, "Получен платёж: +" + UiFormat.Fmt(x.Amount) + " " + x.Currency +
                    (x.Project is null ? "" : " · " + x.Project.Name), "#34D399", x.ProjectId));

            var byDay = events.Where(e => e.Date >= start && e.Date <= end).ToLookup(e => e.Date);
            Days.Clear();
            for (var d = start; d <= end; d = d.AddDays(1))
                Days.Add(new CalDay { Date = d, InMonth = d.Month == _month.Month, IsToday = d == today,
                    Events = byDay[d].ToList(), IsSelected = d == _selected });

            var m = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            MonthTitle = char.ToUpper(m[0]) + m[1..];
            ShowDay(_selected);
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    void ShowDay(DateOnly d)
    {
        _selected = d;
        foreach (var c in Days) c.IsSelected = c.Date == d;
        DayEvents.Clear();
        var day = Days.FirstOrDefault(c => c.Date == d);
        if (day is not null) foreach (var e in day.Events) DayEvents.Add(e);
        NoDayEvents = DayEvents.Count == 0;
        SelectedTitle = d.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    [RelayCommand] private void Select(CalDay? d) { if (d is not null) ShowDay(d.Date); }

    [RelayCommand] private Task PrevAsync() { _month = _month.AddMonths(-1); return LoadAsync(); }

    [RelayCommand] private Task NextAsync() { _month = _month.AddMonths(1); return LoadAsync(); }

    [RelayCommand]
    private Task TodayAsync()
    {
        var t = DateOnly.FromDateTime(DateTime.Today);
        _month = new DateOnly(t.Year, t.Month, 1);
        _selected = t;
        return LoadAsync();
    }

    [RelayCommand]
    private void OpenEvent(CalEvent? e)
    {
        if (e?.ProjectId is int id) Navigator.OpenProject?.Invoke(id);
    }
}
