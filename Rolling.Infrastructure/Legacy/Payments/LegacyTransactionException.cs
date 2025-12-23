namespace Rolling.Infrastructure.Payments;

public sealed class PaymentTransactionException : Exception
{
    public PaymentTransactionException(PaymeErrorDescriptor descriptor, string? id = null, object? data = null)
        : base(descriptor.Name)
    {
        Descriptor = descriptor;
        TransactionId = id;
        DataPayload = data;
    }

    public PaymeErrorDescriptor Descriptor { get; }

    public string? TransactionId { get; }

    public object? DataPayload { get; }
}
