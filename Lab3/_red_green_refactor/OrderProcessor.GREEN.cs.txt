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
        var order = _orders.GetById(orderId);

        if (order == null)
        {
            throw new OrderProcessingException($"Заказ {orderId} не найден.");
        }

        if (order.Status != OrderStatus.New)
        {
            throw new OrderProcessingException($"Заказ {orderId} уже обработан (статус {order.Status}).");
        }

        if (!_inventory.ReserveItems(order))
        {
            _inventory.ReleaseItems(order);
            order.Status = OrderStatus.Failed;
            _orders.Save(order);
            throw new InsufficientStockException($"Недостаточно товаров на складе для заказа {orderId}.");
        }

        var payment = _payments.Charge(order.Id, order.TotalAmount);

        if (!payment.Success)
        {
            _inventory.ReleaseItems(order);
            order.Status = OrderStatus.PaymentFailed;
            _orders.Save(order);
            return order;
        }

        order.Status = OrderStatus.Completed;
        _orders.Save(order);
        _notifications.SendConfirmation(order, _clock.UtcNow);

        return order;
    }
}
