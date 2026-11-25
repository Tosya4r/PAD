using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public sealed record DashboardStats(
    int TotalProjects, int Active, int Completed, int OnHold,
    IReadOnlyDictionary<string, decimal> Potential,    // стоимость активных, ещё не сданных заказов
    IReadOnlyDictionary<string, decimal> Earned,       // фактически полученные деньги за всё время
    IReadOnlyDictionary<string, decimal> Pending,      // остаток к оплате по согласованным заказам
    IReadOnlyDictionary<string, decimal> MonthIncome); // получено в текущем месяце

public static class DashboardService
{
    static readonly ProjectStatus[] ActiveStatuses =
        { ProjectStatus.WaitingStart, ProjectStatus.InProgress, ProjectStatus.InReview };

    /// <summary>Все суммы раздельно по валютам. Считается в памяти: для личной базы это быстро и точно.</summary>
    public static async Task<DashboardStats> LoadAsync(PadDbContext db, DateOnly today, CancellationToken ct = default)
    {
        var projects = await db.Projects.AsNoTracking().Include(p => p.Payments).ToListAsync(ct);

        var potential = new Dictionary<string, decimal>();
        var earned = new Dictionary<string, decimal>();
        var pending = new Dictionary<string, decimal>();
        var month = new Dictionary<string, decimal>();

        foreach (var p in projects)
        {
            var records = p.Payments.Select(x => x.ToRecord()).ToList();

            foreach (var r in records.Where(r => r.IsReceived))
            {
                var signed = FinanceCalculator.SignedAmount(r);
                Add(earned, r.Currency, signed);
                if (r.ReceivedDate.Year == today.Year && r.ReceivedDate.Month == today.Month)
                    Add(month, r.Currency, signed);
            }

            if (ActiveStatuses.Contains(p.Status))
                Add(potential, p.Currency, p.Price);

            if (p.Status is not (ProjectStatus.Planned or ProjectStatus.Cancelled))
                Add(pending, p.Currency, FinanceCalculator.ForProject(p.Price, p.Currency, records).Remaining);
        }

        var live = projects.Where(p => !p.IsArchived).ToList();
        return new DashboardStats(
            live.Count,
            live.Count(p => ActiveStatuses.Contains(p.Status)),
            live.Count(p => p.Status == ProjectStatus.Completed),
            live.Count(p => p.Status == ProjectStatus.OnHold),
            Clean(potential), Clean(earned), Clean(pending), Clean(month));
    }

    static void Add(Dictionary<string, decimal> d, string cur, decimal v) => d[cur] = d.GetValueOrDefault(cur) + v;

    static IReadOnlyDictionary<string, decimal> Clean(Dictionary<string, decimal> d) =>
        d.Where(kv => kv.Value != 0m).ToDictionary(kv => kv.Key, kv => kv.Value);
}
