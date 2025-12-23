namespace Rolling.Web.Models.Sms;

public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public EskizOptions Eskiz { get; set; } = new();

    public sealed class EskizOptions
    {
        public string BaseUrl { get; set; } = "https://notify.eskiz.uz/api";
        public string Sender { get; set; } = "4546";
        public string? CallbackUrl { get; set; }
        public CredentialsOptions Credentials { get; set; } = new();
        public TemplateOptions Templates { get; set; } = new();
        public int TokenLifetimeMinutes { get; set; } = 55;
        public int VerificationCodeTtlMinutes { get; set; } = 5;
        public int VerificationCodeLength { get; set; } = 4;
        public int ResendCooldownSeconds { get; set; } = 30;
    }

    public sealed class CredentialsOptions
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public sealed class TemplateOptions
    {
        public string LoginVerification { get; set; } =
            "Rolling Sushi: Tasdiqlash uchun kod: {code}";
    }
}
