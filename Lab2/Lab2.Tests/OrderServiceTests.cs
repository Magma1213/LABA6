using FluentAssertions;
using Lab2.Core;

namespace Lab2.Tests;

public class OrderServiceTests
{
    private readonly OrderService _service = new();

    private static List<OrderItem> Single(decimal price, int quantity = 1) =>
        new() { new OrderItem("Товар", price, quantity) };

    [Fact]
    public void CalculateTotal_WithNullItems_ThrowsArgumentException()
    {
        // ARRANGE
        IReadOnlyList<OrderItem> items = null!;

        // ACT
        Action act = () => _service.CalculateTotal(items, false, null);

        // ASSERT
        act.Should().Throw<ArgumentException>("список товаров обязателен");
    }

    [Fact]
    public void CalculateTotal_WithEmptyItems_ThrowsArgumentException()
    {
        // ARRANGE
        var items = new List<OrderItem>();

        // ACT
        Action act = () => _service.CalculateTotal(items, false, null);

        // ASSERT
        act.Should().Throw<ArgumentException>("пустой заказ недопустим");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.50)]
    public void CalculateTotal_WithNonPositivePrice_ThrowsArgumentException(double price)
    {
        // ARRANGE
        var items = Single((decimal)price);

        // ACT
        Action act = () => _service.CalculateTotal(items, false, null);

        // ASSERT
        act.Should().Throw<ArgumentException>("цена должна быть строго больше 0");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void CalculateTotal_WithNonPositiveQuantity_ThrowsArgumentException(int quantity)
    {
        // ARRANGE
        var items = Single(100m, quantity);

        // ACT
        Action act = () => _service.CalculateTotal(items, false, null);

        // ASSERT
        act.Should().Throw<ArgumentException>("количество должно быть строго больше 0");
    }

    [Fact]
    public void CalculateTotal_WithOneInvalidItemAmongValid_ThrowsArgumentException()
    {
        // ARRANGE
        var items = new List<OrderItem>
        {
            new("Книга", 500m, 1),
            new("Брак", -5m, 1)
        };

        // ACT
        Action act = () => _service.CalculateTotal(items, false, null);

        // ASSERT
        act.Should().Throw<ArgumentException>("проверяется каждый товар списка");
    }

    [Fact]
    public void CalculateTotal_WithMinimalValidOrder_ReturnsBaseAmountPlusDelivery()
    {
        // ARRANGE
        var items = Single(1m);

        // ACT
        var result = _service.CalculateTotal(items, false, null);

        // ASSERT
        result.BaseAmount.Should().Be(1m);
        result.DiscountPercent.Should().Be(0m);
        result.DeliveryCost.Should().Be(300m, "сумма меньше 3000 руб.");
        result.FinalAmount.Should().Be(301m);
    }

    [Fact]
    public void CalculateTotal_WithSeveralItems_SumsPriceTimesQuantity()
    {
        // ARRANGE
        var items = new List<OrderItem>
        {
            new("Мышь", 1000m, 2),
            new("Клавиатура", 1500m, 1),
            new("Коврик", 250m, 4)
        };

        // ACT
        var result = _service.CalculateTotal(items, false, null);

        // ASSERT
        result.BaseAmount.Should().Be(4500m, "2*1000 + 1*1500 + 4*250");
        result.FinalAmount.Should().Be(4500m, "доставка бесплатна, скидки нет");
    }

    [Theory]
    [InlineData(4999, 0, 0)]
    [InlineData(5000, 5, 250)]
    [InlineData(9999, 5, 499.95)]
    [InlineData(10000, 10, 1000)]
    [InlineData(25000, 10, 2500)]
    public void CalculateTotal_AmountDiscountBoundaries_AppliesExpectedDiscount(
        double price, double expectedPercent, double expectedDiscount)
    {
        // ARRANGE
        var items = Single((decimal)price);

        // ACT
        var result = _service.CalculateTotal(items, false, null);

        // ASSERT
        result.DiscountPercent.Should().Be((decimal)expectedPercent);
        result.DiscountAmount.Should().Be((decimal)expectedDiscount);
        result.FinalAmount.Should().Be(result.BaseAmount - result.DiscountAmount,
            "доставка бесплатна при сумме от 3000 руб.");
    }

    [Fact]
    public void CalculateTotal_WithLoyalCustomerAndSmallOrder_AddsFivePercent()
    {
        // ARRANGE
        var items = Single(2000m);

        // ACT
        var result = _service.CalculateTotal(items, true, null);

        // ASSERT
        result.DiscountPercent.Should().Be(5m);
        result.DiscountAmount.Should().Be(100m);
        result.DeliveryCost.Should().Be(300m, "после скидки 1900 руб. < 3000 руб.");
        result.FinalAmount.Should().Be(2200m);
    }

    [Fact]
    public void CalculateTotal_WithLoyalCustomerAndLargeOrder_SumsLoyaltyWithAmountDiscount()
    {
        // ARRANGE
        var items = Single(10000m);

        // ACT
        var result = _service.CalculateTotal(items, true, null);

        // ASSERT
        result.DiscountPercent.Should().Be(15m, "10% по сумме + 5% лояльность суммируются");
        result.FinalAmount.Should().Be(8500m);
    }

    [Fact]
    public void CalculateTotal_WithWelcome10ForNewCustomer_AppliesTenPercent()
    {
        // ARRANGE
        var items = Single(1000m);

        // ACT
        var result = _service.CalculateTotal(items, false, "WELCOME10");

        // ASSERT
        result.DiscountPercent.Should().Be(10m);
        result.FinalAmount.Should().Be(1200m, "900 руб. + доставка 300 руб.");
    }

    [Fact]
    public void CalculateTotal_WithWelcome10ForLoyalCustomer_ThrowsInvalidOperationException()
    {
        // ARRANGE
        var items = Single(1000m);

        // ACT
        Action act = () => _service.CalculateTotal(items, true, "WELCOME10");

        // ASSERT
        act.Should().Throw<InvalidOperationException>("WELCOME10 только для новых клиентов");
    }

    [Theory]
    [InlineData(3000, 20, 2700)]
    [InlineData(3001, 20, 2700.8)]
    [InlineData(10000, 30, 7000)]
    public void CalculateTotal_WithSummer20AtOrAboveMinimum_AppliesTwentyPercent(
        double price, double expectedPercent, double expectedFinal)
    {
        // ARRANGE
        var items = Single((decimal)price);

        // ACT
        var result = _service.CalculateTotal(items, false, "SUMMER20");

        // ASSERT
        result.DiscountPercent.Should().Be((decimal)expectedPercent,
            "SUMMER20 (20%) суммируется со скидкой по сумме (10% при 10000)");
        result.FinalAmount.Should().Be((decimal)expectedFinal);
    }

    [Fact]
    public void CalculateTotal_WithSummer20BelowMinimum_ThrowsInvalidOperationException()
    {
        // ARRANGE
        var items = Single(2999.99m);

        // ACT
        Action act = () => _service.CalculateTotal(items, false, "SUMMER20");

        // ASSERT
        act.Should().Throw<InvalidOperationException>("SUMMER20 действует при сумме от 3000 руб.");
    }

    [Fact]
    public void CalculateTotal_WithVip30ForLoyalCustomer_AppliesThirtyPlusLoyaltyPercent()
    {
        // ARRANGE
        var items = Single(2000m);

        // ACT
        var result = _service.CalculateTotal(items, true, "VIP30");

        // ASSERT
        result.DiscountPercent.Should().Be(35m, "30% промокод + 5% лояльность");
        result.FinalAmount.Should().Be(1600m, "1300 руб. + доставка 300 руб.");
    }

    [Fact]
    public void CalculateTotal_WithVip30ForNewCustomer_ThrowsInvalidOperationException()
    {
        // ARRANGE
        var items = Single(2000m);

        // ACT
        Action act = () => _service.CalculateTotal(items, false, "VIP30");

        // ASSERT
        act.Should().Throw<InvalidOperationException>("VIP30 только для постоянных клиентов");
    }

    [Theory]
    [InlineData("BLACKFRIDAY")]
    [InlineData("welcome10")]
    [InlineData("WELCOME 10")]
    public void CalculateTotal_WithUnknownPromoCode_ThrowsInvalidOperationException(string promo)
    {
        // ARRANGE
        var items = Single(1000m);

        // ACT
        Action act = () => _service.CalculateTotal(items, false, promo);

        // ASSERT
        act.Should().Throw<InvalidOperationException>("промокод не входит в список действующих");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CalculateTotal_WithEmptyPromoCode_AppliesNoPromoDiscount(string? promo)
    {
        // ARRANGE
        var items = Single(1000m);

        // ACT
        var result = _service.CalculateTotal(items, false, promo);

        // ASSERT
        result.DiscountPercent.Should().Be(0m, "пустой промокод означает отсутствие промокода");
        result.FinalAmount.Should().Be(1300m);
    }

    [Theory]
    [InlineData(2999.99, 300, 3299.99)]
    [InlineData(3000, 0, 3000)]
    [InlineData(3000.01, 0, 3000.01)]
    public void CalculateTotal_DeliveryBoundaryWithoutDiscounts_ChargesExpectedDelivery(
        double price, double expectedDelivery, double expectedFinal)
    {
        // ARRANGE
        var items = Single((decimal)price);

        // ACT
        var result = _service.CalculateTotal(items, false, null);

        // ASSERT
        result.DeliveryCost.Should().Be((decimal)expectedDelivery);
        result.FinalAmount.Should().Be((decimal)expectedFinal);
    }

    [Theory]
    [InlineData(3750, 0, 3000)]
    [InlineData(3749, 300, 3299.2)]
    public void CalculateTotal_DeliveryThresholdIsCheckedAfterDiscounts_ChargesExpectedDelivery(
        double price, double expectedDelivery, double expectedFinal)
    {
        // ARRANGE
        var items = Single((decimal)price);

        // ACT
        var result = _service.CalculateTotal(items, false, "SUMMER20");

        // ASSERT
        result.DeliveryCost.Should().Be((decimal)expectedDelivery,
            "порог 3000 руб. сравнивается с суммой после скидок");
        result.FinalAmount.Should().Be((decimal)expectedFinal);
    }

    [Fact]
    public void CalculateTotal_WithAllDiscountsCombined_SumsAllPercentages()
    {
        // ARRANGE
        var items = new List<OrderItem>
        {
            new("Ноутбук", 8000m, 1),
            new("Мышь", 1000m, 3)
        };

        // ACT
        var result = _service.CalculateTotal(items, true, "VIP30");

        // ASSERT
        result.BaseAmount.Should().Be(11000m);
        result.DiscountPercent.Should().Be(45m, "10% по сумме + 5% лояльность + 30% промокод");
        result.DiscountAmount.Should().Be(4950m);
        result.DeliveryCost.Should().Be(0m);
        result.FinalAmount.Should().Be(6050m);
    }

    [Fact]
    public void CalculateTotal_WithManyIdenticalItems_ReturnsConsistentResult()
    {
        // ARRANGE
        var items = Single(10m, 1000);

        // ACT
        var result = _service.CalculateTotal(items, false, null);

        // ASSERT
        result.BaseAmount.Should().Be(10000m);
        result.DiscountPercent.Should().Be(10m);
        result.FinalAmount.Should().Be(9000m);
        result.FinalAmount.Should().BeGreaterOrEqualTo(0m, "итоговая стоимость не бывает отрицательной");
    }
}
