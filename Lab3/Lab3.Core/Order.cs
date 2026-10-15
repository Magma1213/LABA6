namespace Lab3.Core;

public enum OrderStatus
{
    New,
    Processing,
    Completed,
    Failed,
    PaymentFailed
}

public record OrderItem(string Sku, int Quantity, decimal Price);

public class Order
{
    public int Id { get; init; }

    public List<OrderItem> Items { get; init; } = new();

    public OrderStatus Status { get; set; } = OrderStatus.New;

    public decimal TotalAmount => Items.Sum(i => i.Price * i.Quantity);
}

public record PaymentResult(bool Success, string? Error = null);
