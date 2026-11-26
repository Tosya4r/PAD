using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PAD.Data;

namespace PAD.Desktop.ViewModels;

public sealed record TopLine(int Id, string Name, string Category, string CategoryColor, string Received, string Price,
    double Percent, string PercentText);

public sealed record ExpectedLine(int ProjectId, string Project, string Client, string Amount, string DueText, string StatusText, string Color);

public sealed partial class FinanceViewModel : ObservableObject
{
    public ObservableCollection<StatTile> Tiles { get; } = new();
    public ObservableCollection<TopLine> Top { get; } = new();
    public ObservableCollection<ExpectedLine> Expected { get; } = new();

    [ObservableProperty] private string? error;
    [ObservableProperty] private bool noTop = true;
    [ObservableProperty] private bool noExpected = true;

    public FinanceViewModel() => _ = LoadAsync();

    async Task LoadAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            using var db = App.CreateDb();
            var o = await FinanceOverviewService.LoadAsync(db, today);
            var s = o.Stats;

            Tiles.Add(UiFormat.Money("Средний чек", s.AvgCheck, "", "#3B9EF0"));
            Tiles.Add(UiFormat.Money("Объём заказов", s.Volume, "", "#9B8AFB"));
            Tiles.Add(UiFormat.Count("Сдано проектов", s.Delivered, "", "#19C3A6"));
            Tiles.Add(UiFormat.Count("Оплачено полностью", s.Paid, "", "#34D399"));
            Tiles.Add(UiFormat.Count("Оплачено частично", s.Partial, "", "#F5A524"));
            Tiles.Add(UiFormat.Count("Не оплачено", s.Unpaid, "", "#F26D6D"));

            foreach (var t in o.Top)
                Top.Add(new TopLine(t.Id, t.Name, t.Category, t.CategoryColor, UiFormat.Fmt(t.Received) + " " + t.Currency,
                    "из " + UiFormat.Fmt(t.Price) + " " + t.Currency, (double)Math.Min(t.PaidPercent, 100m),
                    Math.Round(t.PaidPercent) + " %"));
            NoTop = Top.Count == 0;

            foreach (var e in o.Expected)
            {
                var (text, color) = e.Status switch
                {
                    ExpectedStatus.Overdue => ("Просрочено", "#F26D6D"),
                    ExpectedStatus.Soon => ("Скоро", "#F5A524"),
                    ExpectedStatus.PartiallyReceived => ("Частично получено", "#3B9EF0"),
                    _ => ("Ожидается", "#8A929E"),
                };
                var due = e.Due is null ? "срок не задан"
                    : e.Due.Value.ToString("dd.MM.yyyy") + (e.Days < 0 ? $" · {-e.Days} дн. назад" : e.Days == 0 ? " · сегодня" : $" · через {e.Days} дн.");
                Expected.Add(new ExpectedLine(e.ProjectId, e.Project,
                    string.IsNullOrWhiteSpace(e.Client) ? "Без заказчика" : e.Client,
                    UiFormat.Fmt(e.Remaining) + " " + e.Currency, due, text, color));
            }
            NoExpected = Expected.Count == 0;
        }
        catch (Exception ex) { Error = "Не удалось загрузить финансы: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenProject(int id) => Navigator.OpenProject?.Invoke(id);
}
