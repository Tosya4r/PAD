using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public enum ActivityKind { Payment, Status, Delivery, Task, Created }

public sealed record ActivityRow(DateTime When, ActivityKind Kind, string Text, string Sub, int? ProjectId);

/// <summary>Журнал активности собирается из реальных записей: платежи, смены статуса, сдачи, задачи, новые проекты.</summary>
public static class ActivityService
{
    public static async Task<List<ActivityRow>> RecentAsync(PadDbContext db, int take = 6, CancellationToken ct = default)
    {
        var rows = new List<ActivityRow>();

        var pays = await db.Payments.AsNoTracking().Include(p => p.Project).Where(p => p.IsReceived)
            .OrderByDescending(p => p.Id).Take(take * 2).ToListAsync(ct);
        rows.AddRange(pays.Select(p => new ActivityRow(
            p.ReceivedDate.ToDateTime(TimeOnly.MinValue), ActivityKind.Payment,
            (p.Type == PaymentType.Refund ? "Возврат " : "Получен платёж ") + p.Amount.ToString("#,##0.##") + " " + p.Currency,
            "в проекте " + p.Project?.Name, p.ProjectId)));

        var statuses = await db.StatusHistory.AsNoTracking().Include(s => s.Project)
            .OrderByDescending(s => s.Id).Take(take * 2).ToListAsync(ct);
        rows.AddRange(statuses.Select(s => new ActivityRow(s.ChangedAt, ActivityKind.Status,
            "Статус: " + StatusName(s.NewStatus), "в проекте " + s.Project?.Name, s.ProjectId)));

        var deliveries = await db.Deliveries.AsNoTracking().Include(d => d.Project)
            .OrderByDescending(d => d.Id).Take(take).ToListAsync(ct);
        rows.AddRange(deliveries.Select(d => new ActivityRow(d.DeliveredAt, ActivityKind.Delivery,
            "Зафиксирована сдача", "проект " + d.Project?.Name, d.ProjectId)));

        var tasks = await db.Tasks.AsNoTracking().Where(t => t.CompletedAt != null)
            .OrderByDescending(t => t.CompletedAt).Take(take).ToListAsync(ct);
        rows.AddRange(tasks.Select(t => new ActivityRow(t.CompletedAt!.Value, ActivityKind.Task,
            "Выполнена задача", t.Title, t.ProjectId)));

        var created = await db.Projects.AsNoTracking().OrderByDescending(p => p.Id).Take(take).ToListAsync(ct);
        rows.AddRange(created.Select(p => new ActivityRow(p.CreatedAt, ActivityKind.Created,
            "Создан проект", p.Name, p.Id)));

        return rows.OrderByDescending(r => r.When).Take(take).ToList();
    }

    static string StatusName(ProjectStatus s) => s switch
    {
        ProjectStatus.Planned => "Планируется",
        ProjectStatus.WaitingStart => "Ожидает начала",
        ProjectStatus.InProgress => "В работе",
        ProjectStatus.OnHold => "На паузе",
        ProjectStatus.InReview => "На проверке",
        ProjectStatus.Delivered => "Сдан заказчику",
        ProjectStatus.Completed => "Завершён",
        _ => "Отменён",
    };
}
