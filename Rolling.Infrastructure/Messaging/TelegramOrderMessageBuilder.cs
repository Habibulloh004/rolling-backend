using System.Collections.Generic;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Messaging;

public static class TelegramOrderMessageBuilder
{
    public sealed record OrderContext(
        string OrderId,
        string BranchName,
        string Phone,
        string Address,
        string? AddressComment,
        string OrderSummary,
        string? MapLink,
        double? DistanceKm,
        decimal Subtotal,
        decimal Discount,
        decimal DeliveryFee,
        decimal Total,
        string PaymentDescription,
        decimal Bonuses,
        string OrderType,
        int? OrdersCompleted,
        string? PromoCode);

    public static async Task<OrderContext> CreateContextAsync(
        AppDbContext dbContext,
        Order order,
        string orderSummary,
        string paymentDescription,
        CancellationToken cancellationToken)
    {
        var branch = await ResolveBranchConfigurationAsync(dbContext, order, cancellationToken);
        return CreateContext(order, branch, orderSummary, paymentDescription);
    }

    public static OrderContext CreateContext(
        Order order,
        BranchConfiguration? branch,
        string orderSummary,
        string paymentDescription)
    {
        var orderId = !string.IsNullOrWhiteSpace(order.OrderNumber) ? order.OrderNumber : order.Id;
        var branchName = ResolveBranchName(order, branch);
        var phone = ResolvePhone(order);
        var address = ResolveValue(order.DeliveryAddress, "Не указан");
        var addressComment = ResolveOptional(order.DeliveryAddressComment) ?? ResolveOptional(order.Comment);
        var mapLink = BuildMapLink(order.DeliveryLatitude, order.DeliveryLongitude);
        var distanceKm = CalculateDistanceKm(
            branch?.Latitude,
            branch?.Longitude,
            order.DeliveryLatitude,
            order.DeliveryLongitude);
        var bonuses = order.LoyaltyPointsSpent ?? 0m;
        var promoCode = ResolveOptional(order.PromoCode);

        return new OrderContext(
            OrderId: orderId,
            BranchName: branchName,
            Phone: phone,
            Address: address,
            AddressComment: addressComment,
            OrderSummary: orderSummary,
            MapLink: mapLink,
            DistanceKm: distanceKm,
            Subtotal: order.Subtotal,
            Discount: order.Discount,
            DeliveryFee: order.DeliveryFee,
            Total: order.Total,
            PaymentDescription: paymentDescription,
            Bonuses: bonuses,
            OrderType: "Доставка",
            OrdersCompleted: order.UserOrderCount,
            PromoCode: promoCode);
    }

    public static string BuildPaymentDescription(Order order, string? providerOverride = null)
    {
        var provider = (providerOverride ?? order.PaymentMethod ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(provider))
        {
            switch (provider)
            {
                case "payme":
                    return "Payme (Оплачено)";
                case "click":
                    return "Click (Оплачено)";
                case "cash":
                    return "Наличные";
                case "card":
                    return "Карта";
            }
        }

        if (!string.IsNullOrWhiteSpace(order.PaymentMethodId))
        {
            return order.PaymentMethodId switch
            {
                "1" => "Наличные",
                "2" => "Карта",
                "3" => "Карта",
                "4" => "Payme (Оплачено)",
                "5" => "Click (Оплачено)",
                _ => ResolveValue(order.PaymentMethod, "Не указан")
            };
        }

        return ResolveValue(order.PaymentMethod, "Не указан");
    }

    public static string BuildOrderSummary(Order order, string paymentDescription)
    {
        var phone = ResolvePhone(order);
        var sourceLabel = ResolveOrderSourceLabel(order);
        var orderTypeDescription = ResolveOrderTypeDescription(sourceLabel);
        return $"""
Тип оплаты : {paymentDescription}
Номер телефона : {phone}
Тип заказа: {orderTypeDescription}
""".Trim();
    }

