using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public sealed record EarningRow(int PaymentId, int ProjectId, string Project, string Client,
    int CategoryId, string Category, string CategoryColor, DateOnly Date, PaymentType Type, decimal Amount, string Currency);

public sealed record MonthEarning(int Year, int Month, decimal Amount);

/// <summary>Аналитика поступлений. Считаются только платежи с отметкой «получен», по дате поступления.</summary>
public static class FinanceService
{
    public static async Task<List<EarningRow>> EarningsAsync(PadDbContext db, string currency, DateOnly? from,
        int? categoryId = null, int? year = null, int? month = null, CancellationToken ct = default)
    {
        var pays = await db.Payments.AsNoTracking()
            .Include(p => p.Project).ThenInclude(pr => pr!.Category)
            .Where(p => p.IsReceived && p.Currency == currency).ToListAsync(ct);

        return pays
            .Where(p => from is null || p.ReceivedDate >= from)
            .Where(p => categoryId is null || p.Project!.CategoryId == categoryId)
            .Where(p => year is null || (p.ReceivedDate.Year == year && p.ReceivedDate.Month == month))
            .OrderByDescending(p => p.ReceivedDate).ThenByDescending(p => p.Id)
            .Select(p => new EarningRow(p.Id, p.ProjectId, p.Project!.Name, p.Project.ClientName,
                p.Project.CategoryId, p.Project.Category?.Name ?? "", p.Project.Category?.Color ?? "#6B7385",
                p.ReceivedDate, p.Type, FinanceCalculator.SignedAmount(p.ToRecord()), p.Currency))
            .ToList();
    }

    /// <summary>Заработок по месяцам; месяцы без поступлений включаются с нулём.</summary>
    public static async Task<List<MonthEarning>> MonthlyAsync(PadDbContext db, string currency, DateOnly today,
        ChartPeriod period, CancellationToken ct = default)
    {
        var pays = await db.Payments.AsNoTracking().Where(p => p.IsReceived && p.Currency == currency).ToListAsync(ct);

        var end = new DateOnly(today.Year, today.Month, 1);
        var count = period switch { ChartPeriod.Last12 => 12, ChartPeriod.All => 0, _ => 6 };
        if (count == 0)
        {
            count = 6;
            if (pays.Count > 0)
            {
                var first = pays.Min(p => p.ReceivedDate);
                var months = (end.Year - first.Year) * 12 + end.Month - first.Month + 1;
                count = Math.Clamp(months, 6, 36);
            }
        }

        var result = new List<MonthEarning>();
        for (var i = count - 1; i >= 0; i--)
        {
            var m = end.AddMonths(-i);
            var sum = pays.Where(p => p.ReceivedDate.Year == m.Year && p.ReceivedDate.Month == m.Month)
                          .Sum(p => FinanceCalculator.SignedAmount(p.ToRecord()));
            result.Add(new MonthEarning(m.Year, m.Month, sum));
        }
        return result;
    }
}
