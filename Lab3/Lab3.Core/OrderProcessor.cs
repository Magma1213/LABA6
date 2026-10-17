namespace Lab3.Core;

public class OrderProcessor
{
    private readonly IOrderRepository _orders;
    private readonly IInventoryService _inventory;
    private readonly IPaymentGateway _payments;
    private readonly INotificationService _notifications;
    private readonly IDateTimeProvider _clock;

    public OrderProcessor(
        IOrderRepository orders,
        IInventoryService inventory,
        IPaymentGateway payments,
        INotificationService notifications,
        IDateTimeProvider clock)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Order ProcessOrder(int orderId)
    {
        var order = GetNewOrder(orderId);

        if (!_inventory.ReserveItems(order))
        {
            Reject(order, OrderStatus.Failed);
            throw new InsufficientStockException($"Недостаточно товаров на складе для заказа {orderId}.");
        }

        var payment = _payments.Charge(order.Id, order.TotalAmount);

        if (!payment.Success)
        {
            Reject(order, OrderStatus.PaymentFailed);
            return order;
        }

        order.Status = OrderStatus.Completed;
        _orders.Save(order);
        _notifications.SendConfirmation(order, _clock.UtcNow);

        return order;
    }

    private Order GetNewOrder(int orderId)
    {
        var order = _orders.GetById(orderId);

        if (order == null)
        {
            throw new OrderProcessingException($"Заказ {orderId} не найден.");
        }

        if (order.Status != OrderStatus.New)
        {
            throw new OrderProcessingException($"Заказ {orderId} уже обработан (статус {order.Status}).");
        }

        return order;
    }

    private void Reject(Order order, OrderStatus status)
    {
        _inventory.ReleaseItems(order);
        order.Status = status;
        _orders.Save(order);
    }
}
