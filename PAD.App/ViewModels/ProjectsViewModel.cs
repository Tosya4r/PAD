using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;

namespace PAD.Desktop.ViewModels;

public sealed record ProjectItem(int Id, string Name, string Client, string CategoryColor,
    string StatusText, string StatusColor, string PriceText, string PayText, string PayColor);

public sealed record PaymentRow(int Id, string Date, string Type, string Amount, string Purpose, string Received);

public sealed record Option<T>(string Title, T Value);

public sealed partial class ProjectsViewModel : ObservableObject
{
    public ObservableCollection<ProjectItem> Items { get; } = new();
    public ObservableCollection<PaymentRow> Payments { get; } = new();
    public ObservableCollection<StatTile> DetailTiles { get; } = new();

    public IReadOnlyList<string> Currencies { get; } = new[] { "EUR", "USD", "GBP", "PLN", "CZK", "RUB" };
    public IReadOnlyList<Option<ProjectStatus>> StatusOptions { get; } =
        Enum.GetValues<ProjectStatus>().Select(s => new Option<ProjectStatus>(Ru.Status(s), s)).ToList();
    public IReadOnlyList<Option<PaymentType>> PaymentTypes { get; } =
        Enum.GetValues<PaymentType>().Select(t => new Option<PaymentType>(Ru.PayType(t), t)).ToList();

    [ObservableProperty] private IReadOnlyList<Category> categories = Array.Empty<Category>();
    [ObservableProperty] private string? error;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private ProjectItem? selectedItem;

