using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;

namespace PAD.Desktop.ViewModels;

public sealed record TaskLine(int Id, string Title, string Sub, string Priority, string PriorityColor, bool IsDone, string SubColor);

public sealed record FilterOption(string Title, TaskFilter Filter);

public sealed record ProjectOption(int? Id, string Title);

public sealed partial class TasksViewModel : ObservableObject
{
    public ObservableCollection<TaskLine> Tasks { get; } = new();
    public ObservableCollection<ProjectOption> Projects { get; } = new();

    public IReadOnlyList<FilterOption> Filters { get; } = new[]
    {
        new FilterOption("Открытые", TaskFilter.Open), new FilterOption("Просроченные", TaskFilter.Overdue),
        new FilterOption("Завершённые", TaskFilter.Done), new FilterOption("Все", TaskFilter.All),
    };

    public IReadOnlyList<Option<Priority>> Priorities { get; } = new[]
    {
        new Option<Priority>("Низкий", Priority.Low), new Option<Priority>("Средний", Priority.Medium),
        new Option<Priority>("Высокий", Priority.High), new Option<Priority>("Критичный", Priority.Critical),
    };

    [ObservableProperty] private FilterOption? selectedFilter;
    [ObservableProperty] private string newTitle = "";
    [ObservableProperty] private Option<Priority>? newPriority;
    [ObservableProperty] private ProjectOption? newProject;
    [ObservableProperty] private DateTime? newDue;
    [ObservableProperty] private string? error;
    [ObservableProperty] private bool isEmpty = true;

    public TasksViewModel()
    {
        selectedFilter = Filters[0];
        newPriority = Priorities[1];
        _ = InitAsync();
    }

    partial void OnSelectedFilterChanged(FilterOption? value) => _ = ReloadAsync();

    async Task InitAsync()
    {
        try
        {
            using var db = App.CreateDb();
            Projects.Clear();
            Projects.Add(new ProjectOption(null, "Без проекта"));
            foreach (var p in await db.Projects.AsNoTracking().Where(p => !p.IsArchived).OrderBy(p => p.Name).ToListAsync())
                Projects.Add(new ProjectOption(p.Id, p.Name));
            NewProject = Projects[0];
        }
        catch (Exception ex) { Error = ex.Message; }
        await ReloadAsync();
    }

    async Task ReloadAsync()
    {
        try
        {
            using var db = App.CreateDb();
            var rows = await TaskService.ListAsync(db, DateOnly.FromDateTime(DateTime.Today), (SelectedFilter ?? Filters[0]).Filter);
            Tasks.Clear();
            foreach (var t in rows)
            {
                var (prio, color) = t.Priority switch
                {
                    Priority.Critical => ("Критичный", "#F26D6D"),
                    Priority.High => ("Высокий", "#F5A524"),
                    Priority.Medium => ("Средний", "#3B9EF0"),
                    _ => ("Низкий", "#8A929E"),
                };
                Tasks.Add(new TaskLine(t.Id, t.Title,
                    (t.Project is null ? "Без проекта" : t.Project) + " · " + (t.Due.HasValue ? "срок " + t.Due.Value.ToString("dd.MM.yyyy") : "без срока")
                        + (t.IsOverdue ? " · просрочена" : ""),
                    prio, color, t.Status == WorkStatus.Done, t.IsOverdue ? "#F26D6D" : "#8A929E"));
            }
            IsEmpty = Tasks.Count == 0;
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        try
        {
            Error = null;
            using var db = App.CreateDb();
            await TaskService.AddAsync(db, NewTitle, NewProject?.Id, (NewPriority ?? Priorities[1]).Value,
                NewDue is null ? null : DateOnly.FromDateTime(NewDue.Value));
            NewTitle = "";
            NewDue = null;
            await ReloadAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task ToggleAsync(TaskLine? t)
    {
        if (t is null) return;
        try
        {
            using var db = App.CreateDb();
            await TaskService.SetStatusAsync(db, t.Id, t.IsDone ? WorkStatus.Todo : WorkStatus.Done);
            await ReloadAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteAsync(TaskLine? t)
    {
        if (t is null) return;
        try
        {
            using var db = App.CreateDb();
            await TaskService.DeleteAsync(db, t.Id);
            await ReloadAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}
