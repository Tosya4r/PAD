using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAD.Data;

namespace PAD.Desktop.ViewModels;

public sealed record HitItem(SearchHit Hit, string KindText, string Glyph, string Color);

public sealed partial class SearchViewModel : ObservableObject
{
    public ObservableCollection<HitItem> Hits { get; } = new();

    [ObservableProperty] private string query;
    [ObservableProperty] private string summary = "";

    public SearchViewModel(string query)
    {
        this.query = query;
        _ = RunAsync();
    }

    async Task RunAsync()
    {
        try
        {
            using var db = App.CreateDb();
            var hits = await SearchService.SearchAsync(db, Query);
            Hits.Clear();
            foreach (var h in hits)
            {
                var (t, g, c) = h.Kind switch
                {
                    SearchKind.Project => ("Проект", "", "#3B9EF0"),
                    SearchKind.Task => ("Задача", "", "#9B8AFB"),
                    SearchKind.Note => ("Заметка", "", "#F5A524"),
                    _ => ("Шаблон", "", "#19C3A6"),
                };
                Hits.Add(new HitItem(h, t, g, c));
            }
            Summary = hits.Count == 0 ? "По запросу «" + Query + "» ничего не найдено." : "Найдено: " + hits.Count;
        }
        catch (Exception ex) { Summary = ex.Message; }
    }

    [RelayCommand]
    private void Open(HitItem? item)
    {
        if (item is null) return;
        var h = item.Hit;
        switch (h.Kind)
        {
            case SearchKind.Project: Navigator.OpenProject?.Invoke(h.Id); break;
            case SearchKind.Task: Navigator.Go?.Invoke("Задачи", null); break;
            case SearchKind.Note: Navigator.Go?.Invoke("База знаний", h.Id); break;
            default: Navigator.Go?.Invoke("Шаблоны", null); break;
        }
    }
}
