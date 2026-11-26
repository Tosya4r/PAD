using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public static class ProjectStageService
{
    public static readonly string[] DefaultTitles =
    {
        "Этап 01 — Анализ",
        "Этап 02 — Проектирование",
        "Этап 03 — Архитектура",
        "Этап 04 — UX/UI-дизайн",
        "Этап 05 — Разработка",
        "Этап 06 — Тестирование",
        "Этап 07 — Публикация и сдача",
        "Этап 08 — Итоги",
    };

    public static async Task EnsureDefaultsAsync(PadDbContext db, int projectId, CancellationToken ct = default)
    {
        if (await db.Stages.AnyAsync(s => s.ProjectId == projectId, ct)) return;
        var now = DateTime.Now;
        for (var i = 0; i < DefaultTitles.Length; i++)
            db.Stages.Add(new ProjectStage { ProjectId = projectId, Order = i, Title = DefaultTitles[i], Status = StageStatus.NotStarted, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    public static async Task<List<ProjectStage>> ListAsync(PadDbContext db, int projectId, CancellationToken ct = default)
    {
        await EnsureDefaultsAsync(db, projectId, ct);
        return await db.Stages.AsNoTracking().Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Order).ThenBy(s => s.Id).ToListAsync(ct);
    }

    public static async Task SaveContentAsync(PadDbContext db, int stageId, string? content, CancellationToken ct = default)
    {
        var s = await db.Stages.FindAsync(new object[] { stageId }, ct) ?? throw new InvalidOperationException("Этап не найден.");
        s.Content = content ?? "";
        s.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public static async Task SetStatusAsync(PadDbContext db, int stageId, StageStatus status, CancellationToken ct = default)
    {
        var s = await db.Stages.FindAsync(new object[] { stageId }, ct) ?? throw new InvalidOperationException("Этап не найден.");
        s.Status = status;
        s.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public static async Task SetTitleAsync(PadDbContext db, int stageId, string title, CancellationToken ct = default)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0) throw new ArgumentException("Введите название этапа.");
        var s = await db.Stages.FindAsync(new object[] { stageId }, ct) ?? throw new InvalidOperationException("Этап не найден.");
        s.Title = title;
        s.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    public static async Task AddAsync(PadDbContext db, int projectId, string title, CancellationToken ct = default)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0) throw new ArgumentException("Введите название этапа.");
        var order = await db.Stages.Where(s => s.ProjectId == projectId).Select(s => (int?)s.Order).MaxAsync(ct) ?? -1;
        db.Stages.Add(new ProjectStage { ProjectId = projectId, Order = order + 1, Title = title, Status = StageStatus.NotStarted, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
        await db.SaveChangesAsync(ct);
    }

    public static async Task DeleteAsync(PadDbContext db, int stageId, CancellationToken ct = default)
    {
        var s = await db.Stages.FindAsync(new object[] { stageId }, ct);
        if (s is null) return;
        db.Stages.Remove(s);
        await db.SaveChangesAsync(ct);
    }
}