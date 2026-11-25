using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public enum ExpectedStatus { Expected, Soon, Overdue, PartiallyReceived }

public sealed record OverviewStats(
    IReadOnlyDictionary<string, decimal> AvgCheck,   // средняя стоимость сданных/завершённых заказов
    IReadOnlyDictionary<string, decimal> Volume,     // объём согласованных заказов
    int Delivered, int Paid, int Partial, int Unpaid);

public sealed record TopProject(int Id, string Name, string Category, string CategoryColor,
    decimal Price, decimal Received, string Currency, DateOnly? DeliveredAt, decimal PaidPercent);

public sealed record ExpectedPayment(int ProjectId, string Project, string Client, decimal Remaining, string Currency,
    DateOnly? Due, int? Days, ExpectedStatus Status);

public sealed record FinanceOverview(OverviewStats Stats, IReadOnlyList<TopProject> Top, IReadOnlyList<ExpectedPayment> Expected);

public static class FinanceOverviewService
{
    /// <summary>Срок ожидаемой оплаты — крайний срок проекта (отдельных графиков платежей пока нет).</summary>
    public static async Task<FinanceOverview> LoadAsync(PadDbContext db, DateOnly today, CancellationToken ct = default)
    {
        var projects = await db.Projects.AsNoTracking()
            .Include(p => p.Category).Include(p => p.Payments).Include(p => p.Deliveries).ToListAsync(ct);

        var agreed = projects.Where(p => p.Status is not (ProjectStatus.Planned or ProjectStatus.Cancelled)).ToList();
        var fin = agreed.ToDictionary(p => p.Id,
            p => FinanceCalculator.ForProject(p.Price, p.Currency, p.Payments.Select(x => x.ToRecord())));

        var done = projects.Where(p => p.Status is ProjectStatus.Delivered or ProjectStatus.Completed).ToList();
        var avg = done.GroupBy(p => p.Currency).ToDictionary(g => g.Key, g => g.Average(p => p.Price));
        var volume = agreed.GroupBy(p => p.Currency).ToDictionary(g => g.Key, g => g.Sum(p => p.Price));

        var priced = agreed.Where(p => p.Price > 0).ToList();
        var stats = new OverviewStats(avg, volume, done.Count,
            priced.Count(p => fin[p.Id].Status is PaymentStatus.Paid or PaymentStatus.Overpaid),
            priced.Count(p => fin[p.Id].Status == PaymentStatus.PartiallyPaid),
            priced.Count(p => fin[p.Id].Status is PaymentStatus.Unpaid or PaymentStatus.Refunded));

        var top = projects
            .Select(p =>
            {
                var f = FinanceCalculator.ForProject(p.Price, p.Currency, p.Payments.Select(x => x.ToRecord()));
                var delivered = p.Deliveries.Where(d => d.ReopenedAt is null).OrderByDescending(d => d.DeliveredAt)
                    .Select(d => (DateOnly?)DateOnly.FromDateTime(d.DeliveredAt)).FirstOrDefault();
                return new TopProject(p.Id, p.Name, p.Category?.Name ?? "", p.Category?.Color ?? "#6B7385", p.Price, f.Received,
                    p.Currency, delivered, p.Price > 0 ? f.Received * 100m / p.Price : 0m);
            })
            .Where(t => t.Received > 0)
            .OrderByDescending(t => t.Received).Take(10).ToList();

        var expected = agreed.Where(p => fin[p.Id].Remaining > 0).Select(p =>
        {
            var f = fin[p.Id];
            int? days = p.Deadline.HasValue ? p.Deadline.Value.DayNumber - today.DayNumber : null;
            var status = days < 0 ? ExpectedStatus.Overdue
                : days <= 7 ? ExpectedStatus.Soon
                : f.Received > 0 ? ExpectedStatus.PartiallyReceived
                : ExpectedStatus.Expected;
            return new ExpectedPayment(p.Id, p.Name, p.ClientName, f.Remaining, p.Currency, p.Deadline, days, status);
        }).OrderBy(e => e.Due ?? DateOnly.MaxValue).ToList();

        return new FinanceOverview(stats, top, expected);
    }
}
