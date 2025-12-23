using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rolling.Web.Models.Sms;

namespace Rolling.Web.Services.Sms;

public interface ISmsService
{
    Task<SmsCodeRequestResult> RequestLoginCodeAsync(string phone, CancellationToken cancellationToken = default);
    SmsCodeVerificationResult VerifyLoginCode(string phone, string code);
}

public sealed class SmsVerificationService : ISmsService
{
    private readonly EskizSmsClient _smsClient;
    private readonly IOptionsMonitor<SmsOptions> _optionsMonitor;
    private readonly ILogger<SmsVerificationService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SmsCodeEntry> _pendingCodes = new();

    public SmsVerificationService(
        EskizSmsClient smsClient,
        IOptionsMonitor<SmsOptions> optionsMonitor,
        ILogger<SmsVerificationService> logger,
        TimeProvider? timeProvider = null)
    {
        _smsClient = smsClient;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SmsCodeRequestResult> RequestLoginCodeAsync(string phone, CancellationToken cancellationToken = default)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return SmsCodeRequestResult.CreateFailure("INVALID_PHONE", "Valid phone number is required");
        }

        var options = _optionsMonitor.CurrentValue.Eskiz;
        var now = _timeProvider.GetUtcNow();
        if (_pendingCodes.TryGetValue(normalizedPhone, out var existing))
        {
            var cooldown = TimeSpan.FromSeconds(Math.Max(1, options.ResendCooldownSeconds));
            if (now - existing.CreatedAt < cooldown)
            {
                var retryAfter = cooldown - (now - existing.CreatedAt);
                return SmsCodeRequestResult.CreateFailure("RATE_LIMITED", $"Please wait {retryAfter.TotalSeconds:F0} seconds before requesting a new code.");
            }
        }

        var code = GenerateCode(Math.Max(4, options.VerificationCodeLength));
        var message = BuildVerificationMessage(code);
        var sendResult = await _smsClient.SendSmsAsync(normalizedPhone, message, cancellationToken);
        if (!sendResult.Success)
        {
            return SmsCodeRequestResult.CreateFailure("DELIVERY_FAILED", sendResult.ErrorMessage ?? "Failed to send SMS");
        }

        var expiresAt = now.AddMinutes(Math.Max(1, options.VerificationCodeTtlMinutes));
        var entry = new SmsCodeEntry(code, expiresAt, now);
        _pendingCodes[normalizedPhone] = entry;

        _logger.LogInformation("Verification code sent to {Phone}, expires at {ExpiresAt}", normalizedPhone, expiresAt);
        return SmsCodeRequestResult.CreateSuccess(expiresAt);
    }

    public SmsCodeVerificationResult VerifyLoginCode(string phone, string code)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return SmsCodeVerificationResult.CreateFailure("INVALID_PHONE", "Valid phone number is required", false);
        }

        if (!_pendingCodes.TryGetValue(normalizedPhone, out var entry))
        {
            return SmsCodeVerificationResult.CreateFailure("CODE_NOT_FOUND", "No verification code requested for this number", false);
        }

        var now = _timeProvider.GetUtcNow();
        if (now > entry.ExpiresAt)
        {
            _pendingCodes.TryRemove(normalizedPhone, out _);
            return SmsCodeVerificationResult.CreateFailure("CODE_EXPIRED", "Verification code has expired", true);
        }

        if (!string.Equals(entry.Code, code?.Trim(), StringComparison.Ordinal))
        {
            return SmsCodeVerificationResult.CreateFailure("CODE_INVALID", "Invalid verification code", false);
        }

        _pendingCodes.TryRemove(normalizedPhone, out _);
        _logger.LogInformation("Phone {Phone} verified successfully", normalizedPhone);
        return SmsCodeVerificationResult.CreateSuccess();
    }

    private string BuildVerificationMessage(string code)
    {
        var template = _optionsMonitor.CurrentValue.Eskiz.Templates.LoginVerification;
        if (string.IsNullOrWhiteSpace(template))
        {
            template = "Rolling Sushi verification code: {code}";
        }

        if (template.Contains("{code}", StringComparison.OrdinalIgnoreCase))
        {
            return template.Replace("{code}", code, StringComparison.OrdinalIgnoreCase);
        }

        if (template.Contains("%d", StringComparison.Ordinal))
        {
            return template.Replace("%d", code, StringComparison.Ordinal);
        }

        return $"{template.Trim()} {code}";
    }

    private static string NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            return string.Empty;
        }

        if (digits.StartsWith("998", StringComparison.Ordinal))
        {
            if (digits.Length == 12)
            {
                return digits;
            }

            if (digits.Length > 12)
            {
                return digits[..12];
            }

            return string.Empty;
        }

        if (digits.Length == 9)
        {
            return "998" + digits;
        }

        if (digits.Length == 12)
        {
            return digits;
        }

        return string.Empty;
    }

    private static string GenerateCode(int length)
    {
        var random = Random.Shared;
        var min = (int)Math.Pow(10, length - 1);
        var max = (int)Math.Pow(10, length) - 1;
        var code = random.Next(min, max + 1);
        return code.ToString($"D{length}");
    }

    private sealed record SmsCodeEntry(string Code, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);
}

public sealed record SmsCodeRequestResult(bool Success, string Code, string Message, DateTimeOffset? ExpiresAt)
{
    public static SmsCodeRequestResult CreateSuccess(DateTimeOffset expiresAt) =>
        new(true, "SMS_CODE_SENT", "Verification code sent", expiresAt);

    public static SmsCodeRequestResult CreateFailure(string code, string message) =>
        new(false, code, message, null);
}

public sealed record SmsCodeVerificationResult(bool Success, string Code, string Message, bool Expired)
{
    public static SmsCodeVerificationResult CreateSuccess() =>
        new(true, "SMS_CODE_CONFIRMED", "Verification code confirmed", false);

    public static SmsCodeVerificationResult CreateFailure(string code, string message, bool expired) =>
        new(false, code, message, expired);
}
