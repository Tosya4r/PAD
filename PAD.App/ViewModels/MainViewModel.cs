using CommunityToolkit.Mvvm.ComponentModel;

namespace PAD.Desktop.ViewModels;

public sealed record NavItem(string Title, string Glyph, string Color, Func<ObservableObject> Create);

public sealed partial class PlaceholderViewModel : ObservableObject
{
    public string Title { get; }
    public string Text { get; }
    public PlaceholderViewModel(string title, string text) { Title = title; Text = text; }
}

public sealed partial class MainViewModel : ObservableObject
{
    public IReadOnlyList<NavItem> Items { get; }
    public string Version { get; } = "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0");
    public string StoragePath { get; } = "Локально: " + App.DbPath;

    [ObservableProperty] private NavItem? selected;
    [ObservableProperty] private ObservableObject? currentPage;

    public MainViewModel()
    {
        static NavItem Stub(string title, string glyph, string color) =>
            new(title, glyph, color, () => new PlaceholderViewModel(title, "Раздел в разработке."));

        Items = new NavItem[]
        {
            new("Главная", "", "#2F5BEA", () => new DashboardViewModel()),
            new("Мои проекты", "", "#6B5CE7", () => TakeProjects()),
            Stub("Задачи", "", "#1E9E5A"),
            Stub("Календарь", "", "#E5484D"),
            Stub("Финансы", "", "#12A594"),
            Stub("База знаний", "", "#D98A00"),
            Stub("Архив", "", "#7A8190"),
            Stub("Шаблоны", "", "#D6409F"),
            Stub("Мой профиль", "", "#3E8EED"),
            new("Настройки", "", "#7A8190", () => new SettingsViewModel()),
        };
        Navigator.OpenProject = OpenProject;
        Selected = Items[0];
    }

    int? _pendingProjectId;

    ProjectsViewModel TakeProjects()
    {
        var vm = new ProjectsViewModel(_pendingProjectId);
        _pendingProjectId = null;
        return vm;
    }

    void OpenProject(int id)
    {
        _pendingProjectId = id;
        var projects = Items[1];
        if (Selected == projects) Selected = null;
        Selected = projects;
    }

    partial void OnSelectedChanged(NavItem? value)
    {
        if (value is null) return;
        var page = value.Create();
        CurrentPage = page;
        if (page is DashboardViewModel d) _ = d.LoadAsync();
    }
}
