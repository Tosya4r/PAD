using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAD.Data;

namespace PAD.Desktop.ViewModels;

public sealed record NoteItem(int Id, string Title, string Sub);

public sealed partial class KnowledgeViewModel : ObservableObject
{
    public ObservableCollection<NoteItem> Notes { get; } = new();

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private NoteItem? selected;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEditor))] private bool editing;
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string body = "";
    [ObservableProperty] private string tags = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool isError;

    public bool HasEditor => Editing;

    int? _id;
    bool _suppress;

    public KnowledgeViewModel(int? openId = null)
    {
        _ = LoadAsync(openId);
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync(_id);

    partial void OnSelectedChanged(NoteItem? value)
    {
        if (_suppress || value is null) return;
        _ = OpenAsync(value.Id);
    }

    async Task LoadAsync(int? keep)
    {
        try
        {
            using var db = App.CreateDb();
            var rows = await KnowledgeService.NotesAsync(db, SearchText);
            _suppress = true;
            Notes.Clear();
            foreach (var n in rows)
                Notes.Add(new NoteItem(n.Id, n.Title, (n.Tags.Length > 0 ? n.Tags + " · " : "") + n.UpdatedAt.ToString("dd.MM.yyyy")));
            Selected = keep.HasValue ? Notes.FirstOrDefault(n => n.Id == keep) : null;
            _suppress = false;
            if (keep.HasValue && !Editing) await OpenAsync(keep.Value);
        }
        catch (Exception ex) { _suppress = false; Fail(ex); }
    }

    async Task OpenAsync(int id)
    {
        try
        {
            using var db = App.CreateDb();
            var n = await db.Notes.FindAsync(id);
            if (n is null) return;
            _id = n.Id; Title = n.Title; Body = n.Body; Tags = n.Tags;
            Editing = true; Status = ""; IsError = false;
        }
        catch (Exception ex) { Fail(ex); }
    }

    [RelayCommand]
    private void New()
    {
        _id = null; Title = Body = Tags = ""; Editing = true; Status = ""; IsError = false;
        _suppress = true; Selected = null; _suppress = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            using var db = App.CreateDb();
            _id = await KnowledgeService.SaveNoteAsync(db, _id, Title, Body, Tags);
            IsError = false; Status = "Сохранено " + DateTime.Now.ToString("HH:mm");
            await LoadAsync(_id);
        }
        catch (Exception ex) { Fail(ex); }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_id is null) { Editing = false; return; }
        if (MessageBox.Show("Удалить заметку «" + Title + "»?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var db = App.CreateDb();
            await KnowledgeService.DeleteNoteAsync(db, _id.Value);
            _id = null; Editing = false;
            await LoadAsync(null);
        }
        catch (Exception ex) { Fail(ex); }
    }

    void Fail(Exception ex) { IsError = true; Status = ex.Message; }
}
