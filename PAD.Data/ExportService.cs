using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PAD.Domain;

namespace PAD.Data;

public static class ExportService
{
    /// <summary>CSV с разделителем «;» (открывается в Excel). Возвраты — отрицательные суммы.</summary>
    public static async Task<string> PaymentsCsvAsync(PadDbContext db, CancellationToken ct = default)
    {
        var pays = await db.Payments.AsNoTracking()
            .Include(p => p.Project).ThenInclude(pr => pr!.Category).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Дата;Проект;Заказчик;Категория;Тип;Сумма;Валюта;Получен;Назначение");
        foreach (var p in pays.OrderBy(p => p.ReceivedDate).ThenBy(p => p.Id))
        {
            var signed = p.Type == PaymentType.Refund ? -p.Amount : p.Amount;
            sb.AppendLine(string.Join(';',
                p.ReceivedDate.ToString("yyyy-MM-dd"),
                Csv(p.Project?.Name), Csv(p.Project?.ClientName), Csv(p.Project?.Category?.Name),
                TypeName(p.Type),
                signed.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ','),
                p.Currency, p.IsReceived ? "да" : "нет", Csv(p.Purpose)));
        }
        return sb.ToString();
    }

    static string TypeName(PaymentType t) => t switch
    {
        PaymentType.Prepayment => "Предоплата",
        PaymentType.Intermediate => "Промежуточный платёж",
        PaymentType.Final => "Финальная оплата",
        PaymentType.Additional => "Дополнительная оплата",
        _ => "Возврат средств",
    };

    static string Csv(string? s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
