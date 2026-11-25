using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;
using Xunit;

public sealed class ProjectServiceTests : IDisposable
{
    readonly SqliteConnection _conn = new("Data Source=:memory:");
    readonly PadDbContext _db;

    public ProjectServiceTests()
    {
        _conn.Open();
        _db = new PadDbContext(new DbContextOptionsBuilder<PadDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    Task<int> Create(decimal price = 1500m, ProjectStatus st = ProjectStatus.InProgress) =>
        ProjectService.CreateAsync(_db, new NewProject("NOIR", "Клиент", 3, price, "eur", new DateOnly(2026, 12, 20), st, ""));

    NewPayment Pay(int id, decimal a, PaymentType t = PaymentType.Prepayment) =>
        new(id, a, "EUR", new DateOnly(2026, 11, 25), t, "", true);

    [Fact]
    public async Task Create_Normalizes_And_Validates()
    {
        var id = await Create();
        var row = (await ProjectService.GetAsync(_db, id))!;
        Assert.Equal("EUR", row.Currency);
        Assert.Equal(1500m, row.Price);
        Assert.Equal(PaymentStatus.Unpaid, row.PayStatus);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ProjectService.CreateAsync(_db, new NewProject(" ", "", 3, 1m, "EUR", null, ProjectStatus.Planned, "")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ProjectService.CreateAsync(_db, new NewProject("X", "", 3, -1m, "EUR", null, ProjectStatus.Planned, "")));
    }

    [Fact]
    public async Task Spec_Example_Three_Payments()
    {
        var id = await Create();
        await ProjectService.AddPaymentAsync(_db, Pay(id, 500m));
        await ProjectService.AddPaymentAsync(_db, Pay(id, 500m, PaymentType.Intermediate));

        var row = (await ProjectService.GetAsync(_db, id))!;
        Assert.Equal(1000m, row.Received);
        Assert.Equal(500m, row.Remaining);
        Assert.Equal(PaymentStatus.PartiallyPaid, row.PayStatus);

        await ProjectService.AddPaymentAsync(_db, Pay(id, 500m, PaymentType.Final));
        Assert.Equal(PaymentStatus.Paid, (await ProjectService.GetAsync(_db, id))!.PayStatus);
    }

    [Fact]
    public async Task Payment_Must_Be_Positive_And_Deletable()
    {
        var id = await Create();
        await Assert.ThrowsAsync<ArgumentException>(() => ProjectService.AddPaymentAsync(_db, Pay(id, 0m)));
        await ProjectService.AddPaymentAsync(_db, Pay(id, 300m));
        var pay = (await ProjectService.GetPaymentsAsync(_db, id)).Single();
        await ProjectService.DeletePaymentAsync(_db, pay.Id);
        Assert.Empty(await ProjectService.GetPaymentsAsync(_db, id));
    }

    [Fact]
    public async Task Delivery_Does_Not_Change_Payment_Status()
    {
        var id = await Create();
        await ProjectService.DeliverAsync(_db, new DeliveryInput(id, new DateOnly(2026, 12, 1), 1700m, "https://example.com", "ok", "передан"));

        var row = (await ProjectService.GetAsync(_db, id))!;
        Assert.Equal(ProjectStatus.Delivered, row.Status);
        Assert.Equal(1700m, row.Price);
        Assert.Equal(0m, row.Received);                       // сдан, но не оплачен
        Assert.Equal(PaymentStatus.Unpaid, row.PayStatus);
        Assert.Single(await _db.PriceHistory.ToListAsync());  // изменение цены сохранено в истории
        Assert.Single(await _db.Deliveries.ToListAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProjectService.DeliverAsync(_db, new DeliveryInput(id, new DateOnly(2026, 12, 2), 1700m, "", "", "")));
    }

    [Fact]
    public async Task Reopen_Keeps_Delivery_History()
    {
        var id = await Create();
        await ProjectService.DeliverAsync(_db, new DeliveryInput(id, new DateOnly(2026, 12, 1), 1500m, "", "", ""));
        await ProjectService.ReopenAsync(_db, id);

        Assert.Equal(ProjectStatus.InProgress, (await ProjectService.GetAsync(_db, id))!.Status);
        var d = await _db.Deliveries.SingleAsync();
        Assert.NotNull(d.ReopenedAt);

        await ProjectService.DeliverAsync(_db, new DeliveryInput(id, new DateOnly(2026, 12, 5), 1500m, "", "", ""));
        Assert.Equal(2, await _db.Deliveries.CountAsync());
    }

    [Fact]
    public async Task Status_Rules()
    {
        var id = await Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProjectService.SetStatusAsync(_db, id, ProjectStatus.Delivered));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProjectService.SetStatusAsync(_db, id, ProjectStatus.Completed));

        await ProjectService.SetStatusAsync(_db, id, ProjectStatus.OnHold);
        Assert.Equal(ProjectStatus.OnHold, (await ProjectService.GetAsync(_db, id))!.Status);
        Assert.Single(await _db.StatusHistory.ToListAsync());
    }

    [Fact]
    public async Task Search_Finds_By_Name_And_Client()
    {
        await Create();
        Assert.Single(await ProjectService.ListAsync(_db, "noir"));
        Assert.Single(await ProjectService.ListAsync(_db, "клиент"));
        Assert.Empty(await ProjectService.ListAsync(_db, "нет такого"));
    }
}
