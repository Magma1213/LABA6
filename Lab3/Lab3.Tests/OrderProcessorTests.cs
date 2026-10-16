using FluentAssertions;
using Lab3.Core;
using Moq;

namespace Lab3.Tests;

public static class OrderMother
{
    public static Order NewOrder(int id = 1) => new()
    {
        Id = id,
        Items = new List<OrderItem> { new("SKU-1", 2, 500m), new("SKU-2", 1, 250m) }
    };
}

public class OrderProcessorTests
{
    private static readonly DateTime FixedNow = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    private readonly Mock<IOrderRepository> _repository = new();
    private readonly Mock<IInventoryService> _inventory = new();
    private readonly Mock<IPaymentGateway> _payments = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly OrderProcessor _sut;

    public OrderProcessorTests()
    {
        _sut = new OrderProcessor(
            _repository.Object, _inventory.Object, _payments.Object, _notifications.Object, _clock.Object);
    }

    [Fact]
    public void ProcessOrder_WithValidOrder_CompletesOrderAndReturnsIt()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(order.Id, order.TotalAmount)).Returns(new PaymentResult(true));

        // ACT
        var result = _sut.ProcessOrder(order.Id);

        // ASSERT
        result.Should().BeSameAs(order);
        result.Status.Should().Be(OrderStatus.Completed, "оплаченный заказ с резервом получает статус Completed");
    }

    [Fact]
    public void ProcessOrder_WithValidOrder_SavesCompletedOrderOnce()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(order.Id, order.TotalAmount)).Returns(new PaymentResult(true));

        // ACT
        _sut.ProcessOrder(order.Id);

        // ASSERT
        _repository.Verify(r => r.Save(It.Is<Order>(o => o.Id == order.Id && o.Status == OrderStatus.Completed)), Times.Once());
    }

    [Fact]
    public void ProcessOrder_WithValidOrder_ChargesTotalAmountExactlyOnce()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>())).Returns(new PaymentResult(true));

        // ACT
        _sut.ProcessOrder(order.Id);

        // ASSERT
        _payments.Verify(p => p.Charge(order.Id, 1250m), Times.Once(), "2 * 500 + 1 * 250 = 1250 руб.");
    }

    [Fact]
    public void ProcessOrder_WithValidOrder_SendsConfirmationWithDateFromProvider()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(order.Id, order.TotalAmount)).Returns(new PaymentResult(true));
        _clock.Setup(c => c.UtcNow).Returns(FixedNow);

        // ACT
        _sut.ProcessOrder(order.Id);

        // ASSERT
        _notifications.Verify(n => n.SendConfirmation(order, FixedNow), Times.Once(),
            "в уведомлении используется текущая дата из IDateTimeProvider");
    }

    [Fact]
    public void ProcessOrder_WithValidOrder_ReservesItemsAndDoesNotReleaseThem()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(order.Id, order.TotalAmount)).Returns(new PaymentResult(true));

        // ACT
        _sut.ProcessOrder(order.Id);

        // ASSERT
        _inventory.Verify(i => i.ReserveItems(order), Times.Once());
        _inventory.Verify(i => i.ReleaseItems(It.IsAny<Order>()), Times.Never(), "при успехе резерв не откатывается");
    }

    [Fact]
    public void ProcessOrder_WhenOrderNotFound_ThrowsAndTouchesNoOtherDependency()
    {
        // ARRANGE
        var inventory = new Mock<IInventoryService>(MockBehavior.Strict);
        var payments = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        _repository.Setup(r => r.GetById(404)).Returns((Order?)null);
        var sut = new OrderProcessor(_repository.Object, inventory.Object, payments.Object, notifications.Object, _clock.Object);

        // ACT
        Action act = () => sut.ProcessOrder(404);

        // ASSERT
        act.Should().Throw<OrderProcessingException>("любой вызов strict-mock без Setup привёл бы к другому исключению");
        _repository.Verify(r => r.Save(It.IsAny<Order>()), Times.Never());
    }

    [Theory]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.PaymentFailed)]
    public void ProcessOrder_WhenOrderAlreadyProcessed_ThrowsAndDoesNotReserveOrSave(OrderStatus status)
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        order.Status = status;
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);

        // ACT
        Action act = () => _sut.ProcessOrder(order.Id);

        // ASSERT
        act.Should().Throw<OrderProcessingException>();
        _inventory.Verify(i => i.ReserveItems(It.IsAny<Order>()), Times.Never());
        _repository.Verify(r => r.Save(It.IsAny<Order>()), Times.Never());
        order.Status.Should().Be(status, "статус уже обработанного заказа не меняется");
    }

    [Fact]
    public void ProcessOrder_WhenStockIsInsufficient_ThrowsInsufficientStockException()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(false);

        // ACT
        Action act = () => _sut.ProcessOrder(order.Id);

        // ASSERT
        act.Should().Throw<InsufficientStockException>();
    }

    [Fact]
    public void ProcessOrder_WhenStockIsInsufficient_ReleasesReservationAndMarksOrderFailed()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(false);

        // ACT
        Action act = () => _sut.ProcessOrder(order.Id);

        // ASSERT
        act.Should().Throw<InsufficientStockException>();
        order.Status.Should().Be(OrderStatus.Failed);
        _inventory.Verify(i => i.ReleaseItems(order), Times.Once(), "резерв должен быть откачен");
        _repository.Verify(r => r.Save(It.Is<Order>(o => o.Status == OrderStatus.Failed)), Times.Once());
    }

    [Fact]
    public void ProcessOrder_WhenStockIsInsufficient_DoesNotChargeAndDoesNotNotify()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(false);

        // ACT
        Action act = () => _sut.ProcessOrder(order.Id);

        // ASSERT
        act.Should().Throw<InsufficientStockException>();
        _payments.Verify(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>()), Times.Never());
        _notifications.Verify(n => n.SendConfirmation(It.IsAny<Order>(), It.IsAny<DateTime>()), Times.Never());
    }

    [Fact]
    public void ProcessOrder_WhenPaymentFails_ReturnsOrderWithPaymentFailedStatus()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(order.Id, order.TotalAmount)).Returns(new PaymentResult(false, "Недостаточно средств"));

        // ACT
        var result = _sut.ProcessOrder(order.Id);

        // ASSERT
        result.Status.Should().Be(OrderStatus.PaymentFailed, "при сбое платежа исключение не выбрасывается");
    }

    [Fact]
    public void ProcessOrder_WhenPaymentFails_ReleasesReservationAndSavesOrder()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(order.Id, order.TotalAmount)).Returns(new PaymentResult(false));

        // ACT
        _sut.ProcessOrder(order.Id);

        // ASSERT
        _inventory.Verify(i => i.ReleaseItems(order), Times.Once());
        _repository.Verify(r => r.Save(It.Is<Order>(o => o.Status == OrderStatus.PaymentFailed)), Times.Once());
    }

    [Fact]
    public void ProcessOrder_WhenPaymentFails_DoesNotSendConfirmation()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(order.Id, order.TotalAmount)).Returns(new PaymentResult(false));

        // ACT
        _sut.ProcessOrder(order.Id);

        // ASSERT
        _notifications.Verify(n => n.SendConfirmation(It.IsAny<Order>(), It.IsAny<DateTime>()), Times.Never(),
            "при сбое платежа SendConfirmation не вызывается");
    }

    [Fact]
    public void ProcessOrder_PaymentFailureWithStrictMocks_NeverNotifiesCustomer()
    {
        // ARRANGE
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

        // ACT
        var result = sut.ProcessOrder(order.Id);

        // ASSERT
        result.Status.Should().Be(OrderStatus.PaymentFailed, "для notifications не задан Setup, любой вызов упал бы с исключением");
        inventory.Verify(i => i.ReleaseItems(order), Times.Once());
    }

    [Fact]
    public void ProcessOrder_Charge_PassesOrderIdAndAmountCapturedByCallback()
    {
        // ARRANGE
        var order = OrderMother.NewOrder(id: 7);
        int? chargedOrderId = null;
        decimal? chargedAmount = null;
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>()))
            .Callback<int, decimal>((id, amount) => { chargedOrderId = id; chargedAmount = amount; })
            .Returns(new PaymentResult(true));

        // ACT
        _sut.ProcessOrder(order.Id);

        // ASSERT
        chargedOrderId.Should().Be(7);
        chargedAmount.Should().Be(order.TotalAmount);
    }

    [Theory]
    [InlineData(1, 100, 100)]
    [InlineData(3, 50, 150)]
    [InlineData(10, 99, 990)]
    public void ProcessOrder_WithDifferentQuantities_ChargesPriceTimesQuantity(int quantity, int price, int expectedAmount)
    {
        // ARRANGE
        var order = new Order { Id = 5, Items = new List<OrderItem> { new("SKU-1", quantity, price) } };
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>())).Returns(new PaymentResult(true));

        // ACT
        _sut.ProcessOrder(order.Id);

        // ASSERT
        _payments.Verify(p => p.Charge(5, It.Is<decimal>(a => a == expectedAmount)), Times.Once());
    }

    [Fact]
    public void ProcessOrder_WhenRepositoryThrows_PropagatesExceptionAndStopsProcessing()
    {
        // ARRANGE
        _repository.Setup(r => r.GetById(It.IsAny<int>())).Throws(new InvalidOperationException("БД недоступна"));

        // ACT
        Action act = () => _sut.ProcessOrder(1);

        // ASSERT
        act.Should().Throw<InvalidOperationException>().WithMessage("БД недоступна");
        _inventory.Verify(i => i.ReserveItems(It.IsAny<Order>()), Times.Never());
    }

    [Fact]
    public void ProcessOrder_WhenInventoryThrows_PropagatesExceptionAndDoesNotCharge()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Throws(new TimeoutException("склад не отвечает"));

        // ACT
        Action act = () => _sut.ProcessOrder(order.Id);

        // ASSERT
        act.Should().Throw<TimeoutException>();
        _payments.Verify(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>()), Times.Never());
    }

    [Fact]
    public void ProcessOrder_CalledTwiceForSameOrder_SecondCallThrowsAndChargesOnlyOnce()
    {
        // ARRANGE
        var order = OrderMother.NewOrder();
        _repository.Setup(r => r.GetById(order.Id)).Returns(order);
        _inventory.Setup(i => i.ReserveItems(order)).Returns(true);
        _payments.Setup(p => p.Charge(order.Id, order.TotalAmount)).Returns(new PaymentResult(true));

        // ACT
        _sut.ProcessOrder(order.Id);
        Action second = () => _sut.ProcessOrder(order.Id);

        // ASSERT
        second.Should().Throw<OrderProcessingException>("повторная обработка завершённого заказа запрещена");
        _payments.Verify(p => p.Charge(It.IsAny<int>(), It.IsAny<decimal>()), Times.Once());
    }

    [Fact]
    public void Constructor_WithNullDependency_ThrowsArgumentNullException()
    {
        // ARRANGE
        Action noRepository = () => new OrderProcessor(null!, _inventory.Object, _payments.Object, _notifications.Object, _clock.Object);
        Action noInventory = () => new OrderProcessor(_repository.Object, null!, _payments.Object, _notifications.Object, _clock.Object);
        Action noPayments = () => new OrderProcessor(_repository.Object, _inventory.Object, null!, _notifications.Object, _clock.Object);
        Action noNotifications = () => new OrderProcessor(_repository.Object, _inventory.Object, _payments.Object, null!, _clock.Object);
        Action noClock = () => new OrderProcessor(_repository.Object, _inventory.Object, _payments.Object, _notifications.Object, null!);

        // ACT & ASSERT
        noRepository.Should().Throw<ArgumentNullException>().WithParameterName("orders");
        noInventory.Should().Throw<ArgumentNullException>().WithParameterName("inventory");
        noPayments.Should().Throw<ArgumentNullException>().WithParameterName("payments");
        noNotifications.Should().Throw<ArgumentNullException>().WithParameterName("notifications");
        noClock.Should().Throw<ArgumentNullException>().WithParameterName("clock");
    }
}
