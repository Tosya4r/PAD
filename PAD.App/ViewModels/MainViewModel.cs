using CommunityToolkit.Mvvm.ComponentModel;

namespace PAD.Desktop.ViewModels;

public sealed record NavItem(string Title, string Glyph, Func<ObservableObject> Create);

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
        static NavItem Stub(string title, string glyph) =>
            new(title, glyph, () => new PlaceholderViewModel(title, "Раздел в разработке."));

        Items = new NavItem[]
        {
            new("Главная", "", () => new DashboardViewModel()),
            Stub("Мои проекты", ""),
            Stub("Задачи", ""),
            Stub("Календарь", ""),
            Stub("Финансы", ""),
            Stub("База знаний", ""),
            Stub("Архив", ""),
            Stub("Шаблоны", ""),
            Stub("Мой профиль", ""),
            Stub("Настройки", ""),
        };
        Selected = Items[0];
    }

    partial void OnSelectedChanged(NavItem? value)
    {
        if (value is null) return;
        var page = value.Create();
        CurrentPage = page;
        if (page is DashboardViewModel d) _ = d.LoadAsync();
    }
}
