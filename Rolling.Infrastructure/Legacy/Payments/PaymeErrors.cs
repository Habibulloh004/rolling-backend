namespace Rolling.Infrastructure.Payments;

public static class PaymeErrors
{
    public static readonly PaymeErrorDescriptor InvalidAmount = new(
        "InvalidAmount",
        -31001,
        new("Noto'g'ri summa", "Недопустимая сумма", "Invalid amount"));

    public static readonly PaymeErrorDescriptor CantDoOperation = new(
        "CantDoOperation",
        -31008,
        new("Biz operatsiyani bajara olmaymiz", "Мы не можем сделать операцию", "We can't do operation"));

    public static readonly PaymeErrorDescriptor TransactionNotFound = new(
        "TransactionNotFound",
        -31050,
        new("Tranzaktsiya topilmadi", "Транзакция не найдена", "Transaction not found"));

    public static readonly PaymeErrorDescriptor AlreadyDone = new(
        "AlreadyDone",
        -31060,
        new("Mahsulot uchun to'lov qilingan", "Оплачено за товар", "Paid for the product"));

    public static readonly PaymeErrorDescriptor Pending = new(
        "Pending",
        -31050,
        new("Mahsulot uchun to'lov kutilayapti", "Ожидается оплата товар", "Payment for the product is pending"));

    public static readonly PaymeErrorDescriptor InvalidAuthorization = new(
        "InvalidAuthorization",
        -32504,
        new("Avtorizatsiya yaroqsiz", "Авторизация недействительна", "Authorization invalid"));
}
