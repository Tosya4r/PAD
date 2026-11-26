using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;

namespace PAD.Desktop.ViewModels;

public sealed record TemplateItem(int Id, string Name, string Sub);

public sealed partial class TemplatesViewModel : ObservableObject
{
    public ObservableCollection<TemplateItem> Items { get; } = new();
    public IReadOnlyList<string> Currencies { get; } = new[] { "EUR", "USD", "GBP", "PLN", "CZK", "RUB" };

    [ObservableProperty] private IReadOnlyList<Category> categories = Array.Empty<Category>();
    [ObservableProperty] private TemplateItem? selected;
    [ObservableProperty] private bool editing;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string price = "0";
    [ObservableProperty] private string currency = "EUR";
    [ObservableProperty] private Category? category;
    [ObservableProperty] private string taskLines = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool isError;

    int _id;
    bool _suppress;

    public TemplatesViewModel() => _ = LoadAsync(null);

    partial void OnSelectedChanged(TemplateItem? value)
    {
        if (_suppress || value is null) return;
        _ = OpenAsync(value.Id);
    }

    async Task LoadAsync(int? keep)
    {
        try
        {
            using var db = App.CreateDb();
            Categories = await db.Categories.AsNoTracking().ToListAsync();
            var rows = await KnowledgeService.TemplatesAsync(db);
            _suppress = true;
            Items.Clear();
            foreach (var t in rows)
                Items.Add(new TemplateItem(t.Id, t.Name, UiFormat.Fmt(t.Price) + " " + t.Currency + " · задач: " +
                    t.TaskLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length));
            Selected = keep.HasValue ? Items.FirstOrDefault(i => i.Id == keep) : null;
            _suppress = false;
        }
        catch (Exception ex) { _suppress = false; Fail(ex); }
    }

    async Task OpenAsync(int id)
    {
        try
        {
            using var db = App.CreateDb();
            var t = await db.Templates.FindAsync(id);
            if (t is null) return;
            _id = t.Id; Name = t.Name; Description = t.Description; Price = t.Price.ToString("0.##", CultureInfo.CurrentCulture);
            Currency = t.Currency; Category = Categories.FirstOrDefault(c => c.Id == t.CategoryId); TaskLines = t.TaskLines;
            Editing = true; Status = ""; IsError = false;
        }
        catch (Exception ex) { Fail(ex); }
    }

    [RelayCommand]
    private void New()
    {
        _id = 0; Name = Description = TaskLines = ""; Price = "0"; Currency = "EUR"; Category = Categories.FirstOrDefault();
        Editing = true; Status = ""; IsError = false;
        _suppress = true; Selected = null; _suppress = false;
    }

    ProjectTemplate Build()
    {
        var s = (Price ?? "").Replace(" ", "").Replace(" ", "").Replace(',', '.');
        if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var p))
            throw new ArgumentException("Введите стоимость числом, например 1500.");
        return new ProjectTemplate { Id = _id, Name = Name, Description = Description, CategoryId = Category?.Id,
            Price = p, Currency = Currency, TaskLines = TaskLines };
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            using var db = App.CreateDb();
            _id = await KnowledgeService.SaveTemplateAsync(db, Build());
            IsError = false; Status = "Шаблон сохранён.";
            await LoadAsync(_id);
        }
        catch (Exception ex) { Fail(ex); }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_id == 0) { Editing = false; return; }
        if (MessageBox.Show("Удалить шаблон «" + Name + "»? Уже созданные из него проекты не изменятся.", "Удаление",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var db = App.CreateDb();
            await KnowledgeService.DeleteTemplateAsync(db, _id);
            _id = 0; Editing = false;
            await LoadAsync(null);
        }
        catch (Exception ex) { Fail(ex); }
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        try
        {
            if (_id == 0) throw new InvalidOperationException("Сначала сохраните шаблон.");
            using var db = App.CreateDb();
            var pid = await KnowledgeService.CreateProjectFromTemplateAsync(db, _id, null, null, null);
            Navigator.OpenProject?.Invoke(pid);
        }
        catch (Exception ex) { Fail(ex); }
    }

    void Fail(Exception ex) { IsError = true; Status = ex.Message; }
}
