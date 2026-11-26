using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PAD.Data;
using PAD.Desktop.Services;

namespace PAD.Desktop.ViewModels;

public sealed partial class ProfileViewModel : ObservableObject
{
    public System.Collections.ObjectModel.ObservableCollection<StatTile> Tiles { get; } = new();

    [ObservableProperty] private string name = ProfileService.Current.Name;
    [ObservableProperty] private string role = ProfileService.Current.Role;
    [ObservableProperty] private string about = ProfileService.Current.About;
    [ObservableProperty] private DateTime? startDate = DateTime.TryParse(ProfileService.Current.StartDate, out var d) ? d : DateTime.Today;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasAvatar))] private ImageSource? avatar = ProfileService.LoadAvatar();
    [ObservableProperty] private string initial = "P";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool isError;

    public bool HasAvatar => Avatar is not null;

    public ProfileViewModel()
    {
        UpdateInitial();
        _ = LoadStatsAsync();
    }

    partial void OnNameChanged(string value) => UpdateInitial();

    void UpdateInitial() => Initial = string.IsNullOrWhiteSpace(Name) ? "P" : Name.Trim()[..1].ToUpperInvariant();

    async Task LoadStatsAsync()
    {
        try
        {
            using var db = App.CreateDb();
            var s = await DashboardService.LoadAsync(db, DateOnly.FromDateTime(DateTime.Today), ChartPeriod.All);
            Tiles.Add(UiFormat.Count("Проектов", s.TotalProjects, "", "#3B9EF0"));
            Tiles.Add(UiFormat.Count("Завершено", s.Completed, "", "#34D399"));
            Tiles.Add(UiFormat.Money("Заработано", s.Earned, "", "#19C3A6"));
        }
        catch (Exception ex) { IsError = true; Status = ex.Message; }
    }

    [RelayCommand]
    private void ChooseAvatar()
    {
        var dlg = new OpenFileDialog { Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ProfileService.SetAvatar(dlg.FileName);
            Avatar = ProfileService.LoadAvatar();
            IsError = false;
            Status = "Аватарка обновлена.";
        }
        catch (Exception ex) { IsError = true; Status = ex.Message; }
    }

    [RelayCommand]
    private void RemoveAvatar()
    {
        ProfileService.ClearAvatar();
        Avatar = null;
        IsError = false;
        Status = "Аватарка удалена.";
    }

    [RelayCommand]
    private void Save()
    {
        var p = ProfileService.Current;
        p.Name = string.IsNullOrWhiteSpace(Name) ? Environment.UserName : Name.Trim();
        p.Role = Role.Trim();
        p.About = About.Trim();
        p.StartDate = (StartDate ?? DateTime.Today).ToString("yyyy-MM-dd");
        ProfileService.Save();
        IsError = false;
        Status = "Профиль сохранён.";
    }
}
