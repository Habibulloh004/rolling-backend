using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Orders;

internal static class PaymentOrderBuilder
{
    public static async Task<Order?> EnsureAwaitingPaymentOrderAsync(
        AppDbContext dbContext,
        PaymentTransaction transaction,
        JsonElement? orderDetails,
        CancellationToken cancellationToken)
    {
        var existing = await FindOrderAsync(dbContext, transaction.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var order = BuildOrder(transaction, orderDetails, OrderStatus.AwaitingPayment, null, null);
        await dbContext.Orders.AddAsync(order, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return order;
    }

    public static async Task<Order?> EnsurePaidOrderAsync(
        AppDbContext dbContext,
        PaymentTransaction transaction,
        JsonElement? orderDetails,
        string? posterTransactionId,
        string? posterIncomingOrderId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var existing = await FindOrderAsync(dbContext, transaction.Id, cancellationToken);

        if (existing is null)
        {
            var order = BuildOrder(transaction, orderDetails, OrderStatus.Pending, posterTransactionId, posterIncomingOrderId);
            await dbContext.Orders.AddAsync(order, cancellationToken);
        }
        else
        {
            existing.Status = OrderStatus.Pending;
            existing.UpdatedAt = now;

            if (string.IsNullOrWhiteSpace(existing.PaymentTransactionId))
            {
                existing.PaymentTransactionId = transaction.Id;
            }

            if (!string.IsNullOrWhiteSpace(posterIncomingOrderId))
            {
                existing.PosterIncomingOrderId = posterIncomingOrderId;
            }

            if (!string.IsNullOrWhiteSpace(posterTransactionId))
            {
                existing.PosterTransactionId = posterTransactionId;
            }

            var canonicalNumber = posterTransactionId ?? posterIncomingOrderId;
            if (!string.IsNullOrWhiteSpace(canonicalNumber) &&
                (string.IsNullOrWhiteSpace(existing.OrderNumber) || existing.OrderNumber == transaction.Id))
            {
                existing.OrderNumber = canonicalNumber;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    private static Task<Order?> FindOrderAsync(AppDbContext dbContext, string transactionId, CancellationToken cancellationToken)
    {
        return dbContext.Orders.FirstOrDefaultAsync(order =>
                order.PaymentTransactionId == transactionId ||
                order.OrderNumber == transactionId,
            cancellationToken);
    }

    private static Order BuildOrder(
        PaymentTransaction transaction,
        JsonElement? orderDetails,
        OrderStatus status,
        string? posterTransactionId,
        string? posterIncomingOrderId)
    {
        var snapshot = BuildSnapshot(transaction, orderDetails);
        var now = DateTime.UtcNow;

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            OrderNumber = posterTransactionId ?? posterIncomingOrderId ?? transaction.Id,
            Date = now,
            Subtotal = snapshot.Subtotal,
            DeliveryFee = snapshot.DeliveryFee,
            Discount = snapshot.Discount,
            Total = snapshot.Total,
            Status = status,
            DeliveryAddress = snapshot.Address ?? string.Empty,
            DeliveryLatitude = snapshot.DeliveryLatitude,
            DeliveryLongitude = snapshot.DeliveryLongitude,
            DeliveryAddressComment = snapshot.DeliveryAddressComment,
            PaymentMethod = ResolvePaymentMethod(snapshot.PaymentMethodId, transaction.Provider),
            PaymentMethodId = snapshot.PaymentMethodId,
            PaymentTransactionId = transaction.Id,
            BranchId = snapshot.BranchId ?? snapshot.SpotId,
            BranchName = snapshot.BranchName,
            BranchAddress = snapshot.BranchAddress,
            BranchPhone = snapshot.BranchPhone,
            PosterSpotId = snapshot.SpotId,
            PosterIncomingOrderId = posterIncomingOrderId,
            PosterTransactionId = posterTransactionId,
            Phone = snapshot.Phone ?? string.Empty,
            AlternatePhone = snapshot.AlternatePhone,
            FirstName = snapshot.FirstName,
            LastName = snapshot.LastName,
            Comment = snapshot.Comment,
            ServiceMode = snapshot.ServiceMode,
            PromoCode = snapshot.PromoCode,
            PromoDiscountAmount = snapshot.PromoDiscountAmount,
            PromoDiscountPercentage = snapshot.PromoDiscountPercentage,
            LoyaltyPointsSpent = snapshot.LoyaltyPointsUsed,
            UserId = transaction.UserId,
            FcmToken = snapshot.FcmToken,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var item in BuildItems(order.Id, orderDetails))
        {
            order.Items.Add(item);
        }

        return order;
    }

    private static OrderSnapshot BuildSnapshot(PaymentTransaction transaction, JsonElement? orderDetails)
    {
        var root = orderDetails.HasValue && orderDetails.Value.ValueKind == JsonValueKind.Object
            ? orderDetails.Value
            : default;

        var paymentMethodId = GetString(root, "payment_method_id", "paymentMethodId");
        var spotId = GetString(root, "spot_id", "spotId");
        var address = GetString(root, "address", "delivery_address");
        var phone = GetString(root, "phone");
        var alternatePhone = GetString(root, "alternate_phone", "alternatePhone");
        var firstName = GetString(root, "first_name", "firstName");
        var lastName = GetString(root, "last_name", "lastName");
        var comment = GetString(root, "comment");
        var deliveryAddressComment = GetString(root, "delivery_address_comment", "deliveryAddressComment");
        var promoCode = GetString(root, "promo_code", "promoCode");
        var branchId = GetString(root, "branch_id", "branchId");
        var branchName = GetString(root, "branch_name", "branchName");
        var branchAddress = GetString(root, "branch_address", "branchAddress");
        var branchPhone = GetString(root, "branch_phone", "branchPhone");
        var fcmToken = GetString(root, "fcm_token", "fcmToken");

        var deliveryFee = NormalizeAmount(GetDecimal(root, "delivery_price", "deliveryPrice") ?? 0m, transaction.Amount);
        var total = NormalizeAmount(GetDecimal(root, "total") ?? transaction.Amount, transaction.Amount);
        var discount = NormalizeAmount(GetDecimal(root, "discount") ?? 0m, total);
        var subtotal = NormalizeAmount(GetDecimal(root, "subtotal") ?? (total - deliveryFee + discount), total);

        if (subtotal < 0)
        {
            subtotal = 0m;
        }

        var serviceMode = GetInt(root, "service_mode", "serviceMode") ?? 3;
        var deliveryLatitude = GetDouble(root, "delivery_latitude", "deliveryLatitude");
        var deliveryLongitude = GetDouble(root, "delivery_longitude", "deliveryLongitude");
        var promoDiscountAmount = GetDecimal(root, "promo_discount_amount", "promoDiscountAmount");
        var promoDiscountPercentage = GetDouble(root, "promo_discount_percentage", "promoDiscountPercentage");
        var loyaltyPointsUsed = GetDecimal(root, "loyalty_points_used", "loyaltyPointsUsed");

        return new OrderSnapshot(
            spotId,
            phone,
            alternatePhone,
            firstName,
            lastName,
            address,
            comment,
            deliveryFee,
            subtotal,
            discount,
            total,
            paymentMethodId,
            serviceMode,
            deliveryLatitude,
            deliveryLongitude,
            deliveryAddressComment,
            loyaltyPointsUsed,
            promoCode,
            promoDiscountAmount,
            promoDiscountPercentage,
            branchId,
            branchName,
            branchAddress,
            branchPhone,
            fcmToken);
    }

    private static IEnumerable<OrderItem> BuildItems(string orderId, JsonElement? orderDetails)
    {
        if (!orderDetails.HasValue || orderDetails.Value.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (!orderDetails.Value.TryGetProperty("products", out var products) ||
            products.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var product in products.EnumerateArray())
        {
            var productId = GetString(product, "product_id", "productId");
            if (string.IsNullOrWhiteSpace(productId))
            {
                continue;
            }

            var count = GetInt(product, "count") ?? 0;
            if (count <= 0)
            {
                continue;
            }

            var priceOverride = GetDecimal(product, "price_override", "priceOverride");
            var isGift = GetBool(product, "is_gift", "isGift");
            var modifierId = GetString(product, "modificator_id", "modificatorId");

            var unitPrice = priceOverride ?? 0m;

            yield return new OrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderId,
                MenuItemId = productId,
                Name = string.Empty,
                Quantity = count,
                Price = unitPrice,
                TotalPrice = unitPrice * count,
                ModifierId = modifierId,
                IsBonus = isGift,
                PriceOverride = priceOverride
            };
        }
    }

    private static string ResolvePaymentMethod(string? paymentMethodId, string provider)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            return provider;
        }

        return paymentMethodId switch
        {
            "1" => "cash",
            "2" => "card",
            "4" => "payme",
            "5" => "click",
            _ => "unknown"
        };
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            else if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetRawText();
            }
        }

        return null;
    }

