using System.Diagnostics;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Messaging;

namespace Rolling.Infrastructure.Notifications;

public sealed class NotificationService
{
    private static readonly string[] SupportedLanguages = { "en", "uz", "ru" };
    private static readonly ActivitySource ActivitySource = new("Rolling.Notifications");

    private readonly FirebaseMessagingAccessor _messagingAccessor;
    private readonly NotificationTokenStore _tokenStore;
    private readonly ILogger<NotificationService> _logger;

    private readonly Dictionary<string, NotificationTemplate> _messages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new NotificationTemplate("Rolling Sushi", "Your order is ready for pickup!", new Dictionary<string, string>
        {
            ["orderReady"] = "Order Ready",
            ["newPromotion"] = "New Promotion Available!",
            ["welcomeMessage"] = "Welcome to Rolling Sushi!",
            ["orderAwaitingPayment"] = "Waiting for payment to confirm your order",
            ["orderPending"] = "Order received, awaiting confirmation",
            ["orderAccepted"] = "Your order has been accepted!",
            ["orderPreparing"] = "Your order is being prepared",
            ["orderOnTheWay"] = "Your order is on the way!",
            ["orderDelivered"] = "Your order has been delivered",
            ["orderCancelled"] = "Your order has been cancelled"
        }),
        ["uz"] = new NotificationTemplate("Rolling Sushi", "Buyurtmangiz tayyor!", new Dictionary<string, string>
        {
            ["orderReady"] = "Buyurtma Tayyor",
            ["newPromotion"] = "Yangi chegirma mavjud!",
            ["welcomeMessage"] = "Rolling Sushi'ga xush kelibsiz!",
            ["orderAwaitingPayment"] = "To'lov kutilmoqda",
            ["orderPending"] = "Buyurtmangiz qabul qilindi, tasdiqlash kutilmoqda",
            ["orderAccepted"] = "Buyurtmangiz qabul qilindi!",
            ["orderPreparing"] = "Buyurtmangiz tayyorlanmoqda",
            ["orderOnTheWay"] = "Buyurtmangiz yo'lda!",
            ["orderDelivered"] = "Buyurtmangiz yetkazildi",
            ["orderCancelled"] = "Buyurtmangiz bekor qilindi"
        }),
        ["ru"] = new NotificationTemplate("Rolling Sushi", "Ваш заказ готов к выдаче!", new Dictionary<string, string>
        {
            ["orderReady"] = "Заказ Готов",
            ["newPromotion"] = "Доступна новая акция!",
            ["welcomeMessage"] = "Добро пожаловать в Rolling Sushi!",
            ["orderAwaitingPayment"] = "Ожидаем оплату заказа",
            ["orderPending"] = "Заказ получен, ждём подтверждения",
            ["orderAccepted"] = "Ваш заказ принят!",
            ["orderPreparing"] = "Ваш заказ готовится",
            ["orderOnTheWay"] = "Ваш заказ в пути!",
            ["orderDelivered"] = "Ваш заказ доставлен",
            ["orderCancelled"] = "Ваш заказ отменён"
        })
    };

    public NotificationService(
        FirebaseMessagingAccessor messagingAccessor,
        NotificationTokenStore tokenStore,
        ILogger<NotificationService> logger)
    {
        _messagingAccessor = messagingAccessor;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, NotificationTemplate> GetMessages() => _messages;

    public async Task<bool> ValidateTokenAsync(string deviceToken, CancellationToken cancellationToken)
    {
        var tokenPreview = deviceToken.Length > 20
            ? $"{deviceToken[..10]}...{deviceToken[^10..]}"
            : deviceToken;

        _logger.LogDebug("ValidateTokenAsync called: Token={TokenPreview}", tokenPreview);

        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            _logger.LogWarning(
                "Firebase Messaging is not configured. Token validation skipped: Token={TokenPreview}",
                tokenPreview);
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
            _logger.LogDebug("Token validation succeeded: Token={TokenPreview}", tokenPreview);
            return true;
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogWarning(
                "Token validation failed: Token={TokenPreview}, ErrorCode={ErrorCode}, Message={Message}",
                tokenPreview, ex.MessagingErrorCode, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed: Token={TokenPreview}", tokenPreview);
            return false;
        }
    }

    public async Task SendToTopicAsync(string topic, string language, string messageType, NotificationPayload? customMessage, IDictionary<string, string>? data, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("SendToTopic");
        activity?.SetTag("topic", topic);
        activity?.SetTag("language", language);
        activity?.SetTag("messageType", messageType);

        _logger.LogDebug(
            "SendToTopicAsync called: Topic={Topic}, Language={Language}, MessageType={MessageType}",
            topic, language, messageType);

        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            _logger.LogWarning("Firebase Messaging is not configured. Push notification to topic {Topic} skipped", topic);
            return;
        }

        var content = customMessage ?? BuildMessage(language, messageType);
        _logger.LogDebug("Notification content: Title={Title}, Body={Body}", content.Title, content.Body);

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
                    Badge = 1,
                    ContentAvailable = true
                },
                Headers = new Dictionary<string, string>
                {
                    ["apns-priority"] = "10",
                    ["apns-push-type"] = "alert"
                }
            }
        };

        try
        {
            var messageId = await messaging.SendAsync(message, cancellationToken);
            _logger.LogInformation(
                "Push notification sent to topic {Topic}: MessageId={MessageId}, Type={MessageType}",
                topic, messageId, messageType);
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogError(ex,
                "Firebase error sending push to topic {Topic}: Code={ErrorCode}, Message={Message}",
                topic, ex.MessagingErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notification to topic {Topic}", topic);
            throw;
        }
    }

    public async Task SendToDeviceAsync(string deviceToken, string language, string messageType, NotificationPayload? customMessage, IDictionary<string, string>? data, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("SendToDevice");
        activity?.SetTag("language", language);
        activity?.SetTag("messageType", messageType);
        activity?.SetTag("hasCustomMessage", customMessage != null);

        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            _logger.LogWarning("Push notification skipped: device token is empty");
            return;
        }

        var tokenPreview = deviceToken.Length > 20
            ? $"{deviceToken[..10]}...{deviceToken[^10..]}"
            : deviceToken;

        if (_tokenStore.TryGet(deviceToken, out var entry) && entry.IsValid == false)
        {
            _logger.LogWarning(
                "Push notification skipped: device token marked invalid: Token={TokenPreview}",
                tokenPreview);
            return;
        }

        _logger.LogDebug(
            "SendToDeviceAsync called: Token={TokenPreview}, Language={Language}, MessageType={MessageType}",
            tokenPreview, language, messageType);

        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            _logger.LogWarning(
                "Firebase Messaging is not configured. Push notification skipped: Token={TokenPreview}, Type={MessageType}",
                tokenPreview, messageType);
            return;
        }

        var content = customMessage ?? BuildMessage(language, messageType);
        _logger.LogDebug(
            "Notification content prepared: Title={Title}, Body={Body}",
            content.Title, content.Body);

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
                },
                Priority = Priority.High
            },
            Apns = new ApnsConfig
            {
                Aps = new Aps
                {
                    Sound = "default",
                    Badge = 1,
                    ContentAvailable = true,
                    MutableContent = true
                },
                Headers = new Dictionary<string, string>
                {
                    ["apns-priority"] = "10",
                    ["apns-push-type"] = "alert"
                }
            }
        };

        try
        {
            var messageId = await messaging.SendAsync(message, cancellationToken);
            _logger.LogInformation(
                "Push notification sent to device: Token={TokenPreview}, MessageId={MessageId}, Type={MessageType}",
                tokenPreview, messageId, messageType);
        }
        catch (FirebaseMessagingException ex)
        {
            if (ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
            {
                _logger.LogWarning(
                    "Device token appears to be invalid or unregistered: Token={TokenPreview}, ErrorCode={ErrorCode}",
                    tokenPreview, ex.MessagingErrorCode);
                if (!_tokenStore.MarkInvalid(deviceToken))
                {
                    _tokenStore.AddOrUpdate(deviceToken, new NotificationTokenEntry(
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        false));
                }

                return;
            }

            _logger.LogError(ex,
                "Firebase error sending push to device: Token={TokenPreview}, ErrorCode={ErrorCode}, Message={Message}",
                tokenPreview, ex.MessagingErrorCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send push notification to device: Token={TokenPreview}, Type={MessageType}",
                tokenPreview, messageType);
            throw;
        }
    }

    public async Task SendToDevicesAsync(IEnumerable<string> deviceTokens, string language, string messageType, NotificationPayload? customMessage, IDictionary<string, string>? data, CancellationToken cancellationToken)
    {
        var tokens = deviceTokens.ToArray();

        using var activity = ActivitySource.StartActivity("SendToDevices");
        activity?.SetTag("tokenCount", tokens.Length);
        activity?.SetTag("language", language);
        activity?.SetTag("messageType", messageType);

        _logger.LogDebug(
            "SendToDevicesAsync called: TokenCount={TokenCount}, Language={Language}, MessageType={MessageType}",
            tokens.Length, language, messageType);

        if (tokens.Length == 0)
        {
            _logger.LogDebug("No device tokens provided, skipping multicast notification");
            return;
        }

        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            _logger.LogWarning(
                "Firebase Messaging is not configured. Multicast push notification skipped: TokenCount={TokenCount}, Type={MessageType}",
                tokens.Length, messageType);
            return;
        }

        var content = customMessage ?? BuildMessage(language, messageType);
        _logger.LogDebug(
            "Multicast notification content prepared: Title={Title}, Body={Body}, Recipients={TokenCount}",
            content.Title, content.Body, tokens.Length);

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
                },
                Priority = Priority.High
            },
            Apns = new ApnsConfig
            {
                Aps = new Aps
                {
                    Sound = "default",
                    Badge = 1,
                    ContentAvailable = true,
                    MutableContent = true
                },
                Headers = new Dictionary<string, string>
                {
                    ["apns-priority"] = "10",
                    ["apns-push-type"] = "alert"
                }
            }
        };

        try
        {
            var response = await messaging.SendEachForMulticastAsync(message, cancellationToken);
            _logger.LogInformation(
                "Multicast push notification sent: SuccessCount={SuccessCount}, FailureCount={FailureCount}, Type={MessageType}",
                response.SuccessCount, response.FailureCount, messageType);

            // Log individual failures for debugging
            if (response.FailureCount > 0)
            {
                for (var i = 0; i < response.Responses.Count; i++)
                {
                    var sendResponse = response.Responses[i];
                    if (!sendResponse.IsSuccess)
                    {
                        var tokenPreview = tokens[i].Length > 20
                            ? $"{tokens[i][..10]}...{tokens[i][^10..]}"
                            : tokens[i];
                        _logger.LogWarning(
                            "Multicast send failed for token {TokenPreview}: ErrorCode={ErrorCode}, Message={Message}",
                            tokenPreview,
                            sendResponse.Exception?.MessagingErrorCode,
                            sendResponse.Exception?.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send multicast push notification: TokenCount={TokenCount}, Type={MessageType}",
                tokens.Length, messageType);
            throw;
        }
    }

    public async Task SubscribeAsync(string token, string language, CancellationToken cancellationToken)
    {
        var tokenPreview = token.Length > 20
            ? $"{token[..10]}...{token[^10..]}"
            : token;

        _logger.LogDebug(
            "SubscribeAsync called: Token={TokenPreview}, Language={Language}",
            tokenPreview, language);

        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            _logger.LogWarning(
                "Firebase Messaging is not configured. Topic subscription skipped: Token={TokenPreview}",
                tokenPreview);
            return;
        }

        var topic = $"all_users_{language}";
        try
        {
            await messaging.SubscribeToTopicAsync(new[] { token }, topic);
            await messaging.SubscribeToTopicAsync(new[] { token }, "all_users");
            _logger.LogInformation(
                "Device subscribed to topics: Token={TokenPreview}, Topics=[{Topic}, all_users]",
                tokenPreview, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to subscribe device to topics: Token={TokenPreview}, Topic={Topic}",
                tokenPreview, topic);
            throw;
        }
    }

    public async Task UnsubscribeAsync(string token, string language, CancellationToken cancellationToken)
    {
        var tokenPreview = token.Length > 20
            ? $"{token[..10]}...{token[^10..]}"
            : token;

        _logger.LogDebug(
            "UnsubscribeAsync called: Token={TokenPreview}, Language={Language}",
            tokenPreview, language);

        var messaging = _messagingAccessor.Messaging;
        if (messaging is null)
        {
            _logger.LogWarning(
                "Firebase Messaging is not configured. Topic unsubscription skipped: Token={TokenPreview}",
                tokenPreview);
            return;
        }

        var topic = $"all_users_{language}";
        try
        {
            await messaging.UnsubscribeFromTopicAsync(new[] { token }, topic);
            await messaging.UnsubscribeFromTopicAsync(new[] { token }, "all_users");
            _logger.LogInformation(
                "Device unsubscribed from topics: Token={TokenPreview}, Topics=[{Topic}, all_users]",
                tokenPreview, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to unsubscribe device from topics: Token={TokenPreview}, Topic={Topic}",
                tokenPreview, topic);
            throw;
        }
    }

    public static bool IsLanguageSupported(string language) =>
        SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Send order status update notification to a device.
    /// </summary>
    public async Task SendOrderStatusUpdateAsync(
        string deviceToken,
        string orderId,
        string orderNumber,
        string? posterIncomingOrderId,
        string? posterTransactionId,
        string status,
        string language,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("SendOrderStatusUpdate");
        activity?.SetTag("orderId", orderId);
        activity?.SetTag("orderNumber", orderNumber);
        activity?.SetTag("status", status);
        activity?.SetTag("language", language);

        var tokenPreview = deviceToken.Length > 20
            ? $"{deviceToken[..10]}...{deviceToken[^10..]}"
            : deviceToken;

        _logger.LogInformation(
            "Sending order status update notification: OrderId={OrderId}, OrderNumber={OrderNumber}, Status={Status}, Token={TokenPreview}, Language={Language}",
            orderId, orderNumber, status, tokenPreview, language);

        var messageType = status switch
        {
            "awaitingPayment" => "orderAwaitingPayment",
            "pending" => "orderPending",
            "accepted" => "orderAccepted",
            "preparing" => "orderPreparing",
            "onTheWay" => "orderOnTheWay",
            "delivered" => "orderDelivered",
            "cancelled" => "orderCancelled",
            _ => "orderReady"
        };

        _logger.LogDebug(
            "Order status mapped to message type: Status={Status} -> MessageType={MessageType}",
            status, messageType);

        // iOS app expects these specific field names for order identification
        var data = new Dictionary<string, string>
        {
            ["type"] = "orderStatusUpdate",
            ["orderId"] = orderId,
            ["order_id"] = orderId,
            ["orderNumber"] = orderNumber,
            ["order_number"] = orderNumber,
            ["status"] = status
        };

        // Add Poster IDs if available
        if (!string.IsNullOrWhiteSpace(posterIncomingOrderId))
        {
            data["posterIncomingOrderId"] = posterIncomingOrderId;
            data["poster_incoming_order_id"] = posterIncomingOrderId;
            data["incoming_order_id"] = posterIncomingOrderId;
            _logger.LogDebug("Added posterIncomingOrderId to payload: {PosterIncomingOrderId}", posterIncomingOrderId);
        }

        if (!string.IsNullOrWhiteSpace(posterTransactionId))
        {
            data["posterTransactionId"] = posterTransactionId;
            data["poster_transaction_id"] = posterTransactionId;
            data["transaction_id"] = posterTransactionId;
            _logger.LogDebug("Added posterTransactionId to payload: {PosterTransactionId}", posterTransactionId);
        }

        // Add deeplink for iOS app navigation
        data["deeplink"] = $"centro://orders/{orderId}/track";

        try
        {
            await SendToDeviceAsync(deviceToken, language, messageType, null, data, cancellationToken);
            _logger.LogInformation(
                "Order status update notification sent successfully: OrderId={OrderId}, Status={Status}",
                orderId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send order status update notification: OrderId={OrderId}, OrderNumber={OrderNumber}, Status={Status}",
                orderId, orderNumber, status);
            throw;
        }
    }

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
