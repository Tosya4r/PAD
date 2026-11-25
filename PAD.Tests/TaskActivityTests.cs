using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAD.Data;
using PAD.Domain;
using Xunit;

public sealed class TaskActivityTests : IDisposable
{
    readonly SqliteConnection _conn = new("Data Source=:memory:");
    readonly PadDbContext _db;
    static readonly DateOnly Today = new(2026, 11, 25);

    public TaskActivityTests()
    {
        _conn.Open();
        _db = new PadDbContext(new DbContextOptionsBuilder<PadDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    [Fact]
    public async Task Add_Validates_And_Lists_Sorted_By_Due()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TaskService.AddAsync(_db, "  ", null, Priority.Low, null));
        await Assert.ThrowsAsync<ArgumentException>(() => TaskService.AddAsync(_db, "X", 999, Priority.Low, null));

        await TaskService.AddAsync(_db, "Позже", null, Priority.Low, new DateOnly(2026, 12, 1));
        await TaskService.AddAsync(_db, "Просрочена", null, Priority.High, new DateOnly(2026, 11, 20));
        await TaskService.AddAsync(_db, "Без срока", null, Priority.Medium, null);

        var open = await TaskService.ListAsync(_db, Today, TaskFilter.Open);
        Assert.Equal(new[] { "Просрочена", "Позже", "Без срока" }, open.Select(t => t.Title).ToArray());
        Assert.True(open[0].IsOverdue);
        Assert.Single(await TaskService.ListAsync(_db, Today, TaskFilter.Overdue));
    }

    [Fact]
    public async Task Done_Moves_Task_Out_Of_Open_And_Counts_Update()
    {
        var id = await TaskService.AddAsync(_db, "A", null, Priority.Medium, new DateOnly(2026, 11, 1));
        Assert.Equal((1, 1), await TaskService.CountsAsync(_db, Today));

        await TaskService.SetStatusAsync(_db, id, WorkStatus.Done);
        Assert.Equal((0, 0), await TaskService.CountsAsync(_db, Today));
        Assert.Single(await TaskService.ListAsync(_db, Today, TaskFilter.Done));

        await TaskService.SetStatusAsync(_db, id, WorkStatus.Todo);
        Assert.Equal((1, 1), await TaskService.CountsAsync(_db, Today));
    }

    [Fact]
    public async Task Deleting_Project_Keeps_Its_Tasks()
    {
        var pid = await ProjectService.CreateAsync(_db, new NewProject("P", "", 1, 10m, "EUR", null, ProjectStatus.InProgress, ""));
        await TaskService.AddAsync(_db, "T", pid, Priority.Medium, null);
        await ProjectService.DeleteAsync(_db, pid);
        var t = Assert.Single(await TaskService.ListAsync(_db, Today, TaskFilter.All));
        Assert.Null(t.ProjectId);
    }

    [Fact]
    public async Task Activity_Merges_Real_Events_Newest_First()
    {
        var id = await ProjectService.CreateAsync(_db, new NewProject("NOIR", "", 1, 1000m, "EUR", null, ProjectStatus.InProgress, ""));
        await ProjectService.AddPaymentAsync(_db, new NewPayment(id, 500m, "EUR", new DateOnly(2026, 11, 25), PaymentType.Prepayment, "", true));
        await ProjectService.SetStatusAsync(_db, id, ProjectStatus.OnHold);
        var t = await TaskService.AddAsync(_db, "Сделать", id, Priority.Medium, null);
        await TaskService.SetStatusAsync(_db, t, WorkStatus.Done);

        var rows = await ActivityService.RecentAsync(_db, 10);
        Assert.Contains(rows, r => r.Kind == ActivityKind.Payment && r.Text.Contains("500"));
        Assert.Contains(rows, r => r.Kind == ActivityKind.Status);
        Assert.Contains(rows, r => r.Kind == ActivityKind.Task);
        Assert.Contains(rows, r => r.Kind == ActivityKind.Created);
        Assert.Equal(rows.OrderByDescending(r => r.When).Select(r => r.When), rows.Select(r => r.When));
    }
}
