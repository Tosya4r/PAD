using PAD.Domain;
using Xunit;

public class FinanceCalculatorTests
{
    static PaymentRecord P(decimal a, PaymentType t, int month = 11, bool received = true, string cur = "EUR") =>
        new(Guid.NewGuid(), a, cur, new DateOnly(2026, month, 25), t, received);

    [Fact]
    public void Example_From_Spec_1500_Price_1000_Received()
    {
        var f = FinanceCalculator.ForProject(1500m, "EUR", new[]
        {
            P(500m, PaymentType.Prepayment), P(500m, PaymentType.Intermediate)
        });
        Assert.Equal(1000m, f.Received);
        Assert.Equal(500m, f.Remaining);
        Assert.Equal(PaymentStatus.PartiallyPaid, f.Status);
    }

    [Fact]
    public void Unreceived_Payment_Is_Not_Earnings() =>
        Assert.Equal(PaymentStatus.Unpaid,
            FinanceCalculator.ForProject(1500m, "EUR", new[] { P(500m, PaymentType.Prepayment, received: false) }).Status);

    [Fact]
    public void Full_Payment_Is_Paid_And_Overpayment_Detected()
    {
        Assert.Equal(PaymentStatus.Paid,
            FinanceCalculator.ForProject(1000m, "EUR", new[] { P(1000m, PaymentType.Final) }).Status);
        Assert.Equal(PaymentStatus.Overpaid,
            FinanceCalculator.ForProject(1000m, "EUR", new[] { P(1200m, PaymentType.Final) }).Status);
    }

    [Fact]
    public void Refund_Reduces_Received()
    {
        var f = FinanceCalculator.ForProject(1000m, "EUR", new[] { P(1000m, PaymentType.Final), P(300m, PaymentType.Refund) });
        Assert.Equal(700m, f.Received);
        Assert.Equal(PaymentStatus.PartiallyPaid, f.Status);
    }

    [Fact]
    public void Currencies_Are_Not_Mixed()
    {
        var f = FinanceCalculator.ForProject(1000m, "EUR", new[] { P(1000m, PaymentType.Final, cur: "USD") });
        Assert.Equal(0m, f.Received);
    }

    [Fact]
    public void Monthly_Earnings_Use_Received_Date()
    {
        var r = FinanceCalculator.EarningsByMonth(new[] { P(500m, PaymentType.Prepayment, 11), P(1000m, PaymentType.Final, 12) });
        Assert.Equal(500m, r[("EUR", 2026, 11)]);
        Assert.Equal(1000m, r[("EUR", 2026, 12)]);
    }
}
