using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Orders;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Infrastructure.Poster;
using Rolling.Web.Models.Orders;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("")]
public sealed class PosterBridgeController : ControllerBase
{
    private readonly PosterService _posterService;
    private readonly AppDbContext _dbContext;
    private readonly ActiveOrderTracker _orderTracker;
    private readonly NotificationTokenStore _tokenStore;
    private readonly IOrderUpdatesPublisher _orderUpdatesPublisher;
    private readonly ILogger<PosterBridgeController> _logger;

    public PosterBridgeController(
        PosterService posterService,
        AppDbContext dbContext,
        ActiveOrderTracker orderTracker,
        NotificationTokenStore tokenStore,
        IOrderUpdatesPublisher orderUpdatesPublisher,
        ILogger<PosterBridgeController> logger)
    {
        _posterService = posterService;
        _dbContext = dbContext;
        _orderTracker = orderTracker;
        _tokenStore = tokenStore;
        _orderUpdatesPublisher = orderUpdatesPublisher;
        _logger = logger;
    }

    [HttpGet("posterClientGroup")]
    public async Task<IActionResult> GetClientGroupsAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetClientGroupsAsync(cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpGet("posterClient/{id}")]
    public async Task<IActionResult> GetClientAsync(string id, CancellationToken cancellationToken)
    {
        var document = await _posterService.GetClientAsync(id, cancellationToken);
        return CreatePosterResponse(document, unwrapResponse: false);
    }

