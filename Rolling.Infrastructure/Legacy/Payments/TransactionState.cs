namespace Rolling.Infrastructure.Payments;

public enum TransactionState
{
    Paid = 2,
    Pending = 1,
    PendingCanceled = -1,
    PaidCanceled = -2
}
