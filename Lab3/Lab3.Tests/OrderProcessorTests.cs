using FluentAssertions;
using Lab3.Core;
using Moq;

namespace Lab3.Tests;

public static class OrderMother
{
    public static Order NewOrder(int id = 1, params OrderItem[] items) => new()
    {
        Id = id,
        Items = items.Length == 0
            ? new List<OrderItem> { new("SKU-1", 2, 500m), new("SKU-2", 1, 250m) }
            : items.ToList()
    };

    public static Order WithStatus(OrderStatus status, int id = 1)
    {
        var order = NewOrder(id);
        order.Status = status;
        return order;
    }
}

public class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<int, Order> _store = new();

    public int SaveCount { get; private set; }

    public Order? GetById(int orderId) => _store.TryGetValue(orderId, out var order) ? order : null;

    public void Save(Order order)
    {
        _store[order.Id] = order;
        SaveCount++;
    }
}

public class OrderProcessorTests
{
    private static readonly DateTime FixedNow = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    private readonly Mock<IOrderRepository> _repository = new();
    private readonly Mock<IInventoryService> _inventory = new();
    private readonly Mock<IPaymentGateway> _payments = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public OrderProcessorTests()
    {
        _inventory.Setup(i => i.ReserveItems(It.IsAny<Order>())).Returns(true);
        _payments.Setup(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>())).Returns(new PaymentResult(true));
        _clock.Setup(c => c.UtcNow).Returns(FixedNow);
    }

    private OrderProcessor CreateSut() =>
        new(_repository.Object, _inventory.Object, _payments.Object, _notifications.Object, _clock.Object);

    private Order GivenOrderInRepository(Order order)
    {
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        return order;
    }

    // ---------- Успешная обработка ----------

    [Fact]
    public void ProcessOrder_WithValidOrder_CompletesOrderAndReturnsIt()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        var sut = CreateSut();

        // ===== ACT =====
        var result = sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        result.Should().BeSameAs(order);
        result.Status.Should().Be(OrderStatus.Completed, "оплаченный заказ с резервом получает статус Completed");
    }

    [Fact]
    public void ProcessOrder_WithValidOrder_SavesCompletedOrderOnce()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        _repository.Verify(r => r.Save(It.Is<Order>(o => o.Id == order.Id && o.Status == OrderStatus.Completed)), Times.Once());
    }

    [Fact]
    public void ProcessOrder_WithValidOrder_ChargesTotalAmountExactlyOnce()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        _payments.Verify(p => p.Charge(order.Id, 1250m), Times.Once(), "2 * 500 + 1 * 250 = 1250 руб.");
    }

    [Fact]
    public void ProcessOrder_WithValidOrder_SendsConfirmationWithDateFromProvider()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        _notifications.Verify(n => n.SendConfirmation(order, FixedNow), Times.Once(),
            "в уведомлении используется текущая дата из IDateTimeProvider");
        _clock.Verify(c => c.UtcNow, Times.AtLeastOnce());
    }

    [Fact]
    public void ProcessOrder_WithValidOrder_ReservesItemsAndDoesNotReleaseThem()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        _inventory.Verify(i => i.ReserveItems(order), Times.Once());
        _inventory.Verify(i => i.ReleaseItems(It.IsAny<Order>()), Times.Never(), "при успехе резерв не откатывается");
    }

    // ---------- Заказ не найден / уже обработан ----------

    [Fact]
    public void ProcessOrder_WhenOrderNotFound_ThrowsAndTouchesNoOtherDependency()
    {
        // ===== ARRANGE =====
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        var payments = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        _repository.Setup(r => r.GetById(404)).Returns((Order?)null);
        var sut = new OrderProcessor(_repository.Object, inventory.Object, payments.Object, notifications.Object, _clock.Object);

        // ===== ACT =====
        Action act = () => sut.ProcessOrder(404);

        // ===== ASSERT =====
        act.Should().Throw<OrderProcessingException>().WithMessage("*404*",
            "любой вызов strict-mock без Setup привёл бы к другому исключению");
        _repository.Verify(r => r.Save(It.IsAny<Order>()), Times.Never());
    }

    [Theory]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.PaymentFailed)]
    public void ProcessOrder_WhenOrderAlreadyProcessed_ThrowsAndDoesNotReserveOrSave(OrderStatus status)
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.WithStatus(status));
        var sut = CreateSut();

        // ===== ACT =====
        Action act = () => sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        act.Should().Throw<OrderProcessingException>();
        _inventory.Verify(i => i.ReserveItems(It.IsAny<Order>()), Times.Never());
        _repository.Verify(r => r.Save(It.IsAny<Order>()), Times.Never());
        order.Status.Should().Be(status, "статус уже обработанного заказа не меняется");
    }

    // ---------- Нет товаров на складе ----------

    [Fact]
    public void ProcessOrder_WhenStockIsInsufficient_ThrowsInsufficientStockException()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        _inventory.Setup(i => i.ReserveItems(order)).Returns(false);
        var sut = CreateSut();

        // ===== ACT =====
        Action act = () => sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        act.Should().Throw<InsufficientStockException>();
    }

    [Fact]
    public void ProcessOrder_WhenStockIsInsufficient_ReleasesReservationAndMarksOrderFailed()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        _inventory.Setup(i => i.ReserveItems(order)).Returns(false);
        var sut = CreateSut();

        // ===== ACT =====
        Action act = () => sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        act.Should().Throw<InsufficientStockException>();
        order.Status.Should().Be(OrderStatus.Failed);
        _inventory.Verify(i => i.ReleaseItems(order), Times.Once(), "резерв должен быть откачен");
        _repository.Verify(r => r.Save(It.Is<Order>(o => o.Status == OrderStatus.Failed)), Times.Once());
    }

    [Fact]
    public void ProcessOrder_WhenStockIsInsufficient_DoesNotChargeAndDoesNotNotify()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        _inventory.Setup(i => i.ReserveItems(order)).Returns(false);
        var sut = CreateSut();

        // ===== ACT =====
        Action act = () => sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        act.Should().Throw<InsufficientStockException>();
        _payments.Verify(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>()), Times.Never());
        _notifications.Verify(n => n.SendConfirmation(It.IsAny<Order>(), It.IsAny<DateTime>()), Times.Never());
    }

    // ---------- Сбой платежа ----------

    [Fact]
    public void ProcessOrder_WhenPaymentFails_ReturnsOrderWithPaymentFailedStatus()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        _payments.Setup(p => p.Charge(order.Id, It.IsAny<decimal>())).Returns(new PaymentResult(false, "Недостаточно средств"));
        var sut = CreateSut();

        // ===== ACT =====
        var result = sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        result.Status.Should().Be(OrderStatus.PaymentFailed, "при сбое платежа исключение не выбрасывается");
    }

    [Fact]
    public void ProcessOrder_WhenPaymentFails_ReleasesReservationAndSavesOrder()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        _payments.Setup(p => p.Charge(order.Id, It.IsAny<decimal>())).Returns(new PaymentResult(false));
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        _inventory.Verify(i => i.ReleaseItems(order), Times.Once());
        _repository.Verify(r => r.Save(It.Is<Order>(o => o.Status == OrderStatus.PaymentFailed)), Times.Once());
    }

    [Fact]
    public void ProcessOrder_WhenPaymentFails_DoesNotSendConfirmation()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        _payments.Setup(p => p.Charge(order.Id, It.IsAny<decimal>())).Returns(new PaymentResult(false));
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        _notifications.Verify(n => n.SendConfirmation(It.IsAny<Order>(), It.IsAny<DateTime>()), Times.Never(),
            "при сбое платежа SendConfirmation не вызывается");
    }

    // ---------- Strict Mock ----------

    [Fact]
    public void ProcessOrder_HappyPathWithStrictMocks_MakesOnlyExpectedCalls()
    {
        // ===== ARRANGE =====
        var order = OrderMother.NewOrder();
        var repository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        var payments = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        repository.Setup(r => r.GetById(order.Id)).Returns(order);
        repository.Setup(r => r.Save(order));
        inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        payments.Setup(p => p.Charge(order.Id, 1250m)).Returns(new PaymentResult(true));
        notifications.Setup(n => n.SendConfirmation(order, FixedNow));
        var sut = new OrderProcessor(repository.Object, inventory.Object, payments.Object, notifications.Object, _clock.Object);

        // ===== ACT =====
        var result = sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        result.Status.Should().Be(OrderStatus.Completed);
        repository.VerifyAll();
        inventory.VerifyAll();
        payments.VerifyAll();
        notifications.VerifyAll();
        inventory.Verify(i => i.ReleaseItems(It.IsAny<Order>()), Times.Never());
    }

    [Fact]
    public void ProcessOrder_PaymentFailureWithStrictMocks_NeverNotifiesCustomer()
    {
        // ===== ARRANGE =====
        var order = OrderMother.NewOrder();
        var repository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        var payments = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        repository.Setup(r => r.GetById(order.Id)).Returns(order);
        repository.Setup(r => r.Save(order));
        inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        inventory.Setup(i => i.ReleaseItems(order));
        payments.Setup(p => p.Charge(order.Id, 1250m)).Returns(new PaymentResult(false, "Отказ банка"));
        var sut = new OrderProcessor(repository.Object, inventory.Object, payments.Object, notifications.Object, _clock.Object);

        // ===== ACT =====
        var result = sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        result.Status.Should().Be(OrderStatus.PaymentFailed, "для notifications не задан Setup, любой вызов упал бы с исключением");
        inventory.Verify(i => i.ReleaseItems(order), Times.Once());
    }

    // ---------- Callback и матчеры ----------

    [Fact]
    public void ProcessOrder_Charge_PassesOrderIdAndAmountCapturedByCallback()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder(id: 7));
        int? chargedOrderId = null;
        decimal? chargedAmount = null;
        _payments.Setup(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>()))
            .Callback<int, decimal>((id, amount) => { chargedOrderId = id; chargedAmount = amount; })
            .Returns(new PaymentResult(true));
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        chargedOrderId.Should().Be(7);
        chargedAmount.Should().Be(order.TotalAmount);
    }

    public static IEnumerable<object[]> ItemSets => new List<object[]>
    {
        new object[] { new[] { new OrderItem("A", 1, 100m) }, 100m },
        new object[] { new[] { new OrderItem("A", 3, 100m), new OrderItem("B", 2, 50.5m) }, 401m },
        new object[] { new[] { new OrderItem("A", 10, 0.99m) }, 9.9m }
    };

    [Theory]
    [MemberData(nameof(ItemSets))]
    public void ProcessOrder_WithDifferentItemSets_ChargesSumOfPriceTimesQuantity(OrderItem[] items, decimal expectedAmount)
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder(5, items));
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        _payments.Verify(p => p.Charge(5, It.Is<decimal>(a => a == expectedAmount)), Times.Once());
    }

    [Fact]
    public void ProcessOrder_Always_RequestsOrderFromRepositoryByGivenId()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder(id: 42));
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(42);

        // ===== ASSERT =====
        _repository.Verify(r => r.GetById(It.Is<int>(id => id == 42)), Times.Once());
        _inventory.Verify(i => i.ReserveItems(It.Is<Order>(o => o.Id == 42)), Times.Once());
    }

    // ---------- Исключения зависимостей (Throws) ----------

    [Fact]
    public void ProcessOrder_WhenRepositoryThrows_PropagatesExceptionAndStopsProcessing()
    {
        // ===== ARRANGE =====
        _repository.Setup(r => r.GetById(It.IsAny<int>())).Throws(new InvalidOperationException("БД недоступна"));
        var sut = CreateSut();

        // ===== ACT =====
        Action act = () => sut.ProcessOrder(1);

        // ===== ASSERT =====
        act.Should().Throw<InvalidOperationException>().WithMessage("БД недоступна");
        _inventory.Verify(i => i.ReserveItems(It.IsAny<Order>()), Times.Never());
    }

    [Fact]
    public void ProcessOrder_WhenInventoryThrows_PropagatesExceptionAndDoesNotCharge()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        _inventory.Setup(i => i.ReserveItems(order)).Throws(new TimeoutException("склад не отвечает"));
        var sut = CreateSut();

        // ===== ACT =====
        Action act = () => sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        act.Should().Throw<TimeoutException>();
        _payments.Verify(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>()), Times.Never());
    }

    // ---------- Fake и состояние ----------

    [Fact]
    public void ProcessOrder_WithInMemoryRepository_PersistsFinalStatus()
    {
        // ===== ARRANGE =====
        var repository = new InMemoryOrderRepository();
        repository.Save(OrderMother.NewOrder(id: 3));
        var sut = new OrderProcessor(repository, _inventory.Object, _payments.Object, _notifications.Object, _clock.Object);

        // ===== ACT =====
        sut.ProcessOrder(3);

        // ===== ASSERT =====
        repository.GetById(3)!.Status.Should().Be(OrderStatus.Completed);
        repository.SaveCount.Should().Be(2, "одно сохранение при создании заказа и одно после обработки");
    }

    [Fact]
    public void ProcessOrder_CalledTwiceForSameOrder_SecondCallThrowsAndChargesOnlyOnce()
    {
        // ===== ARRANGE =====
        var order = GivenOrderInRepository(OrderMother.NewOrder());
        var sut = CreateSut();

        // ===== ACT =====
        sut.ProcessOrder(order.Id);
        Action second = () => sut.ProcessOrder(order.Id);

        // ===== ASSERT =====
        second.Should().Throw<OrderProcessingException>("повторная обработка завершённого заказа запрещена");
        _payments.Verify(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>()), Times.Once());
    }

    // ---------- Конструктор ----------

    [Theory]
    [InlineData(0, "orders")]
    [InlineData(1, "inventory")]
    [InlineData(2, "payments")]
    [InlineData(3, "notifications")]
    [InlineData(4, "clock")]
    public void Constructor_WithNullDependency_ThrowsArgumentNullException(int nullIndex, string expectedParamName)
    {
        // ===== ARRANGE =====
        object?[] deps = { _repository.Object, _inventory.Object, _payments.Object, _notifications.Object, _clock.Object };
        deps[nullIndex] = null;

        // ===== ACT =====
        Action act = () => new OrderProcessor(
            (IOrderRepository)deps[0]!, (IInventoryService)deps[1]!, (IPaymentGateway)deps[2]!,
            (INotificationService)deps[3]!, (IDateTimeProvider)deps[4]!);

        // ===== ASSERT =====
        act.Should().Throw<ArgumentNullException>().WithParameterName(expectedParamName);
    }

    // ---------- Модели ----------

    [Fact]
    public void Order_TotalAmount_SumsPriceTimesQuantityAndIsZeroWithoutItems()
    {
        // ===== ARRANGE =====
        var empty = new Order { Id = 1 };
        var filled = OrderMother.NewOrder();

        // ===== ACT =====
        var emptyTotal = empty.TotalAmount;
        var filledTotal = filled.TotalAmount;

        // ===== ASSERT =====
        emptyTotal.Should().Be(0m);
        filledTotal.Should().Be(1250m);
        new PaymentResult(false, "x").Should().Be(new PaymentResult(false, "x"));
    }
}
