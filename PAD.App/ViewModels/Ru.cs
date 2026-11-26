using PAD.Domain;

namespace PAD.Desktop.ViewModels;

/// <summary>Русские названия и цвета статусов.</summary>
public static class Ru
{
    public static string Status(ProjectStatus s) => s switch
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

    public static string StatusColor(ProjectStatus s) => s switch
    {
        ProjectStatus.InProgress => "#3B9EF0",
        ProjectStatus.WaitingStart => "#9B8AFB",
        ProjectStatus.OnHold => "#F5A524",
        ProjectStatus.InReview => "#22B8CF",
        ProjectStatus.Delivered => "#19C3A6",
        ProjectStatus.Completed => "#34D399",
        ProjectStatus.Cancelled => "#F26D6D",
        _ => "#8A929E",
    };

    public static string Pay(PaymentStatus s) => s switch
    {
        PaymentStatus.Unpaid => "Не оплачен",
        PaymentStatus.PartiallyPaid => "Частично оплачен",
        PaymentStatus.Paid => "Оплачен",
        PaymentStatus.Overpaid => "Переплата",
        _ => "Возврат",
    };

    public static string PayColor(PaymentStatus s) => s switch
    {
        PaymentStatus.Paid => "#34D399",
        PaymentStatus.PartiallyPaid => "#F5A524",
        PaymentStatus.Overpaid => "#9B8AFB",
        PaymentStatus.Refunded => "#F26D6D",
        _ => "#8A929E",
    };

    public static string PayType(PaymentType t) => t switch
    {
        PaymentType.Prepayment => "Предоплата",
        PaymentType.Intermediate => "Промежуточный платёж",
        PaymentType.Final => "Финальная оплата",
        PaymentType.Additional => "Дополнительная оплата",
        _ => "Возврат средств",
    };
}
