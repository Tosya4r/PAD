namespace PAD.Domain;

public enum PaymentType { Prepayment, Intermediate, Final, Additional, Refund }

public enum PaymentStatus { Unpaid, PartiallyPaid, Paid, Overpaid, Refunded }

public sealed record PaymentRecord(
    int Id,
    decimal Amount,          // всегда положительное; знак определяется типом
    string Currency,
    DateOnly ReceivedDate,
    PaymentType Type,
    bool IsReceived);        // только полученные деньги считаются заработком

public sealed record ProjectFinance(
    string Currency,
    decimal Price,
    decimal Received,        // получено минус возвраты
    decimal Refunded,
    decimal Remaining,       // max(Price - Received, 0)
    PaymentStatus Status);

public static class FinanceCalculator
{
    /// <summary>Вклад платежа в заработок: + для обычных, − для возврата, 0 если не получен.</summary>
    public static decimal SignedAmount(PaymentRecord p) =>
        !p.IsReceived ? 0m : p.Type == PaymentType.Refund ? -p.Amount : p.Amount;

    /// <summary>Расчёт по одному проекту. Платежи в другой валюте не смешиваются — вызывающий обязан отфильтровать.</summary>
    public static ProjectFinance ForProject(decimal price, string currency, IEnumerable<PaymentRecord> payments)
    {
        var list = payments.Where(p => p.Currency == currency).ToList();
        var refunded = list.Where(p => p.IsReceived && p.Type == PaymentType.Refund).Sum(p => p.Amount);
        var received = list.Sum(SignedAmount);
        var remaining = Math.Max(price - received, 0m);

        PaymentStatus status;
        if (refunded > 0 && received <= 0) status = PaymentStatus.Refunded;
        else if (received <= 0) status = PaymentStatus.Unpaid;
        else if (received < price) status = PaymentStatus.PartiallyPaid;
        else if (received == price) status = PaymentStatus.Paid;
        else status = PaymentStatus.Overpaid;

        return new ProjectFinance(currency, price, received, refunded, remaining, status);
    }

    /// <summary>Заработок по месяцам по дате поступления (не по дате создания проекта). Раздельно по валютам.</summary>
    public static IReadOnlyDictionary<(string Currency, int Year, int Month), decimal> EarningsByMonth(
        IEnumerable<PaymentRecord> payments) =>
        payments.Where(p => p.IsReceived)
                .GroupBy(p => (p.Currency, p.ReceivedDate.Year, p.ReceivedDate.Month))
                .ToDictionary(g => g.Key, g => g.Sum(SignedAmount));
}
