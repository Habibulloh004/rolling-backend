namespace Rolling.Infrastructure.Payments;

public sealed class PaymeErrorDescriptor
{
    public PaymeErrorDescriptor(string name, int code, LocalizedMessage message)
    {
        Name = name;
        Code = code;
        Message = message;
    }

    public string Name { get; }

    public int Code { get; }

    public LocalizedMessage Message { get; }

    public sealed class LocalizedMessage
    {
        public LocalizedMessage(string uz, string ru, string en)
        {
            Uz = uz;
            Ru = ru;
            En = en;
        }

        public string Uz { get; }

        public string Ru { get; }

        public string En { get; }
    }
}
