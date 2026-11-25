using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;
using Xunit;

public sealed class CategoryChartTests : IDisposable
{
    readonly SqliteConnection _conn = new("Data Source=:memory:");
    readonly PadDbContext _db;

    public CategoryChartTests()
    {
        _conn.Open();
        _db = new PadDbContext(new DbContextOptionsBuilder<PadDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    static Payment Pay(decimal a, int month) =>
        new() { Amount = a, Currency = "EUR", ReceivedDate = new DateOnly(2026, month, 20), Type = PaymentType.Final, IsReceived = true };

    [Fact]
    public void Palette_Gives_Distinct_Colors_Even_After_It_Runs_Out()
    {
        var used = new List<string>();
        for (var i = 0; i < 60; i++) used.Add(ColorPalette.NextFree(used));
        Assert.Equal(60, used.Select(c => c.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public async Task Seeded_Categories_Have_Distinct_Colors()
    {
        var colors = await _db.Categories.Select(c => c.Color).ToListAsync();
        Assert.Equal(colors.Count, colors.Distinct().Count());
    }

    [Fact]
    public async Task New_Category_Gets_Unused_Color_Automatically()
    {
        var before = await _db.Categories.Select(c => c.Color).ToListAsync();
        var created = await CategoryService.AddAsync(_db, "  Мобильные приложения ");
        Assert.Equal("Мобильные приложения", created.Name);
        Assert.DoesNotContain(created.Color, before);
    }

    [Fact]
    public async Task Duplicate_Or_Empty_Category_Is_Rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => CategoryService.AddAsync(_db, "лендинги"));
        await Assert.ThrowsAsync<ArgumentException>(() => CategoryService.AddAsync(_db, "   "));
    }

    [Fact]
    public async Task Chart_Uses_Received_Money_Per_Category_And_Period()
    {
        _db.Projects.Add(new Project { Name = "Shop", CategoryId = 3, Price = 9999m, Status = ProjectStatus.Completed,
            Payments = { Pay(500m, 11) } });
        _db.Projects.Add(new Project { Name = "App", CategoryId = 4, Price = 100m, Status = ProjectStatus.Completed,
            Payments = { Pay(500m, 11), Pay(2000m, 1) } });   // январь — вне периода «Месяц»
        await _db.SaveChangesAsync();

        var today = new DateOnly(2026, 11, 25);

        var month = await DashboardService.LoadAsync(_db, today, ChartPeriod.Month);
        Assert.Equal("EUR", month.ChartCurrency);
        Assert.Equal(500m, month.Categories.Single(c => c.Id == 3).Amount);
        Assert.Equal(500m, month.Categories.Single(c => c.Id == 4).Amount);
        Assert.Equal(50m, month.Categories.Single(c => c.Id == 3).Percent);

        var all = await DashboardService.LoadAsync(_db, today, ChartPeriod.All);
        Assert.Equal(2500m, all.Categories.Single(c => c.Id == 4).Amount);
        Assert.Equal(8, all.Categories.Count);                       // категории без дохода остаются в легенде
        Assert.Equal(0m, all.Categories.Single(c => c.Id == 1).Amount);
    }
}
