using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public sealed record ProjectRow(
    int Id, string Name, string Client, string Description,
    int CategoryId, string CategoryName, string CategoryColor,
    ProjectStatus Status, decimal Price, string Currency,
    decimal Received, decimal Remaining, PaymentStatus PayStatus, DateOnly? Deadline);

public sealed record NewProject(string Name, string Client, int CategoryId, decimal Price, string Currency,
    DateOnly? Deadline, ProjectStatus Status, string Description);

public sealed record NewPayment(int ProjectId, decimal Amount, string Currency, DateOnly Date,
    PaymentType Type, string Purpose, bool IsReceived);

public sealed record DeliveryInput(int ProjectId, DateOnly Date, decimal FinalPrice, string Url, string Comment, string Result);

/// <summary>Операции с проектами. Каждая операция сохраняется одним SaveChanges, то есть одной транзакцией БД.</summary>
public static class ProjectService
{
    public static async Task<int> CreateAsync(PadDbContext db, NewProject n, CancellationToken ct = default)
    {
        var name = (n.Name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Введите название проекта.");
        if (n.Price < 0) throw new ArgumentException("Стоимость не может быть отрицательной.");
        var cur = NormalizeCurrency(n.Currency);
        if (!await db.Categories.AnyAsync(c => c.Id == n.CategoryId, ct)) throw new ArgumentException("Выберите категорию.");

        var p = new Project
        {
            Name = name, ClientName = (n.Client ?? "").Trim(), Description = (n.Description ?? "").Trim(),
            CategoryId = n.CategoryId, Price = n.Price, Currency = cur, Deadline = n.Deadline, Status = n.Status,
        };
        db.Projects.Add(p);
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    public static async Task<List<ProjectRow>> ListAsync(PadDbContext db, string? search = null, bool archived = false, CancellationToken ct = default)
    {
        var list = await db.Projects.AsNoTracking().Include(p => p.Category).Include(p => p.Payments)
            .Where(p => p.IsArchived == archived).ToListAsync(ct);

        var s = search?.Trim();
        if (!string.IsNullOrEmpty(s))
            list = list.Where(p => Has(p.Name, s) || Has(p.ClientName, s) || Has(p.Description, s)).ToList();

        return list.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Select(ToRow).ToList();
    }

    public static async Task<ProjectRow?> GetAsync(PadDbContext db, int id, CancellationToken ct = default)
    {
        var p = await db.Projects.AsNoTracking().Include(x => x.Category).Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? null : ToRow(p);
    }

    public static Task<List<Payment>> GetPaymentsAsync(PadDbContext db, int projectId, CancellationToken ct = default) =>
        db.Payments.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.ReceivedDate).ThenByDescending(x => x.Id).ToListAsync(ct);

    public static async Task AddPaymentAsync(PadDbContext db, NewPayment n, CancellationToken ct = default)
    {
        if (n.Amount <= 0) throw new ArgumentException("Сумма платежа должна быть больше нуля.");
        if (!await db.Projects.AnyAsync(p => p.Id == n.ProjectId, ct)) throw new InvalidOperationException("Проект не найден.");
        db.Payments.Add(new Payment
        {
            ProjectId = n.ProjectId, Amount = n.Amount, Currency = NormalizeCurrency(n.Currency), ReceivedDate = n.Date,
            Type = n.Type, Purpose = (n.Purpose ?? "").Trim(), IsReceived = n.IsReceived,
        });
        await db.SaveChangesAsync(ct);
    }

    public static async Task DeletePaymentAsync(PadDbContext db, int paymentId, CancellationToken ct = default)
    {
        var pay = await db.Payments.FindAsync(new object[] { paymentId }, ct);
        if (pay is null) return;
        db.Payments.Remove(pay);
        await db.SaveChangesAsync(ct);
    }

    public static async Task SetStatusAsync(PadDbContext db, int projectId, ProjectStatus status, CancellationToken ct = default)
    {
        var p = await db.Projects.FindAsync(new object[] { projectId }, ct) ?? throw new InvalidOperationException("Проект не найден.");
        if (p.Status == status) return;
        if (status == ProjectStatus.Delivered)
            throw new InvalidOperationException("Статус «Сдан заказчику» ставится кнопкой «Зафиксировать сдачу».");
        if (status == ProjectStatus.Completed && p.Status != ProjectStatus.Delivered)
            throw new InvalidOperationException("Завершить можно только сданный заказчику проект.");

        db.StatusHistory.Add(new ProjectStatusHistory { ProjectId = p.Id, ChangedAt = DateTime.Now, OldStatus = p.Status, NewStatus = status });
        p.Status = status;
        await db.SaveChangesAsync(ct);
    }

    public static async Task SetPriceAsync(PadDbContext db, int projectId, decimal newPrice, string reason, CancellationToken ct = default)
    {
        if (newPrice < 0) throw new ArgumentException("Стоимость не может быть отрицательной.");
        var p = await db.Projects.FindAsync(new object[] { projectId }, ct) ?? throw new InvalidOperationException("Проект не найден.");
        if (p.Price == newPrice) return;
        db.PriceHistory.Add(new ProjectPriceHistory
        { ProjectId = p.Id, ChangedAt = DateTime.Now, OldPrice = p.Price, NewPrice = newPrice, Currency = p.Currency, Reason = reason });
        p.Price = newPrice;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Фиксация сдачи. Оплату НЕ затрагивает: платежи учитываются отдельно.</summary>
    public static async Task DeliverAsync(PadDbContext db, DeliveryInput d, CancellationToken ct = default)
    {
        if (d.FinalPrice < 0) throw new ArgumentException("Финальная стоимость не может быть отрицательной.");
        var p = await db.Projects.FindAsync(new object[] { d.ProjectId }, ct) ?? throw new InvalidOperationException("Проект не найден.");
        if (p.Status is ProjectStatus.Delivered or ProjectStatus.Completed)
            throw new InvalidOperationException("Сдача этого проекта уже зафиксирована.");
        if (p.Status == ProjectStatus.Cancelled)
            throw new InvalidOperationException("Отменённый проект нельзя сдать.");

        if (p.Price != d.FinalPrice)
            db.PriceHistory.Add(new ProjectPriceHistory
            { ProjectId = p.Id, ChangedAt = DateTime.Now, OldPrice = p.Price, NewPrice = d.FinalPrice, Currency = p.Currency, Reason = "Финальная стоимость при сдаче" });

        db.StatusHistory.Add(new ProjectStatusHistory { ProjectId = p.Id, ChangedAt = DateTime.Now, OldStatus = p.Status, NewStatus = ProjectStatus.Delivered });
        db.Deliveries.Add(new ProjectDelivery
        {
            ProjectId = p.Id, DeliveredAt = d.Date.ToDateTime(TimeOnly.MinValue), FinalPrice = d.FinalPrice, Currency = p.Currency,
            ProductUrl = (d.Url ?? "").Trim(), Comment = (d.Comment ?? "").Trim(), HandoverResult = (d.Result ?? "").Trim(),
        });
        p.Price = d.FinalPrice;
        p.Status = ProjectStatus.Delivered;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Повторное открытие: запись о сдаче сохраняется, ей проставляется ReopenedAt.</summary>
    public static async Task ReopenAsync(PadDbContext db, int projectId, CancellationToken ct = default)
    {
        var p = await db.Projects.Include(x => x.Deliveries).FirstOrDefaultAsync(x => x.Id == projectId, ct)
                ?? throw new InvalidOperationException("Проект не найден.");
        if (p.Status is not (ProjectStatus.Delivered or ProjectStatus.Completed))
            throw new InvalidOperationException("Открыть повторно можно только сданный или завершённый проект.");

        foreach (var d in p.Deliveries.Where(d => d.ReopenedAt is null)) d.ReopenedAt = DateTime.Now;
        db.StatusHistory.Add(new ProjectStatusHistory { ProjectId = p.Id, ChangedAt = DateTime.Now, OldStatus = p.Status, NewStatus = ProjectStatus.InProgress });
        p.Status = ProjectStatus.InProgress;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Архивирование только скрывает проект: платежи и история остаются, статистика прошлых периодов не меняется.</summary>
    public static async Task SetArchivedAsync(PadDbContext db, int projectId, bool archived, CancellationToken ct = default)
    {
        var p = await db.Projects.FindAsync(new object[] { projectId }, ct) ?? throw new InvalidOperationException("Проект не найден.");
        p.IsArchived = archived;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Полное удаление проекта вместе с платежами (каскадом). Вызывать только после подтверждения.</summary>
    public static async Task DeleteAsync(PadDbContext db, int projectId, CancellationToken ct = default)
    {
        var p = await db.Projects.FindAsync(new object[] { projectId }, ct);
        if (p is null) return;
        db.Projects.Remove(p);
        await db.SaveChangesAsync(ct);
    }

    static ProjectRow ToRow(Project p)
    {
        var fin = FinanceCalculator.ForProject(p.Price, p.Currency, p.Payments.Select(x => x.ToRecord()));
        return new ProjectRow(p.Id, p.Name, p.ClientName, p.Description, p.CategoryId,
            p.Category?.Name ?? "", p.Category?.Color ?? "#6B7385",
            p.Status, p.Price, p.Currency, fin.Received, fin.Remaining, fin.Status, p.Deadline);
    }

    static bool Has(string? text, string s) => text?.Contains(s, StringComparison.CurrentCultureIgnoreCase) == true;

    static string NormalizeCurrency(string? c)
    {
        var cur = (c ?? "").Trim().ToUpperInvariant();
        if (cur.Length != 3 || !cur.All(char.IsLetter)) throw new ArgumentException("Валюта: код из трёх букв, например EUR.");
        return cur;
    }
}
