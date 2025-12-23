using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Messaging;

namespace Rolling.Infrastructure.Notifications;

public sealed class NotificationService
{
    private static readonly string[] SupportedLanguages = { "en", "uz", "ru" };

    private readonly FirebaseMessagingAccessor _messagingAccessor;
    private readonly ILogger<NotificationService> _logger;

    private readonly Dictionary<string, NotificationTemplate> _messages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new NotificationTemplate("Rolling Sushi", "Your order is ready for pickup!", new Dictionary<string, string>
        {
            ["orderReady"] = "Order Ready",
            ["newPromotion"] = "New Promotion Available!",
            ["welcomeMessage"] = "Welcome to Rolling Sushi!"
        }),
        ["uz"] = new NotificationTemplate("Rolling Sushi", "Buyurtmangiz tayyor!", new Dictionary<string, string>
        {
            ["orderReady"] = "Buyurtma Tayyor",
            ["newPromotion"] = "Yangi chegirma mavjud!",
            ["welcomeMessage"] = "Rolling Sushi'ga xush kelibsiz!"
        }),
        ["ru"] = new NotificationTemplate("Rolling Sushi", "Ваш заказ готов к выдаче!", new Dictionary<string, string>
        {
            ["orderReady"] = "Заказ Готов",
            ["newPromotion"] = "Доступна новая акция!",
            ["welcomeMessage"] = "Добро пожаловать в Rolling Sushi!"
        })
    };

    public NotificationService(FirebaseMessagingAccessor messagingAccessor, ILogger<NotificationService> logger)
    {
        _messagingAccessor = messagingAccessor;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, NotificationTemplate> GetMessages() => _messages;

    public async Task<bool> ValidateTokenAsync(string deviceToken, CancellationToken cancellationToken)
    {
        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            return false;
        }

        try
        {
            var message = new Message
            {
                Token = deviceToken,
                Notification = new Notification
                {
                    Title = "Test",
                    Body = "Test"
                }
            };

            await messaging.SendAsync(message, dryRun: true, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed.");
            return false;
        }
    }

    public async Task SendToTopicAsync(string topic, string language, string messageType, NotificationPayload? customMessage, IDictionary<string, string>? data, CancellationToken cancellationToken)
    {
        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            return;
        }

        var content = customMessage ?? BuildMessage(language, messageType);
        var message = new Message
        {
            Topic = topic,
            Notification = new Notification
            {
                Title = content.Title,
                Body = content.Body
            },
            Data = BuildData(language, messageType, data),
            Android = new AndroidConfig
            {
                Notification = new AndroidNotification
                {
                    Icon = "ic_notification",
                    Color = "#004032",
                    Sound = "default"
                }
            },
            Apns = new ApnsConfig
            {
                Aps = new Aps
                {
                    Sound = "default",
                    Badge = 1
                }
            }
        };

        await messaging.SendAsync(message, cancellationToken);
    }

    public async Task SendToDeviceAsync(string deviceToken, string language, string messageType, NotificationPayload? customMessage, IDictionary<string, string>? data, CancellationToken cancellationToken)
    {
        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            return;
        }

        var content = customMessage ?? BuildMessage(language, messageType);
        var message = new Message
        {
            Token = deviceToken,
            Notification = new Notification
            {
                Title = content.Title,
                Body = content.Body
            },
            Data = BuildData(language, messageType, data),
            Android = new AndroidConfig
            {
                Notification = new AndroidNotification
                {
                    Icon = "ic_notification",
                    Color = "#004032",
                    Sound = "default"
                }
            },
            Apns = new ApnsConfig
            {
                Aps = new Aps
                {
                    Sound = "default",
                    Badge = 1
                }
            }
        };

        await messaging.SendAsync(message, cancellationToken);
    }

    public async Task SendToDevicesAsync(IEnumerable<string> deviceTokens, string language, string messageType, NotificationPayload? customMessage, IDictionary<string, string>? data, CancellationToken cancellationToken)
    {
        var tokens = deviceTokens.ToArray();
        if (tokens.Length == 0)
        {
            return;
        }

        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            return;
        }

        var content = customMessage ?? BuildMessage(language, messageType);
        var message = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification
            {
                Title = content.Title,
                Body = content.Body
            },
            Data = BuildData(language, messageType, data),
            Android = new AndroidConfig
            {
                Notification = new AndroidNotification
                {
                    Icon = "ic_notification",
                    Color = "#004032",
                    Sound = "default"
                }
            },
            Apns = new ApnsConfig
            {
                Aps = new Aps
                {
                    Sound = "default",
                    Badge = 1
                }
            }
        };

        await messaging.SendEachForMulticastAsync(message, cancellationToken);
    }

    public async Task SubscribeAsync(string token, string language, CancellationToken cancellationToken)
    {
        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            return;
        }

        var topic = $"all_users_{language}";
        await messaging.SubscribeToTopicAsync(new[] { token }, topic);
        await messaging.SubscribeToTopicAsync(new[] { token }, "all_users");
    }

    public async Task UnsubscribeAsync(string token, string language, CancellationToken cancellationToken)
    {
        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            return;
        }

        var topic = $"all_users_{language}";
        await messaging.UnsubscribeFromTopicAsync(new[] { token }, topic);
        await messaging.UnsubscribeFromTopicAsync(new[] { token }, "all_users");
    }

    public static bool IsLanguageSupported(string language) =>
        SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);

    private NotificationPayload BuildMessage(string language, string messageType)
    {
        var lang = IsLanguageSupported(language) ? language.ToLowerInvariant() : "en";
        var template = _messages[lang];
        var body = template.Extras.TryGetValue(messageType, out var value) ? value : template.Body;
        return new NotificationPayload(template.Title, body);
    }

    private static IReadOnlyDictionary<string, string> BuildData(string language, string messageType, IDictionary<string, string>? extra)
    {
        var data = new Dictionary<string, string>
        {
            ["language"] = language,
            ["messageType"] = messageType,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };

        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                data[key] = value;
            }
        }

        return data;
    }
}

public sealed record NotificationTemplate(string Title, string Body, IReadOnlyDictionary<string, string> Extras);

public sealed record NotificationPayload(string Title, string Body);
