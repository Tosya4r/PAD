using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;
using Xunit;

public sealed class DashboardServiceTests : IDisposable
{
    readonly SqliteConnection _conn = new("Data Source=:memory:");
    readonly PadDbContext _db;

    public DashboardServiceTests()
    {
        _conn.Open();
        _db = new PadDbContext(new DbContextOptionsBuilder<PadDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    static Payment Pay(decimal a, int month, PaymentType t = PaymentType.Prepayment) =>
        new() { Amount = a, Currency = "EUR", ReceivedDate = new DateOnly(2026, month, 20), Type = t, IsReceived = true };

    [Fact]
    public async Task Categories_Are_Seeded() =>
        Assert.Equal(8, await _db.Categories.CountAsync());

    [Fact]
    public async Task Money_RoundTrips_Exactly()
    {
        _db.Projects.Add(new Project { Name = "A", CategoryId = 4, Price = 1234.56m });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        Assert.Equal(1234.56m, (await _db.Projects.SingleAsync()).Price);
    }

    [Fact]
    public async Task Delivered_Unpaid_Project_Is_Not_Earnings()
    {
        // Заказ 1: в работе, 1500, получено 500 (окт) + 500 (ноя)
        _db.Projects.Add(new Project
        {
            Name = "InWork", CategoryId = 4, Price = 1500m, Status = ProjectStatus.InProgress,
            Payments = { Pay(500m, 10), Pay(500m, 11, PaymentType.Intermediate) }
        });
        // Заказ 2: сдан, 1000, оплат нет
        _db.Projects.Add(new Project { Name = "Delivered", CategoryId = 1, Price = 1000m, Status = ProjectStatus.Delivered });
        await _db.SaveChangesAsync();

        var s = await DashboardService.LoadAsync(_db, new DateOnly(2026, 11, 25));

        Assert.Equal(2, s.TotalProjects);
        Assert.Equal(1, s.Active);
        Assert.Equal(1500m, s.Potential["EUR"]);   // только несданный активный заказ
        Assert.Equal(1000m, s.Earned["EUR"]);      // только реально полученное
        Assert.Equal(1500m, s.Pending["EUR"]);     // 500 + 1000
        Assert.Equal(500m, s.MonthIncome["EUR"]);  // только ноябрь
    }

    [Fact]
    public async Task Archived_Project_Keeps_Earnings_But_Leaves_Counters()
    {
        _db.Projects.Add(new Project
        {
            Name = "Old", CategoryId = 2, Price = 800m, Status = ProjectStatus.Completed, IsArchived = true,
            Payments = { Pay(800m, 3, PaymentType.Final) }
        });
        await _db.SaveChangesAsync();

        var s = await DashboardService.LoadAsync(_db, new DateOnly(2026, 11, 25));
        Assert.Equal(0, s.TotalProjects);
        Assert.Equal(800m, s.Earned["EUR"]);
    }
}