    public static string BuildNewOrderMessage(OrderContext context)
    {
        var distanceText = context.DistanceKm.HasValue
            ? $"{context.DistanceKm.Value.ToString("0.0", CultureInfo.InvariantCulture)} км"
            : "Не указан";
        var mapLinkLine = string.IsNullOrWhiteSpace(context.MapLink) ? "Не указан" : context.MapLink!;
        var addressComment = ResolveValue(context.AddressComment, "Не указан");
        var orderAmount = ResolveOrderAmountForDisplay(context);
        var sourceLabel = ResolveSourceLabelFromSummary(context.OrderSummary);

        var lines = new List<string>
        {
            $"📦 Новый заказ №{context.OrderId}",
            $"🛒 Название филиал: {context.BranchName}",
            $"📞 Телефон: {context.Phone}",
            $"🏠 Адрес: {context.Address}",
            $"🔗 [Посмотреть на карте] {mapLinkLine}",
            $"🗺️ Расстояние: {distanceText}",
            $"💵 Сумма заказа: {FormatAmount(orderAmount)}",
            $"💳 Метод оплаты: {context.PaymentDescription}",
            $"🎁 Бонусы: {FormatAmount(context.Bonuses)}",
            $"💵 К оплате: {FormatAmount(context.Total)}",
            $"🛍 Тип заказа: {context.OrderType}",
            $"📲 Источник: {sourceLabel}",
            $"🚚 Доставка: {FormatAmount(context.DeliveryFee)}",
            $"✏️ Комментарий: {context.OrderSummary}"
        };

        var ordersCompleted = Math.Max(context.OrdersCompleted ?? 0, 0);
        lines.Add($"📦 Количество заказов: {ordersCompleted}");

        if (!string.IsNullOrWhiteSpace(context.PromoCode))
        {
            lines.Add($"🏷 Промокод: {context.PromoCode}");
        }

        lines.Add($"✏️ Комментарий к адресу: {addressComment}");

        return string.Join("\n", lines);
    }

    public static string BuildCancelOrderMessage(OrderContext context)
    {
        var isCancelledByClient = context.OrderSummary.Contains("клиентом", StringComparison.OrdinalIgnoreCase);
        var headerIcon = isCancelledByClient ? "❌" : "⚠️";
        var headerText = isCancelledByClient ? "ЗАКАЗ ОТМЕНЁН КЛИЕНТОМ" : "ЗАКАЗ ОТМЕНЁН АДМИНИСТРАТОРОМ";
        var sourceLabel = ResolveSourceLabelFromSummary(context.OrderSummary);

        var lines = new List<string>
        {
            $"{headerIcon} {headerText}",
            string.Empty,
            $"📦 Номер заказа: #{context.OrderId}",
            $"🛒 Филиал: {context.BranchName}",
            $"📞 Телефон клиента: {context.Phone}",
            $"🏠 Адрес: {context.Address}",
            $"💵 Сумма заказа: {FormatAmount(context.Total)}",
            $"💳 Метод оплаты: {context.PaymentDescription}"
        };

        if (context.Bonuses > 0m)
        {
            lines.Add($"🎁 Бонусы: {FormatAmount(context.Bonuses)}");
        }

        lines.Add($"📲 Источник: {sourceLabel}");
        lines.Add(string.Empty);
        lines.Add($"⚠️ {context.OrderSummary}");

        return string.Join("\n", lines);
    }

    public static string? BuildMapLink(double? latitude, double? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue)
        {
            return null;
        }

        if (double.IsNaN(latitude.Value) || double.IsNaN(longitude.Value))
        {
            return null;
        }

