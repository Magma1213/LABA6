namespace Lab3.Core;

public class OrderProcessingException : Exception
{
    public OrderProcessingException(string message) : base(message)
    {
    }
}

public class InsufficientStockException : Exception
{
    public InsufficientStockException(string message) : base(message)
    {
    }
}
