using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public enum TaskFilter { Open, Overdue, Done, All }

public sealed record TaskRow(int Id, string Title, int? ProjectId, string? Project, WorkStatus Status,
    Priority Priority, DateOnly? Due, bool IsOverdue);

public static class TaskService
{
    public static async Task<int> AddAsync(PadDbContext db, string? title, int? projectId, Priority priority,
        DateOnly? due, string? notes = null, CancellationToken ct = default)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0) throw new ArgumentException("Введите название задачи.");
        if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw new ArgumentException("Проект не найден.");
        var t = new TaskItem { Title = title, ProjectId = projectId, Priority = priority, Due = due, Notes = (notes ?? "").Trim() };
        db.Tasks.Add(t);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    public static async Task<List<TaskRow>> ListAsync(PadDbContext db, DateOnly today, TaskFilter filter,
        int? projectId = null, CancellationToken ct = default)
    {
        var all = await db.Tasks.AsNoTracking().Include(t => t.Project)
            .Where(t => projectId == null || t.ProjectId == projectId).ToListAsync(ct);

        IEnumerable<TaskItem> q = filter switch
        {
            TaskFilter.Open => all.Where(t => t.Status != WorkStatus.Done),
            TaskFilter.Overdue => all.Where(t => t.Status != WorkStatus.Done && t.Due < today),
            TaskFilter.Done => all.Where(t => t.Status == WorkStatus.Done),
            _ => all,
        };

        return q.OrderBy(t => t.Status == WorkStatus.Done)
                .ThenBy(t => t.Due ?? DateOnly.MaxValue)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.Id)
                .Select(t => new TaskRow(t.Id, t.Title, t.ProjectId, t.Project?.Name, t.Status, t.Priority, t.Due,
                    t.Status != WorkStatus.Done && t.Due < today))
                .ToList();
    }

    public static async Task<(int Open, int Overdue)> CountsAsync(PadDbContext db, DateOnly today, CancellationToken ct = default)
    {
        var open = await db.Tasks.AsNoTracking().Where(t => t.Status != WorkStatus.Done).ToListAsync(ct);
        return (open.Count, open.Count(t => t.Due < today));
    }

    public static async Task SetStatusAsync(PadDbContext db, int id, WorkStatus status, CancellationToken ct = default)
    {
        var t = await db.Tasks.FindAsync(new object[] { id }, ct) ?? throw new InvalidOperationException("Задача не найдена.");
        t.Status = status;
        t.CompletedAt = status == WorkStatus.Done ? DateTime.Now : null;
        await db.SaveChangesAsync(ct);
    }

    public static async Task DeleteAsync(PadDbContext db, int id, CancellationToken ct = default)
    {
        var t = await db.Tasks.FindAsync(new object[] { id }, ct);
        if (t is null) return;
        db.Tasks.Remove(t);
        await db.SaveChangesAsync(ct);
    }
}
