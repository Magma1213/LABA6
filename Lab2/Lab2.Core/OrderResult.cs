namespace Lab2.Core;

public record OrderResult(
    decimal BaseAmount,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal DeliveryCost,
    decimal FinalAmount);
