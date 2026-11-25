using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public enum ChartPeriod { Month, Last6, Last12, All }

public sealed record CategoryShare(int Id, string Name, string Color, decimal Amount, decimal Percent);

public sealed record DashboardStats(
    int TotalProjects, int Active, int Completed, int OnHold,
    IReadOnlyDictionary<string, decimal> Potential,    // стоимость активных, ещё не сданных заказов
    IReadOnlyDictionary<string, decimal> Earned,       // фактически полученные деньги за всё время
    IReadOnlyDictionary<string, decimal> Pending,      // остаток к оплате по согласованным заказам
    IReadOnlyDictionary<string, decimal> MonthIncome,  // получено в текущем месяце
    IReadOnlyList<CategoryShare> Categories,           // заработок по категориям за период (одна валюта)
    string ChartCurrency);

public static class DashboardService
{
    static readonly ProjectStatus[] ActiveStatuses =
        { ProjectStatus.WaitingStart, ProjectStatus.InProgress, ProjectStatus.InReview };

    /// <summary>Все суммы раздельно по валютам. Считается в памяти: для личной базы это быстро и точно.</summary>
    public static async Task<DashboardStats> LoadAsync(PadDbContext db, DateOnly today,
        ChartPeriod period = ChartPeriod.All, CancellationToken ct = default)
    {
        var projects = await db.Projects.AsNoTracking().Include(p => p.Payments).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);

        var from = PeriodStart(today, period);

        var potential = new Dictionary<string, decimal>();
        var earned = new Dictionary<string, decimal>();
        var pending = new Dictionary<string, decimal>();
        var month = new Dictionary<string, decimal>();
        var byCategory = new Dictionary<(string Currency, int CategoryId), decimal>();

        foreach (var p in projects)
        {
            var records = p.Payments.Select(x => x.ToRecord()).ToList();

            foreach (var r in records.Where(r => r.IsReceived))
            {
                var signed = FinanceCalculator.SignedAmount(r);
                Add(earned, r.Currency, signed);
                if (r.ReceivedDate.Year == today.Year && r.ReceivedDate.Month == today.Month)
                    Add(month, r.Currency, signed);
                if (from is null || r.ReceivedDate >= from)
                {
                    var key = (r.Currency, p.CategoryId);
                    byCategory[key] = byCategory.GetValueOrDefault(key) + signed;
                }
            }

            if (ActiveStatuses.Contains(p.Status))
                Add(potential, p.Currency, p.Price);

            if (p.Status is not (ProjectStatus.Planned or ProjectStatus.Cancelled))
                Add(pending, p.Currency, FinanceCalculator.ForProject(p.Price, p.Currency, records).Remaining);
        }

        // Диаграмма строится в одной валюте: EUR, если в ней есть доход, иначе в той, где дохода больше.
        var totalsByCurrency = byCategory
            .GroupBy(kv => kv.Key.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(kv => Math.Max(kv.Value, 0m)));
        var chartCurrency =
            totalsByCurrency.GetValueOrDefault("EUR") > 0 ? "EUR" :
            totalsByCurrency.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key).FirstOrDefault() ?? "EUR";

        var amounts = categories.Select(c => (c, amount: Math.Max(byCategory.GetValueOrDefault((chartCurrency, c.Id)), 0m))).ToList();
        var total = amounts.Sum(x => x.amount);
        var shares = amounts
            .Select(x => new CategoryShare(x.c.Id, x.c.Name, x.c.Color, x.amount, total > 0 ? x.amount * 100m / total : 0m))
            .OrderByDescending(x => x.Amount).ThenBy(x => x.Name)
            .ToList();

        var live = projects.Where(p => !p.IsArchived).ToList();
        return new DashboardStats(
            live.Count,
            live.Count(p => ActiveStatuses.Contains(p.Status)),
            live.Count(p => p.Status == ProjectStatus.Completed),
            live.Count(p => p.Status == ProjectStatus.OnHold),
            Clean(potential), Clean(earned), Clean(pending), Clean(month),
            shares, chartCurrency);
    }

    public static DateOnly? PeriodStart(DateOnly today, ChartPeriod period)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return period switch
        {
            ChartPeriod.Month => monthStart,
            ChartPeriod.Last6 => monthStart.AddMonths(-5),
            ChartPeriod.Last12 => monthStart.AddMonths(-11),
            _ => null,
        };
    }

    static void Add(Dictionary<string, decimal> d, string cur, decimal v) => d[cur] = d.GetValueOrDefault(cur) + v;

    static IReadOnlyDictionary<string, decimal> Clean(Dictionary<string, decimal> d) =>
        d.Where(kv => kv.Value != 0m).ToDictionary(kv => kv.Key, kv => kv.Value);
}