        return $"https://yandex.com/maps/?pt={longitude.Value.ToString(CultureInfo.InvariantCulture)},{latitude.Value.ToString(CultureInfo.InvariantCulture)}&z=16&l=map";
    }

    public static double? CalculateDistanceKm(
        double? branchLatitude,
        double? branchLongitude,
        double? destinationLatitude,
        double? destinationLongitude)
    {
        if (!branchLatitude.HasValue || !branchLongitude.HasValue ||
            !destinationLatitude.HasValue || !destinationLongitude.HasValue)
        {
            return null;
        }

        var lat1 = DegreesToRadians(branchLatitude.Value);
        var lon1 = DegreesToRadians(branchLongitude.Value);
        var lat2 = DegreesToRadians(destinationLatitude.Value);
        var lon2 = DegreesToRadians(destinationLongitude.Value);

        var deltaLat = lat2 - lat1;
        var deltaLon = lon2 - lon1;

        var a = Math.Pow(Math.Sin(deltaLat / 2), 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLon / 2), 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        var distance = 6371.0 * c;

        return double.IsFinite(distance) ? distance : null;
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180.0;

    private static string ResolveBranchName(Order order, BranchConfiguration? branch)
    {
        if (!string.IsNullOrWhiteSpace(order.BranchName))
        {
            return order.BranchName;
        }

        if (branch != null)
        {
            return ResolveValue(branch.NameRu, ResolveValue(branch.NameEn, ResolveValue(branch.NameUz, "Не указан")));
        }

        return "Не указан";
    }

    private static string ResolvePhone(Order order)
    {
        if (!string.IsNullOrWhiteSpace(order.Phone))
        {
            return order.Phone;
        }

        if (!string.IsNullOrWhiteSpace(order.AlternatePhone))
        {
            return order.AlternatePhone;
        }

        return "Не указан";
    }

    private static string ResolveOrderSourceLabel(Order order)
    {
        var source = (order.Comment ?? string.Empty).ToLowerInvariant();

        if (source.Contains("веб-сайт") || source.Contains("веб сайт") || source.Contains("website"))
        {
            return "Веб-сайт";
        }

        if (source.Contains("kotlin"))
        {
            return "Мобильное приложение (Kotlin)";
        }

        if (source.Contains("swift"))
        {
            return "Мобильное приложение (Swift)";
        }

        return "Мобильное приложение (Swift)";
    }

    private static string ResolveOrderTypeDescription(string sourceLabel)
    {
        return sourceLabel switch
        {
            "Веб-сайт" => "Через веб-сайт",
            "Мобильное приложение (Kotlin)" => "Через мобильное приложение (Kotlin)",
            _ => "Через мобильное приложение (Swift)"
        };
    }

    private static string ResolveSourceLabelFromSummary(string? orderSummary)
    {
        var normalized = (orderSummary ?? string.Empty).ToLowerInvariant();

        if (normalized.Contains("веб-сайт") || normalized.Contains("веб сайт") || normalized.Contains("website"))
        {
            return "Веб-сайт";
        }

        if (normalized.Contains("kotlin"))
        {
            return "Мобильное приложение (Kotlin)";
        }

        if (normalized.Contains("swift"))
        {
            return "Мобильное приложение (Swift)";
        }

        return "Мобильное приложение (Swift)";
    }

    private static string ResolveValue(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static string? ResolveOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string FormatAmount(decimal value)
    {
        var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        var formatInfo = new NumberFormatInfo
        {
            NumberGroupSeparator = " ",
            NumberDecimalDigits = 0
        };
        return $"{rounded.ToString("N0", formatInfo)} сум";
    }

    private static decimal ResolveOrderAmountForDisplay(OrderContext context)
    {
        var derived = context.Total - context.DeliveryFee + context.Discount;
        if (derived < 0m)
        {
            derived = 0m;
        }

        return Math.Max(context.Subtotal, derived);
    }

    private static async Task<BranchConfiguration?> ResolveBranchConfigurationAsync(
        AppDbContext dbContext,
        Order order,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(order.PosterSpotId) &&
            int.TryParse(order.PosterSpotId, out var spotId))
        {
            return await dbContext.BranchConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(branch => branch.SpotId == spotId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(order.BranchId) &&
            int.TryParse(order.BranchId, out var branchId))
        {
            return await dbContext.BranchConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(branch => branch.Id == branchId, cancellationToken);
        }

        return null;
    }
}
