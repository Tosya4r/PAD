using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAD.Desktop.Services;

namespace PAD.Desktop.ViewModels;

public sealed record NavItem(string Title, string Glyph, Func<ObservableObject> Create);

public sealed partial class MainViewModel : ObservableObject
{
    public IReadOnlyList<NavItem> Items { get; }
    public string Version { get; } = "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0");
    public string StoragePath { get; } = "Локально: " + App.DbPath;

    [ObservableProperty] private NavItem? selected;
    [ObservableProperty] private ObservableObject? currentPage;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string timeText = "";
    [ObservableProperty] private string dateText = "";
    [ObservableProperty] private string userName = "";
    [ObservableProperty] private string userRole = "";
    [ObservableProperty] private string userInitial = "P";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasAvatar))] private ImageSource? avatar;
    [ObservableProperty] private string dbSize = "";

    public bool HasAvatar => Avatar is not null;

    int? _pendingProjectId;
    int? _pendingNoteId;
    string? _pendingSearch;
    bool _pendingCreate;

    public MainViewModel()
    {
        Items = new NavItem[]
        {
            new("Главная", "", () => new DashboardViewModel()),
            new("Мои проекты", "", () => TakeProjects()),
            new("Задачи", "", () => new TasksViewModel()),
            new("Финансы", "", () => new FinanceViewModel()),
            new("Архив", "", () => new ArchiveViewModel()),
            new("Настройки", "", () => new SettingsViewModel()),
        };

        Navigator.OpenProject = OpenProject;
        Navigator.Search = text => { Selected = null; CurrentPage = new SearchViewModel(text); };
        Navigator.Go = (title, id) =>
        {
            _pendingNoteId = id;
            var item = Items.FirstOrDefault(i => i.Title == title);
            if (item is null) return;
            if (Selected == item) Selected = null;
            Selected = item;
        };
        Navigator.NewProject = () => { _pendingCreate = true; Go(1); };

        ProfileService.Changed += RefreshProfile;
        RefreshProfile();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();

        Selected = Items[0];
    }

    void Tick()
    {
        var now = DateTime.Now;
        TimeText = now.ToString("HH:mm");
        var d = now.ToString("dddd, d MMMM", System.Globalization.CultureInfo.CurrentCulture);
        DateText = d.Length == 0 ? d : char.ToUpper(d[0]) + d[1..];
    }

    void RefreshProfile()
    {
        var p = ProfileService.Current;
        UserName = p.Name;
        UserRole = p.Role;
        UserInitial = string.IsNullOrWhiteSpace(p.Name) ? "P" : p.Name.Trim()[..1].ToUpperInvariant();
        Avatar = ProfileService.LoadAvatar();
        try { DbSize = File.Exists(App.DbPath) ? (new FileInfo(App.DbPath).Length / 1024.0 / 1024.0).ToString("0.0") + " МБ" : ""; }
        catch { DbSize = ""; }
    }

    ProjectsViewModel TakeProjects()
    {
        var vm = new ProjectsViewModel(_pendingProjectId, _pendingSearch, _pendingCreate);
        _pendingProjectId = null;
        _pendingSearch = null;
        _pendingCreate = false;
        return vm;
    }

    KnowledgeViewModel TakeKnowledge()
    {
        var vm = new KnowledgeViewModel(_pendingNoteId);
        _pendingNoteId = null;
        return vm;
    }

    void Go(int index)
    {
        var item = Items[index];
        if (Selected == item) Selected = null;
        Selected = item;
    }

    void OpenProject(int id)
    {
        _pendingProjectId = id;
        Go(1);
    }

    [RelayCommand]
    private void RunSearch()
    {
        var text = SearchText.Trim();
        if (text.Length == 0) return;
        Navigator.Search?.Invoke(text);
        SearchText = "";
    }

    [RelayCommand]
    private void NewProject() => Navigator.NewProject?.Invoke();

    [RelayCommand]
    private void OpenProfile()
    {
        Selected = null;
        CurrentPage = new ProfileViewModel();
    }

    partial void OnSelectedChanged(NavItem? value)
    {
        if (value is null) return;
        var page = value.Create();
        CurrentPage = page;
        if (page is DashboardViewModel d) _ = d.LoadAsync();
        RefreshProfile();
    }
}
