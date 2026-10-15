namespace Lab3.Core;

public interface IOrderRepository
{
    Order? GetById(int orderId);

    void Save(Order order);
}

public interface IInventoryService
{
    bool ReserveItems(Order order);

    void ReleaseItems(Order order);
}

public interface IPaymentGateway
{
    PaymentResult Charge(int orderId, decimal amount);
}

public interface INotificationService
{
    void SendConfirmation(Order order, DateTime processedAt);
}

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