    private static decimal? GetDecimal(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var numeric))
            {
                return numeric;
            }

            if (element.ValueKind == JsonValueKind.String &&
                decimal.TryParse(element.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static double? GetDouble(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var numeric))
            {
                return numeric;
            }

            if (element.ValueKind == JsonValueKind.String &&
                double.TryParse(element.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? GetInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numeric))
            {
                return numeric;
            }

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool GetBool(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (element.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numeric))
            {
                return numeric != 0;
            }

            if (element.ValueKind == JsonValueKind.String &&
                bool.TryParse(element.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return false;
    }

    private static decimal NormalizeAmount(decimal value, decimal reference)
    {
        if (value <= 0)
        {
            return value;
        }

        if (reference > 0 && value > reference * 3m)
        {
            return Math.Round(value / 100m, 2, MidpointRounding.AwayFromZero);
        }

        return value;
    }

    private sealed record OrderSnapshot(
        string? SpotId,
        string? Phone,
        string? AlternatePhone,
        string? FirstName,
        string? LastName,
        string? Address,
        string? Comment,
        decimal DeliveryFee,
        decimal Subtotal,
        decimal Discount,
        decimal Total,
        string? PaymentMethodId,
        int ServiceMode,
        double? DeliveryLatitude,
        double? DeliveryLongitude,
        string? DeliveryAddressComment,
        decimal? LoyaltyPointsUsed,
        string? PromoCode,
        decimal? PromoDiscountAmount,
        double? PromoDiscountPercentage,
        string? BranchId,
        string? BranchName,
        string? BranchAddress,
        string? BranchPhone,
        string? FcmToken);
}