    [HttpPost("posterCreateClient")]
    public async Task<IActionResult> CreateClientAsync([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var document = await _posterService.CreateClientAsync(payload, cancellationToken);
        return CreatePosterResponse(document, unwrapResponse: false);
    }

    [HttpGet("posterClients")]
    public async Task<IActionResult> GetClientsAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetClientsAsync(null, cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpGet("posterProducts")]
    public async Task<IActionResult> GetProductsAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetProductsAsync(cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpGet("posterCategories")]
    public async Task<IActionResult> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetCategoriesAsync(cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpGet("getClientTransaction/{phone}")]
    public async Task<IActionResult> GetClientByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        var convertedPhone = ConvertPhoneNumber(phone);
        var document = await _posterService.GetClientsAsync(convertedPhone, cancellationToken);
        if (document is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Poster service unavailable" });
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("response", out var clients) || clients.ValueKind != JsonValueKind.Array)
            {
                return Ok(new { error = "No client found." });
            }

            var first = clients.EnumerateArray().FirstOrDefault();
            return first.ValueKind == JsonValueKind.Undefined
                ? Ok(new { error = "No client found." })
                : Ok(first.Clone());
        }
    }

    [HttpGet("getSpot")]
    public async Task<IActionResult> GetSpotAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetSpotsAsync(cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpPost("api/posttoposter")]
    public async Task<IActionResult> PostToPosterAsync([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        // Log incoming request
        _logger.LogInformation("=== INCOMING ORDER REQUEST ===");
        _logger.LogInformation("SpotId: {SpotId}", request.SpotId);
        _logger.LogInformation("Phone: {Phone}", request.Phone);
        _logger.LogInformation("FirstName: {FirstName}", request.FirstName);
        _logger.LogInformation("LastName: {LastName}", request.LastName);
        _logger.LogInformation("Address: {Address}", request.Address);
        _logger.LogInformation("DeliveryPrice: {DeliveryPrice}", request.DeliveryPrice);
        _logger.LogInformation("PaymentMethodId: {PaymentMethodId}", request.PaymentMethodId);
        _logger.LogInformation("FcmToken: {FcmToken}", request.FcmToken);
        _logger.LogInformation("Products count: {Count}", request.Products.Count);

        UpdateTokenLanguageFromRequest(request);

        foreach (var (product, idx) in request.Products.Select((p, i) => (p, i)))
        {
            _logger.LogInformation("Product[{Index}]: ProductId={ProductId}, Name={Name}, Count={Count}, Price={Price}, IsGift={IsGift}, ModificatorId={ModificatorId}",
                idx, product.ProductId, product.Name, product.Count, product.Price, product.IsGift, product.ModificatorId);
        }

        // Match iOS PosterAPIService.createIncomingOrder encoding rules.
        var sanitizedPhone = SanitizePosterPhoneNumber(request.Phone);
        var sanitizedAlternatePhone = string.IsNullOrWhiteSpace(request.AlternatePhone)
            ? null
            : SanitizePosterPhoneNumber(request.AlternatePhone);

        // Build Poster-compatible payload with snake_case keys (exclude fields Poster doesn't understand like fcmToken)
        var posterPayload = new Dictionary<string, object?>
        {
            ["spot_id"] = request.SpotId,
            ["phone"] = sanitizedPhone,
            ["alternate_phone"] = sanitizedAlternatePhone,
            ["first_name"] = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim()[..Math.Min(60, request.FirstName.Trim().Length)],
            ["last_name"] = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim()[..Math.Min(60, request.LastName.Trim().Length)],
            ["address"] = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim()[..Math.Min(250, request.Address.Trim().Length)],
            ["comment"] = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            // Poster expects delivery_price in minor units (x100), same as iOS implementation.
            ["delivery_price"] = request.DeliveryPrice > 0m ? (long)Math.Round(request.DeliveryPrice * 100m) : null,
            ["payment_method_id"] = string.IsNullOrWhiteSpace(request.PaymentMethodId) ? null : request.PaymentMethodId,
            ["service_mode"] = request.ServiceMode,
            ["products"] = request.Products.Select(p => new Dictionary<string, object?>
            {
                ["product_id"] = p.ProductId,
                ["count"] = p.Count,
                ["modificator_id"] = string.IsNullOrWhiteSpace(p.ModificatorId) ? null : p.ModificatorId,
                // Match iOS: send price only when overriding (promo items), and in minor units (x100).
                ["price"] = p.PriceOverride.HasValue ? (long)Math.Round(p.PriceOverride.Value * 100m) : null,
                ["is_gift"] = p.IsGift
            }).ToList(),
            ["promotions"] = request.Promotions?.Select(promo => new Dictionary<string, object?>
            {
                ["id"] = promo.Id,
                ["involved_products"] = promo.InvolvedProducts.Select(ip => new Dictionary<string, object?>
                {
                    ["id"] = ip.Id,
                    ["count"] = ip.Count,
                    ["modification_id"] = ip.ModificationId
                }).ToList(),
                ["result_products"] = promo.ResultProducts.Select(rp => new Dictionary<string, object?>
                {
                    ["id"] = rp.Id,
                    ["count"] = rp.Count,
                    ["modification_id"] = rp.ModificationId
                }).ToList()
            }).ToList()
        };

        var payload = JsonSerializer.SerializeToElement(posterPayload);
        var posterResult = await _posterService.CreateIncomingOrderDetailedAsync(payload, cancellationToken);
        if (posterResult is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Poster service unavailable"
            });
        }

        string? incomingOrderId = null;
        string? transactionId = null;

        if (posterResult.Document is not null)
        {
            using (posterResult.Document)
            {
                var root = posterResult.Document.RootElement;
                if (root.TryGetProperty("response", out var response))
                {
                    if (response.TryGetProperty("incoming_order_id", out var incomingOrderIdProp))
                    {
                        incomingOrderId = incomingOrderIdProp.ValueKind == JsonValueKind.Number
                            ? incomingOrderIdProp.GetInt64().ToString()
                            : incomingOrderIdProp.GetString();
                    }

                    if (response.TryGetProperty("transaction_id", out var transactionIdProp))
                    {
                        transactionId = transactionIdProp.ValueKind == JsonValueKind.Number
                            ? transactionIdProp.GetInt64().ToString()
                            : transactionIdProp.GetString();
                    }
                }
            }
        }

        if (!posterResult.Success || (incomingOrderId is null && transactionId is null))
        {
            _logger.LogWarning(
                "Poster createIncomingOrder failed. StatusCode: {StatusCode}. PosterResponse: {PosterResponse}",
                (int)posterResult.StatusCode, posterResult.Body);

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Poster order could not be created",
                posterStatusCode = (int)posterResult.StatusCode,
                posterResponse = posterResult.Body
            });
        }

        var canonicalOrderNumber = transactionId ?? incomingOrderId;
        if (string.IsNullOrWhiteSpace(canonicalOrderNumber))
        {
            _logger.LogWarning(
                "Poster createIncomingOrder succeeded but returned no transaction_id/incoming_order_id. PosterResponse: {PosterResponse}",
                posterResult.Body);

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Poster order could not be created",
                posterStatusCode = (int)posterResult.StatusCode,
                posterResponse = posterResult.Body
            });
        }

        // Idempotency: if we already have this Poster order saved, return it.
        var existingOrder = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o =>
                    (transactionId != null && o.PosterTransactionId == transactionId) ||
                    (incomingOrderId != null && o.PosterIncomingOrderId == incomingOrderId) ||
                    o.OrderNumber == canonicalOrderNumber,
                cancellationToken);

        if (existingOrder is not null)
        {
            _logger.LogInformation(
                "Order already exists in DB for Poster order. OrderId {OrderId}, OrderNumber {OrderNumber}, PosterIncomingOrderId {IncomingOrderId}, PosterTransactionId {TransactionId}",
                existingOrder.Id, existingOrder.OrderNumber, existingOrder.PosterIncomingOrderId, existingOrder.PosterTransactionId);

            existingOrder = await EnsureOrderFcmTokenAsync(existingOrder, request.FcmToken, cancellationToken);
            TrackOrderForPolling(existingOrder, request.FcmToken);

            return Ok(new
            {
                response = new
                {
                    incoming_order_id = incomingOrderId,
                    transaction_id = transactionId
                },
                order = new OrderResponse
                {
                    Id = existingOrder.Id,
                    OrderNumber = existingOrder.OrderNumber,
                    Date = existingOrder.Date,
                    Subtotal = existingOrder.Subtotal,
                    DeliveryFee = existingOrder.DeliveryFee,
                    Discount = existingOrder.Discount,
                    Total = existingOrder.Total,
                    Status = existingOrder.Status.ToString().ToLowerInvariant(),
                    DeliveryAddress = existingOrder.DeliveryAddress,
                    DeliveryLatitude = existingOrder.DeliveryLatitude,
                    DeliveryLongitude = existingOrder.DeliveryLongitude,
                    DeliveryAddressComment = existingOrder.DeliveryAddressComment,
                    PaymentMethod = existingOrder.PaymentMethod,
                    PaymentMethodId = existingOrder.PaymentMethodId,
                    PosterSpotId = existingOrder.PosterSpotId,
                    PosterIncomingOrderId = existingOrder.PosterIncomingOrderId,
                    PosterTransactionId = existingOrder.PosterTransactionId,
                    Phone = existingOrder.Phone,
                    AlternatePhone = existingOrder.AlternatePhone,
                    FirstName = existingOrder.FirstName,
                    LastName = existingOrder.LastName,
                    Comment = existingOrder.Comment,
                    ServiceMode = existingOrder.ServiceMode,
                    PromoCode = existingOrder.PromoCode,
                    PromoDiscountAmount = existingOrder.PromoDiscountAmount,
                    PromoDiscountPercentage = existingOrder.PromoDiscountPercentage,
                    LoyaltyPointsSpent = existingOrder.LoyaltyPointsSpent,
                    UserId = existingOrder.UserId,
                    UserOrderCount = existingOrder.UserOrderCount,
                    FcmToken = existingOrder.FcmToken,
                    CreatedAt = existingOrder.CreatedAt,
                    UpdatedAt = existingOrder.UpdatedAt,
                    Items = existingOrder.Items.Select(item => new OrderItemResponse
                    {
                        Id = item.Id,
                        MenuItemId = item.MenuItemId,
                        Name = item.Name,
                        Quantity = item.Quantity,
                        Price = item.Price,
                        TotalPrice = item.TotalPrice,
                        Modifiers = item.Modifiers,
                        ImageUrl = item.ImageUrl,
                        IsBonus = item.IsBonus
                    }).ToList(),
                    Branch = existingOrder.BranchId is not null ? new BranchResponse
                    {
                        Id = existingOrder.BranchId,
                        Name = existingOrder.BranchName ?? string.Empty,
                        Address = existingOrder.BranchAddress ?? string.Empty,
                        Phone = existingOrder.BranchPhone
                    } : null
                }
            });
        }

        var orderId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var orderNumber = canonicalOrderNumber;

        var order = new Order
        {
            Id = orderId,
            OrderNumber = orderNumber,
            Date = now,
            Subtotal = request.Subtotal,
            DeliveryFee = request.DeliveryPrice,
            Discount = request.Discount,
            Total = request.Total,
            Status = request.PaymentMethodId == "2" ? OrderStatus.AwaitingPayment : OrderStatus.Pending,
            DeliveryAddress = request.Address ?? string.Empty,
            DeliveryLatitude = request.DeliveryLatitude,
            DeliveryLongitude = request.DeliveryLongitude,
            DeliveryAddressComment = request.DeliveryAddressComment,
            PaymentMethod = request.PaymentMethodId switch
            {
                "1" => "cash",
                "2" => "card",
                _ => "unknown"
            },
            PaymentMethodId = request.PaymentMethodId,
            SavedCardToken = request.SavedCardToken,
            BranchId = request.BranchId ?? request.SpotId,
            BranchName = request.BranchName,
            BranchAddress = request.BranchAddress,
            BranchPhone = request.BranchPhone,
            PosterSpotId = request.SpotId,
            PosterIncomingOrderId = incomingOrderId,
            PosterTransactionId = transactionId,
            Phone = request.Phone,
            AlternatePhone = request.AlternatePhone,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Comment = request.Comment,
            ServiceMode = request.ServiceMode,
            PromoCode = request.PromoCode,
            PromoDiscountAmount = request.PromoDiscountAmount,
            PromoDiscountPercentage = request.PromoDiscountPercentage,
            LoyaltyPointsSpent = request.LoyaltyPointsUsed,
            UserId = request.UserId,
            UserOrderCount = request.UserOrderCount,
            FcmToken = request.FcmToken,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var product in request.Products)
        {
            var orderItem = new OrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderId,
                MenuItemId = product.ProductId,
                Name = product.Name,
                Quantity = product.Count,
                Price = product.Price,
                TotalPrice = product.TotalPrice,
                Modifiers = product.Modifiers,
                ModifierId = product.ModificatorId,
                ImageUrl = product.ImageUrl,
                IsBonus = product.IsGift,
                PriceOverride = product.PriceOverride
            };
            order.Items.Add(orderItem);
        }

        try
        {
            await _dbContext.Orders.AddAsync(order, cancellationToken);
            var savedCount = await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "SUCCESS: Database save completed (post-Poster). Saved {Count} entities. Order {OrderId} with OrderNumber {OrderNumber}, PosterIncomingOrderId {IncomingOrderId}",
                savedCount, orderId, order.OrderNumber, incomingOrderId);
            await _orderUpdatesPublisher.PublishAsync(
                OrderUpdateEventFactory.Create(order, "created"),
                cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "DbUpdateException while saving order {OrderId} (OrderNumber {OrderNumber}). Retrying lookup by poster ids.", orderId, orderNumber);

            var retry = await _dbContext.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o =>
                        (transactionId != null && o.PosterTransactionId == transactionId) ||
                        (incomingOrderId != null && o.PosterIncomingOrderId == incomingOrderId) ||
                        o.OrderNumber == canonicalOrderNumber,
                    cancellationToken);

            if (retry is not null)
            {
                retry = await EnsureOrderFcmTokenAsync(retry, request.FcmToken, cancellationToken);
                TrackOrderForPolling(retry, request.FcmToken);

                return Ok(new
                {
                    response = new
                    {
                        incoming_order_id = incomingOrderId,
                        transaction_id = transactionId
                    },
                    order = new OrderResponse
                    {
                        Id = retry.Id,
                        OrderNumber = retry.OrderNumber,
                        Date = retry.Date,
                        Subtotal = retry.Subtotal,
                        DeliveryFee = retry.DeliveryFee,
                        Discount = retry.Discount,
                        Total = retry.Total,
                        Status = retry.Status.ToString().ToLowerInvariant(),
                        DeliveryAddress = retry.DeliveryAddress,
                        DeliveryLatitude = retry.DeliveryLatitude,
                        DeliveryLongitude = retry.DeliveryLongitude,
                        DeliveryAddressComment = retry.DeliveryAddressComment,
                        PaymentMethod = retry.PaymentMethod,
                        PaymentMethodId = retry.PaymentMethodId,
                        PosterSpotId = retry.PosterSpotId,
                        PosterIncomingOrderId = retry.PosterIncomingOrderId,
                        PosterTransactionId = retry.PosterTransactionId,
                        Phone = retry.Phone,
                        AlternatePhone = retry.AlternatePhone,
                        FirstName = retry.FirstName,
                        LastName = retry.LastName,
                        Comment = retry.Comment,
                        ServiceMode = retry.ServiceMode,
                        PromoCode = retry.PromoCode,
                        PromoDiscountAmount = retry.PromoDiscountAmount,
                        PromoDiscountPercentage = retry.PromoDiscountPercentage,
                        LoyaltyPointsSpent = retry.LoyaltyPointsSpent,
                        UserId = retry.UserId,
                        UserOrderCount = retry.UserOrderCount,
                        FcmToken = retry.FcmToken,
                        CreatedAt = retry.CreatedAt,
                        UpdatedAt = retry.UpdatedAt,
                        Items = retry.Items.Select(item => new OrderItemResponse
                        {
                            Id = item.Id,
                            MenuItemId = item.MenuItemId,
                            Name = item.Name,
                            Quantity = item.Quantity,
                            Price = item.Price,
                            TotalPrice = item.TotalPrice,
                            Modifiers = item.Modifiers,
                            ImageUrl = item.ImageUrl,
                            IsBonus = item.IsBonus
                        }).ToList(),
                        Branch = retry.BranchId is not null ? new BranchResponse
                        {
                            Id = retry.BranchId,
                            Name = retry.BranchName ?? string.Empty,
                            Address = retry.BranchAddress ?? string.Empty,
                            Phone = retry.BranchPhone
                        } : null
                    }
                });
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Order created in Poster but failed to save to backend database",
                posterIncomingOrderId = incomingOrderId,
                posterTransactionId = transactionId,
                databaseError = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FAILED to save order {OrderId} to database (post-Poster). Error: {Error}", orderId, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Order created in Poster but failed to save to backend database",
                posterIncomingOrderId = incomingOrderId,
                posterTransactionId = transactionId,
                databaseError = ex.Message
            });
        }

        TrackOrderForPolling(order, request.FcmToken);

        return Ok(new
        {
            response = new
            {
                incoming_order_id = incomingOrderId,
                transaction_id = transactionId
            },
            order = new OrderResponse
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                Date = order.Date,
                Subtotal = order.Subtotal,
                DeliveryFee = order.DeliveryFee,
                Discount = order.Discount,
                Total = order.Total,
                Status = order.Status.ToString().ToLowerInvariant(),
                DeliveryAddress = order.DeliveryAddress,
                DeliveryLatitude = order.DeliveryLatitude,
                DeliveryLongitude = order.DeliveryLongitude,
                DeliveryAddressComment = order.DeliveryAddressComment,
                PaymentMethod = order.PaymentMethod,
                PaymentMethodId = order.PaymentMethodId,
                PosterSpotId = order.PosterSpotId,
                PosterIncomingOrderId = order.PosterIncomingOrderId,
                PosterTransactionId = order.PosterTransactionId,
                Phone = order.Phone,
                AlternatePhone = order.AlternatePhone,
                FirstName = order.FirstName,
                LastName = order.LastName,
                Comment = order.Comment,
                ServiceMode = order.ServiceMode,
                PromoCode = order.PromoCode,
                PromoDiscountAmount = order.PromoDiscountAmount,
                PromoDiscountPercentage = order.PromoDiscountPercentage,
                LoyaltyPointsSpent = order.LoyaltyPointsSpent,
                UserId = order.UserId,
                UserOrderCount = order.UserOrderCount,
                FcmToken = order.FcmToken,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
                Items = order.Items.Select(item => new OrderItemResponse
                {
                    Id = item.Id,
                    MenuItemId = item.MenuItemId,
                    Name = item.Name,
                    Quantity = item.Quantity,
                    Price = item.Price,
                    TotalPrice = item.TotalPrice,
                    Modifiers = item.Modifiers,
                    ImageUrl = item.ImageUrl,
                    IsBonus = item.IsBonus
                }).ToList(),
                Branch = order.BranchId is not null ? new BranchResponse
                {
                    Id = order.BranchId,
                    Name = order.BranchName ?? string.Empty,
                    Address = order.BranchAddress ?? string.Empty,
                    Phone = order.BranchPhone
                } : null
            }
        });
    }

    private IActionResult CreatePosterResponse(JsonDocument? document, bool unwrapResponse = true)
    {
        if (document is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Poster service unavailable" });
        }

        using (document)
        {
            var root = document.RootElement;
            if (unwrapResponse && root.TryGetProperty("response", out var response))
            {
                return Ok(response.Clone());
            }

            return Ok(root.Clone());
        }
    }

    private static string SanitizePosterPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return string.Empty;
        }

        var digits = new StringBuilder(phoneNumber.Length);
        foreach (var ch in phoneNumber)
        {
            if (char.IsDigit(ch))
            {
                digits.Append(ch);
            }
        }

        var normalized = digits.ToString();
        if (normalized.Length == 9 && !normalized.StartsWith("998", StringComparison.Ordinal))
        {
            normalized = "998" + normalized;
        }

        return normalized;
    }

    private static string ConvertPhoneNumber(string phoneNumber)
    {
        var normalized = phoneNumber.Trim();
        if (normalized.StartsWith("+", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        if (!normalized.StartsWith("998", StringComparison.Ordinal) && normalized.Length < 12)
        {
            normalized = $"998{normalized}";
        }

        return normalized;
    }

    /// <summary>
    /// Mock endpoint for creating orders without calling Poster API.
    /// Saves order to backend database only with fake Poster IDs.
    /// </summary>
    [HttpPost("api/posttoposter/mock")]
    public async Task<IActionResult> PostToPosterMockAsync([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("=== MOCK ORDER REQUEST (No Poster API call) ===");
        _logger.LogInformation("SpotId: {SpotId}, Phone: {Phone}, Products: {Count}",
            request.SpotId, request.Phone, request.Products.Count);

        UpdateTokenLanguageFromRequest(request);

        // Generate fake Poster IDs
        var random = new Random();
        var fakeIncomingOrderId = random.Next(10000, 99999).ToString();
        var fakeTransactionId = random.Next(100000, 999999).ToString();

        // Create order entity
        var orderId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var orderNumber = fakeIncomingOrderId;

        var order = new Order
        {
            Id = orderId,
            OrderNumber = orderNumber,
            Date = now,
            Subtotal = request.Subtotal,
            DeliveryFee = request.DeliveryPrice,
            Discount = request.Discount,
            Total = request.Total,
            Status = request.PaymentMethodId == "2" ? OrderStatus.AwaitingPayment : OrderStatus.Pending,
            DeliveryAddress = request.Address ?? string.Empty,
            DeliveryLatitude = request.DeliveryLatitude,
            DeliveryLongitude = request.DeliveryLongitude,
            DeliveryAddressComment = request.DeliveryAddressComment,
            PaymentMethod = request.PaymentMethodId switch
            {
                "1" => "cash",
                "2" => "card",
                _ => "unknown"
            },
            PaymentMethodId = request.PaymentMethodId,
            SavedCardToken = request.SavedCardToken,
            BranchId = request.BranchId ?? request.SpotId,
            BranchName = request.BranchName,
            BranchAddress = request.BranchAddress,
            BranchPhone = request.BranchPhone,
            PosterSpotId = request.SpotId,
            PosterIncomingOrderId = fakeIncomingOrderId,
            PosterTransactionId = fakeTransactionId,
            Phone = request.Phone,
            AlternatePhone = request.AlternatePhone,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Comment = request.Comment,
            ServiceMode = request.ServiceMode,
            PromoCode = request.PromoCode,
            PromoDiscountAmount = request.PromoDiscountAmount,
            PromoDiscountPercentage = request.PromoDiscountPercentage,
            LoyaltyPointsSpent = request.LoyaltyPointsUsed,
            UserId = request.UserId,
            UserOrderCount = request.UserOrderCount,
            FcmToken = request.FcmToken,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Create order items
        foreach (var product in request.Products)
        {
            var orderItem = new OrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderId,
                MenuItemId = product.ProductId,
                Name = product.Name,
                Quantity = product.Count,
                Price = product.Price,
                TotalPrice = product.TotalPrice,
                Modifiers = product.Modifiers,
                ModifierId = product.ModificatorId,
                ImageUrl = product.ImageUrl,
                IsBonus = product.IsGift,
                PriceOverride = product.PriceOverride
            };
            order.Items.Add(orderItem);
        }

        // Save to database
        try
        {
            await _dbContext.Orders.AddAsync(order, cancellationToken);
            var savedCount = await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "MOCK SUCCESS: Database save completed. Saved {Count} entities. Order {OrderId} with OrderNumber {OrderNumber}, FakeIncomingOrderId {IncomingOrderId}, FakeTransactionId {TransactionId}",
                savedCount, orderId, orderNumber, fakeIncomingOrderId, fakeTransactionId);
            await _orderUpdatesPublisher.PublishAsync(
                OrderUpdateEventFactory.Create(order, "created"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MOCK FAILED to save order {OrderId} to database. Error: {Error}", orderId, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Failed to save mock order to backend database",
                databaseError = ex.Message
            });
        }

        TrackOrderForPolling(order, request.FcmToken);

        // Return response matching the real endpoint format
        return Ok(new
        {
            response = new
            {
                incoming_order_id = fakeIncomingOrderId,
                transaction_id = fakeTransactionId
            },
            order = new OrderResponse
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                Date = order.Date,
                Subtotal = order.Subtotal,
                DeliveryFee = order.DeliveryFee,
                Discount = order.Discount,
                Total = order.Total,
                Status = order.Status.ToString().ToLowerInvariant(),
                DeliveryAddress = order.DeliveryAddress,
                DeliveryLatitude = order.DeliveryLatitude,
                DeliveryLongitude = order.DeliveryLongitude,
                DeliveryAddressComment = order.DeliveryAddressComment,
                PaymentMethod = order.PaymentMethod,
                PaymentMethodId = order.PaymentMethodId,
                PosterSpotId = order.PosterSpotId,
                PosterIncomingOrderId = order.PosterIncomingOrderId,
                PosterTransactionId = order.PosterTransactionId,
                Phone = order.Phone,
                AlternatePhone = order.AlternatePhone,
                FirstName = order.FirstName,
                LastName = order.LastName,
                Comment = order.Comment,
                ServiceMode = order.ServiceMode,
                PromoCode = order.PromoCode,
                PromoDiscountAmount = order.PromoDiscountAmount,
                PromoDiscountPercentage = order.PromoDiscountPercentage,
                LoyaltyPointsSpent = order.LoyaltyPointsSpent,
                UserId = order.UserId,
                UserOrderCount = order.UserOrderCount,
                FcmToken = order.FcmToken,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
                Items = order.Items.Select(item => new OrderItemResponse
                {
                    Id = item.Id,
                    MenuItemId = item.MenuItemId,
                    Name = item.Name,
                    Quantity = item.Quantity,
                    Price = item.Price,
                    TotalPrice = item.TotalPrice,
                    Modifiers = item.Modifiers,
                    ImageUrl = item.ImageUrl,
                    IsBonus = item.IsBonus
                }).ToList(),
                Branch = order.BranchId is not null ? new BranchResponse
                {
                    Id = order.BranchId,
                    Name = order.BranchName ?? string.Empty,
                    Address = order.BranchAddress ?? string.Empty,
                    Phone = order.BranchPhone
                } : null
            }
        });
    }

    private static bool ShouldTrackOrder(OrderStatus status) =>
        status is not OrderStatus.Delivered and not OrderStatus.Cancelled;

    private void TrackOrderForPolling(Order order, string? fcmTokenOverride = null)
    {
        if (!ShouldTrackOrder(order.Status))
        {
            return;
        }

        var fcmToken = string.IsNullOrWhiteSpace(fcmTokenOverride) ? order.FcmToken : fcmTokenOverride;
        var language = ResolveLanguage(fcmToken);

        _orderTracker.TrackOrder(new TrackedOrder
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            PosterIncomingOrderId = order.PosterIncomingOrderId,
            PosterTransactionId = order.PosterTransactionId,
            FcmToken = fcmToken,
            Phone = order.Phone,
            CurrentStatus = order.Status,
            Language = language
        });
    }

    private string ResolveLanguage(string? fcmToken)
    {
        if (!string.IsNullOrWhiteSpace(fcmToken) &&
            _tokenStore.TryGet(fcmToken, out var entry))
        {
            var language = entry.Language;
            if (!string.IsNullOrWhiteSpace(language) && NotificationService.IsLanguageSupported(language))
            {
                return language.Trim().ToLowerInvariant();
            }
        }

        return "en";
    }

    private void UpdateTokenLanguageFromRequest(CreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FcmToken))
        {
            return;
        }

        var language = ResolveRequestLanguage(request.Language);
        if (language is null)
        {
            return;
        }

        if (_tokenStore.TryGet(request.FcmToken, out var entry))
        {
            _tokenStore.AddOrUpdate(request.FcmToken, entry with
            {
                Language = language,
                UserId = request.UserId ?? entry.UserId
            });
            return;
        }

        _tokenStore.AddOrUpdate(request.FcmToken, new NotificationTokenEntry(
            language,
            request.UserId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null));
    }

    private static string? ResolveRequestLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var trimmed = language.Trim().ToLowerInvariant();
        return NotificationService.IsLanguageSupported(trimmed) ? trimmed : null;
    }

    private async Task<Order> EnsureOrderFcmTokenAsync(
        Order order,
        string? fcmToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fcmToken) ||
            string.Equals(order.FcmToken, fcmToken, StringComparison.Ordinal))
        {
            return order;
        }

        var tracked = await _dbContext.Orders.FindAsync(new object[] { order.Id }, cancellationToken);
        if (tracked is null)
        {
            return order;
        }

        try
        {
            tracked.FcmToken = fcmToken;
            tracked.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            order.FcmToken = fcmToken;
            order.UpdatedAt = tracked.UpdatedAt;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update FCM token for order {OrderId}", order.Id);
        }

        return order;
    }
}
