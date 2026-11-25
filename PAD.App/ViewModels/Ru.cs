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
        ProjectStatus.InProgress => "#2F5BEA",
        ProjectStatus.WaitingStart => "#6E56CF",
        ProjectStatus.OnHold => "#D98A00",
        ProjectStatus.InReview => "#00A2C7",
        ProjectStatus.Delivered => "#12A594",
        ProjectStatus.Completed => "#1E9E5A",
        ProjectStatus.Cancelled => "#D13C3C",
        _ => "#6B7385",
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
        PaymentStatus.Paid => "#1E9E5A",
        PaymentStatus.PartiallyPaid => "#D98A00",
        PaymentStatus.Overpaid => "#7A4DE0",
        PaymentStatus.Refunded => "#D13C3C",
        _ => "#6B7385",
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