    // создание
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowDetail), nameof(ShowEmpty))] private bool isCreating;
    [ObservableProperty] private string newName = "";
    [ObservableProperty] private string newClient = "";
    [ObservableProperty] private string newDescription = "";
    [ObservableProperty] private string newPrice = "";
    [ObservableProperty] private string newCurrency = "EUR";
    [ObservableProperty] private DateTime? newDeadline;
    [ObservableProperty] private Category? newCategory;
    [ObservableProperty] private Option<ProjectStatus>? newStatus;

    // карточка
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowDetail), nameof(ShowEmpty))] private bool hasDetail;
    [ObservableProperty] private string detailName = "";
    [ObservableProperty] private string detailSub = "";
    [ObservableProperty] private string detailPay = "";
    [ObservableProperty] private string detailPayColor = "#6B7385";
    [ObservableProperty] private Option<ProjectStatus>? selectedStatus;
    [ObservableProperty] private bool canDeliver;
    [ObservableProperty] private bool canReopen;

    // платёж
    [ObservableProperty] private string payAmount = "";
    [ObservableProperty] private Option<PaymentType>? payType;
    [ObservableProperty] private DateTime? payDate = DateTime.Today;
    [ObservableProperty] private string payPurpose = "";
    [ObservableProperty] private bool payIsReceived = true;

    // сдача
    [ObservableProperty] private bool isDelivering;
    [ObservableProperty] private string delivPrice = "";
    [ObservableProperty] private DateTime? delivDate = DateTime.Today;
    [ObservableProperty] private string delivUrl = "";
    [ObservableProperty] private string delivResult = "";
    [ObservableProperty] private string delivComment = "";

    public bool ShowDetail => HasDetail && !IsCreating;
    public bool ShowEmpty => !HasDetail && !IsCreating;

    int _detailId;
    string _detailCurrency = "EUR";
    decimal _detailPrice;
    bool _suppress;

    readonly int? _openId;

    public ProjectsViewModel(int? openId = null)
    {
        _openId = openId;
        payType = PaymentTypes[0];
        newStatus = StatusOptions.First(o => o.Value == ProjectStatus.InProgress);
        _ = InitAsync();
    }

    async Task InitAsync()
    {
        await Run(async () =>
        {
            using var db = App.CreateDb();
            Categories = await db.Categories.AsNoTracking().ToListAsync();
            NewCategory = Categories.FirstOrDefault();
            await LoadListAsync(_openId);
            if (_openId.HasValue) await LoadDetailAsync(_openId);
        });
    }

    partial void OnSearchTextChanged(string value) => _ = Run(() => LoadListAsync(SelectedItem?.Id));

    partial void OnSelectedItemChanged(ProjectItem? value)
    {
        if (_suppress) return;
        IsCreating = false;
        IsDelivering = false;
        _ = Run(() => LoadDetailAsync(value?.Id));
    }

    partial void OnSelectedStatusChanged(Option<ProjectStatus>? value)
    {
        if (_suppress || value is null || !HasDetail) return;
        var id = _detailId;
        _ = Run(async () =>
        {
            try
            {
                using var db = App.CreateDb();
                await ProjectService.SetStatusAsync(db, id, value.Value);
            }
            finally { await RefreshAsync(id); }
        });
    }

    async Task Run(Func<Task> action)
    {
        try { Error = null; await action(); }
        catch (Exception ex) { Error = ex.Message; }
    }

    async Task RefreshAsync(int id)
    {
        await LoadListAsync(id);
        await LoadDetailAsync(id);
    }

    async Task LoadListAsync(int? keepId)
    {
        using var db = App.CreateDb();
        var rows = await ProjectService.ListAsync(db, SearchText);
        _suppress = true;
        try
        {
            Items.Clear();
            foreach (var r in rows)
                Items.Add(new ProjectItem(r.Id, r.Name, string.IsNullOrWhiteSpace(r.Client) ? "Без заказчика" : r.Client,
                    r.CategoryColor, Ru.Status(r.Status), Ru.StatusColor(r.Status),
                    Money(r.Price, r.Currency), Ru.Pay(r.PayStatus), Ru.PayColor(r.PayStatus)));
            SelectedItem = keepId.HasValue ? Items.FirstOrDefault(i => i.Id == keepId) : null;
        }
        finally { _suppress = false; }
    }

    async Task LoadDetailAsync(int? id)
    {
        if (id is null) { HasDetail = false; return; }
        using var db = App.CreateDb();
        var r = await ProjectService.GetAsync(db, id.Value);
        if (r is null) { HasDetail = false; return; }
        var pays = await ProjectService.GetPaymentsAsync(db, id.Value);

        _suppress = true;
        try
        {
            _detailId = r.Id;
            _detailCurrency = r.Currency;
            _detailPrice = r.Price;
            DetailName = r.Name;
            DetailSub = (string.IsNullOrWhiteSpace(r.Client) ? "Без заказчика" : r.Client) + " · " + r.CategoryName;
            DetailPay = Ru.Pay(r.PayStatus);
            DetailPayColor = Ru.PayColor(r.PayStatus);
            SelectedStatus = StatusOptions.First(o => o.Value == r.Status);
            CanDeliver = r.Status is not (ProjectStatus.Delivered or ProjectStatus.Completed or ProjectStatus.Cancelled);
            CanReopen = r.Status is ProjectStatus.Delivered or ProjectStatus.Completed;

            DetailTiles.Clear();
            DetailTiles.Add(Tile("Стоимость", r.Price, r.Currency, "", "#2F5BEA", "#E6EDFF"));
            DetailTiles.Add(Tile("Получено", r.Received, r.Currency, "", "#1E9E5A", "#E3F5EA"));
            DetailTiles.Add(Tile("Остаток к оплате", r.Remaining, r.Currency, "", "#D98A00", "#FCF0D9"));
            DetailTiles.Add(new StatTile("Дедлайн",
                new[] { new MoneyLine(r.Deadline?.ToString("dd.MM.yyyy") ?? "не задан", "") }, "", "#7A4DE0", "#EEE8FC"));

            Payments.Clear();
            foreach (var p in pays)
                Payments.Add(new PaymentRow(p.Id, p.ReceivedDate.ToString("dd.MM.yyyy"), Ru.PayType(p.Type),
                    (p.Type == PaymentType.Refund ? "−" : "+") + Fmt(p.Amount) + " " + p.Currency,
                    p.Purpose, p.IsReceived ? "получен" : "ожидается"));

            HasDetail = true;
        }
        finally { _suppress = false; }
    }

    [RelayCommand]
    private void NewProject()
    {
        IsCreating = true;
        IsDelivering = false;
        NewName = NewClient = NewDescription = NewPrice = "";
        NewDeadline = null;
        Error = null;
    }

    [RelayCommand]
    private void CancelCreate() => IsCreating = false;

    [RelayCommand]
    private Task CreateProjectAsync() => Run(async () =>
    {
        if (NewCategory is null) throw new ArgumentException("Выберите категорию.");
        if (!TryMoney(NewPrice, out var price)) throw new ArgumentException("Введите стоимость числом, например 1500.");
        using var db = App.CreateDb();
        var id = await ProjectService.CreateAsync(db, new NewProject(NewName, NewClient, NewCategory.Id, price, NewCurrency,
            ToDate(NewDeadline), NewStatus?.Value ?? ProjectStatus.Planned, NewDescription));
        IsCreating = false;
        await RefreshAsync(id);
    });

    [RelayCommand]
    private Task AddPaymentAsync() => Run(async () =>
    {
        if (!HasDetail) return;
        if (!TryMoney(PayAmount, out var amount)) throw new ArgumentException("Введите сумму числом, например 500.");
        using var db = App.CreateDb();
        await ProjectService.AddPaymentAsync(db, new NewPayment(_detailId, amount, _detailCurrency,
            ToDate(PayDate) ?? DateOnly.FromDateTime(DateTime.Today), (PayType ?? PaymentTypes[0]).Value, PayPurpose, PayIsReceived));
        PayAmount = PayPurpose = "";
        await RefreshAsync(_detailId);
    });

    [RelayCommand]
    private Task DeletePaymentAsync(PaymentRow? row) => Run(async () =>
    {
        if (row is null) return;
        using var db = App.CreateDb();
        await ProjectService.DeletePaymentAsync(db, row.Id);
        await RefreshAsync(_detailId);
    });

    [RelayCommand]
    private void OpenDelivery()
    {
        DelivPrice = _detailPrice.ToString("0.##", CultureInfo.CurrentCulture);
        DelivDate = DateTime.Today;
        DelivUrl = DelivResult = DelivComment = "";
        IsDelivering = true;
    }

    [RelayCommand]
    private void CancelDelivery() => IsDelivering = false;

    [RelayCommand]
    private Task ConfirmDeliveryAsync() => Run(async () =>
    {
        if (!TryMoney(DelivPrice, out var price)) throw new ArgumentException("Введите финальную стоимость числом.");
        using var db = App.CreateDb();
        await ProjectService.DeliverAsync(db, new DeliveryInput(_detailId,
            ToDate(DelivDate) ?? DateOnly.FromDateTime(DateTime.Today), price, DelivUrl, DelivComment, DelivResult));
        IsDelivering = false;
        await RefreshAsync(_detailId);
    });

    [RelayCommand]
    private Task ReopenAsync() => Run(async () =>
    {
        using var db = App.CreateDb();
        await ProjectService.ReopenAsync(db, _detailId);
        await RefreshAsync(_detailId);
    });

    static StatTile Tile(string label, decimal amount, string cur, string glyph, string accent, string tint) =>
        new(label, new[] { new MoneyLine(Fmt(amount), cur) }, glyph, accent, tint);

    static string Fmt(decimal v) => v.ToString("#,##0.##", CultureInfo.CurrentCulture);

    static string Money(decimal v, string cur) => Fmt(v) + " " + cur;

    static DateOnly? ToDate(DateTime? d) => d is null ? null : DateOnly.FromDateTime(d.Value);

    static bool TryMoney(string? s, out decimal v)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Replace(" ", "").Replace(" ", "").Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out v);
    }
}
