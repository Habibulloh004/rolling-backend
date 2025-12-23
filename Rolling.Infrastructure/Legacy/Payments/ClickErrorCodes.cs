namespace Rolling.Infrastructure.Payments;

public static class ClickErrorCodes
{
    public const int Success = 0;
    public const int SignFailed = -1;
    public const int InvalidAmount = -2;
    public const int ActionNotFound = -3;
    public const int AlreadyPaid = -4;
    public const int UserNotFound = -5;
    public const int TransactionNotFound = -6;
    public const int BadRequest = -8;
    public const int TransactionCanceled = -9;
}
