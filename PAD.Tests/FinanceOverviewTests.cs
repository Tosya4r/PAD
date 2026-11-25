using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;
using Xunit;

public sealed class FinanceOverviewTests : IDisposable
{
    readonly SqliteConnection _conn = new("Data Source=:memory:");
    readonly PadDbContext _db;
    static readonly DateOnly Today = new(2026, 11, 25);

    public FinanceOverviewTests()
    {
        _conn.Open();
        _db = new PadDbContext(new DbContextOptionsBuilder<PadDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    static Payment Pay(decimal a) =>
        new() { Amount = a, Currency = "EUR", ReceivedDate = new DateOnly(2026, 11, 1), Type = PaymentType.Prepayment };

    async Task Seed()
    {
        _db.Projects.Add(new Project { Name = "Paid", CategoryId = 1, Price = 1000m, Status = ProjectStatus.Completed, Payments = { Pay(1000m) } });
        _db.Projects.Add(new Project { Name = "Partial", CategoryId = 2, Price = 1500m, Status = ProjectStatus.Delivered,
            Deadline = new DateOnly(2026, 11, 20), Payments = { Pay(500m) } });
        _db.Projects.Add(new Project { Name = "Unpaid", CategoryId = 3, Price = 500m, Status = ProjectStatus.InProgress,
            Deadline = new DateOnly(2026, 11, 28) });
        _db.Projects.Add(new Project { Name = "Later", CategoryId = 3, Price = 300m, Status = ProjectStatus.InProgress,
            Deadline = new DateOnly(2027, 2, 1) });
        _db.Projects.Add(new Project { Name = "Idea", CategoryId = 4, Price = 9999m, Status = ProjectStatus.Planned });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Stats_Count_Only_Agreed_Orders()
    {
        await Seed();
        var s = (await FinanceOverviewService.LoadAsync(_db, Today)).Stats;
        Assert.Equal(2, s.Delivered);
        Assert.Equal(1, s.Paid);
        Assert.Equal(1, s.Partial);
        Assert.Equal(2, s.Unpaid);
        Assert.Equal(3300m, s.Volume["EUR"]);        // без «Планируется»
        Assert.Equal(1250m, s.AvgCheck["EUR"]);      // (1000 + 1500) / 2
    }

    [Fact]
    public async Task Top_Is_Sorted_By_Received_And_Skips_Unpaid()
    {
        await Seed();
        var top = (await FinanceOverviewService.LoadAsync(_db, Today)).Top;
        Assert.Equal(new[] { "Paid", "Partial" }, top.Select(t => t.Name).ToArray());
        Assert.Equal(100m, top[0].PaidPercent);
        Assert.Equal(500m * 100m / 1500m, top[1].PaidPercent);
    }

    [Fact]
    public async Task Expected_Statuses_By_Deadline()
    {
        await Seed();
        var e = (await FinanceOverviewService.LoadAsync(_db, Today)).Expected;
        Assert.DoesNotContain(e, x => x.Project is "Paid" or "Idea");
        Assert.Equal(ExpectedStatus.Overdue, e.Single(x => x.Project == "Partial").Status);
        Assert.Equal(1000m, e.Single(x => x.Project == "Partial").Remaining);
        Assert.Equal(ExpectedStatus.Soon, e.Single(x => x.Project == "Unpaid").Status);
        Assert.Equal(ExpectedStatus.Expected, e.Single(x => x.Project == "Later").Status);
        Assert.Equal("Partial", e[0].Project);       // самый ранний срок — первым
    }

    [Fact]
    public async Task Archive_Hides_Project_But_Keeps_Money()
    {
        await Seed();
        var id = (await ProjectService.ListAsync(_db)).Single(r => r.Name == "Paid").Id;
        await ProjectService.SetArchivedAsync(_db, id, true);

        Assert.DoesNotContain(await ProjectService.ListAsync(_db), r => r.Name == "Paid");
        Assert.Equal("Paid", (await ProjectService.ListAsync(_db, null, archived: true)).Single().Name);
        var stats = await DashboardService.LoadAsync(_db, Today);
        Assert.Equal(1500m, stats.Earned["EUR"]);    // 1000 + 500: деньги архива не пропали

        await ProjectService.SetArchivedAsync(_db, id, false);
        Assert.Contains(await ProjectService.ListAsync(_db), r => r.Name == "Paid");
    }

    [Fact]
    public async Task Delete_Removes_Project_And_Its_Payments()
    {
        await Seed();
        var id = (await ProjectService.ListAsync(_db, "Partial")).Single().Id;
        await ProjectService.DeleteAsync(_db, id);
        Assert.Empty(await ProjectService.ListAsync(_db, "Partial"));
        Assert.Equal(1, await _db.Payments.CountAsync());
    }
}
