namespace Lab2.Core;

public class OrderService
{
    private const int Threshold5Percent = 5000;
    private const int Threshold10Percent = 10000;
    private const int SummerPromoMinAmount = 3000;
    private const int FreeDeliveryThreshold = 3000;
    private const int DeliveryCost = 300;

    public OrderResult CalculateTotal(IReadOnlyList<OrderItem> items, bool isLoyalCustomer, string? promoCode)
    {
        Validate(items);

        decimal baseAmount = items.Sum(i => i.Price * i.Quantity);

        decimal discountPercent = GetAmountDiscountPercent(baseAmount);

        if (isLoyalCustomer)
        {
            discountPercent += 5m;
        }

        discountPercent += GetPromoDiscountPercent(promoCode, baseAmount, isLoyalCustomer);

        decimal discountAmount = baseAmount * discountPercent / 100m;
        decimal amountAfterDiscount = baseAmount - discountAmount;

        decimal delivery = amountAfterDiscount >= FreeDeliveryThreshold ? 0m : DeliveryCost;

        decimal finalAmount = Math.Max(0m, amountAfterDiscount + delivery);

        return new OrderResult(baseAmount, discountPercent, discountAmount, delivery, finalAmount);
    }

    private static void Validate(IReadOnlyList<OrderItem> items)
    {
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("Список товаров не должен быть пустым.", nameof(items));
        }

        foreach (var item in items)
        {
            if (item.Price <= 0)
            {
                throw new ArgumentException($"Цена товара «{item.Name}» должна быть больше 0.", nameof(items));
            }

            if (item.Quantity <= 0)
            {
                throw new ArgumentException($"Количество товара «{item.Name}» должно быть больше 0.", nameof(items));
            }
        }
    }

    private static decimal GetAmountDiscountPercent(decimal baseAmount)
    {
        if (baseAmount >= Threshold10Percent)
        {
            return 10m;
        }

        if (baseAmount >= Threshold5Percent)
        {
            return 5m;
        }

        return 0m;
    }

    private static decimal GetPromoDiscountPercent(string? promoCode, decimal baseAmount, bool isLoyalCustomer)
    {
        if (string.IsNullOrWhiteSpace(promoCode))
        {
            return 0m;
        }

        switch (promoCode.Trim().ToUpperInvariant())
        {
            case "WELCOME10":
                if (isLoyalCustomer)
                {
                    throw new InvalidOperationException("Промокод WELCOME10 действует только для новых клиентов.");
                }
                return 10m;

            case "SUMMER20":
                if (baseAmount < SummerPromoMinAmount)
                {
                    throw new InvalidOperationException("Промокод SUMMER20 действует при сумме заказа от 3 000 руб.");
                }
                return 20m;

            case "VIP30":
                if (!isLoyalCustomer)
                {
                    throw new InvalidOperationException("Промокод VIP30 действует только для постоянных клиентов.");
                }
                return 30m;

            default:
                throw new InvalidOperationException($"Промокод «{promoCode}» недействителен.");
        }
    }
}
