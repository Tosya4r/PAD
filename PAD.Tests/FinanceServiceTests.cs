using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;
using Xunit;

public sealed class FinanceServiceTests : IDisposable
{
    readonly SqliteConnection _conn = new("Data Source=:memory:");
    readonly PadDbContext _db;

    public FinanceServiceTests()
    {
        _conn.Open();
        _db = new PadDbContext(new DbContextOptionsBuilder<PadDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    static Payment Pay(decimal a, int month, string cur = "EUR", bool received = true, PaymentType t = PaymentType.Final) =>
        new() { Amount = a, Currency = cur, ReceivedDate = new DateOnly(2026, month, 10), Type = t, IsReceived = received };

    async Task Seed()
    {
        _db.Projects.Add(new Project { Name = "Shop", CategoryId = 3, Price = 1000m, Payments = { Pay(500m, 9), Pay(300m, 11) } });
        _db.Projects.Add(new Project { Name = "App", CategoryId = 4, Price = 1000m,
            Payments = { Pay(200m, 11), Pay(999m, 11, received: false), Pay(50m, 11, "USD"), Pay(100m, 11, t: PaymentType.Refund) } });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Earnings_Only_Received_In_Currency_With_Refunds_Signed()
    {
        await Seed();
        var rows = await FinanceService.EarningsAsync(_db, "EUR", null);
        Assert.Equal(4, rows.Count);                       // без неполученного и без USD
        Assert.Equal(-100m, rows.Single(r => r.Type == PaymentType.Refund).Amount);
    }

    [Fact]
    public async Task Earnings_Filter_By_Category_And_Month()
    {
        await Seed();
        var shop = await FinanceService.EarningsAsync(_db, "EUR", null, categoryId: 3);
        Assert.Equal(2, shop.Count);
        Assert.All(shop, r => Assert.Equal("Shop", r.Project));

        var nov = await FinanceService.EarningsAsync(_db, "EUR", null, year: 2026, month: 11);
        Assert.Equal(3, nov.Count);

        var from = await FinanceService.EarningsAsync(_db, "EUR", new DateOnly(2026, 11, 1));
        Assert.DoesNotContain(from, r => r.Date.Month == 9);
    }

    [Fact]
    public async Task Monthly_Includes_Empty_Months_And_Sums_By_Received_Date()
    {
        await Seed();
        var m = await FinanceService.MonthlyAsync(_db, "EUR", new DateOnly(2026, 11, 25), ChartPeriod.Last6);
        Assert.Equal(6, m.Count);
        Assert.Equal(new DateOnly(2026, 6, 1), new DateOnly(m[0].Year, m[0].Month, 1));
        Assert.Equal(500m, m.Single(x => x.Month == 9).Amount);
        Assert.Equal(0m, m.Single(x => x.Month == 10).Amount);
        Assert.Equal(400m, m.Single(x => x.Month == 11).Amount);   // 300 + 200 − 100
    }

    [Fact]
    public async Task Monthly_All_Starts_At_First_Payment_But_At_Least_Six_Months()
    {
        var empty = await FinanceService.MonthlyAsync(_db, "EUR", new DateOnly(2026, 11, 25), ChartPeriod.All);
        Assert.Equal(6, empty.Count);
        await Seed();
        var all = await FinanceService.MonthlyAsync(_db, "EUR", new DateOnly(2026, 11, 25), ChartPeriod.All);
        Assert.Equal(6, all.Count);
    }
}
