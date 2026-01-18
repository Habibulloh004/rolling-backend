using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Orders;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Infrastructure.Poster;

namespace Rolling.Web.HostedServices;

/// <summary>
/// Background service that polls Poster API for order status changes
/// and sends push notifications to clients when status changes.
/// </summary>
public sealed class OrderStatusPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActiveOrderTracker _orderTracker;
    private readonly NotificationTokenStore _tokenStore;
    private readonly OrderPollingOptions _options;
    private readonly ILogger<OrderStatusPollingService> _logger;

    public OrderStatusPollingService(
        IServiceScopeFactory scopeFactory,
        ActiveOrderTracker orderTracker,
        NotificationTokenStore tokenStore,
        IOptions<OrderPollingOptions> options,
        ILogger<OrderStatusPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _orderTracker = orderTracker;
        _tokenStore = tokenStore;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Order status polling is disabled");
            return;
        }

        _logger.LogInformation(
            "Order status polling started. Interval: {Interval}s, LookbackDays: {LookbackDays}d, Max tracking: {MaxMinutes}m",
            _options.PollingIntervalSeconds,
            _options.TransactionsLookbackDays,
            _options.MaxTrackingMinutes);

        try
        {
            await HydrateTrackedOrdersAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hydrate active orders for polling");
        }

        var interval = TimeSpan.FromSeconds(_options.PollingIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PollOrderStatusesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during order status polling");
            }
        }
    }

    private async Task PollOrderStatusesAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var posterService = scope.ServiceProvider.GetRequiredService<PosterService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        PruneTrackedOrders(now);
        var lookbackDays = Math.Max(1, _options.TransactionsLookbackDays);
        var cutoff = now.AddDays(-lookbackDays);

        var trackedOrders = _orderTracker.GetTrackedOrders();
        if (trackedOrders.Count == 0)
        {
            return;
        }

        trackedOrders = await SyncTrackedOrdersFromDbAsync(dbContext, trackedOrders, cancellationToken);
        if (trackedOrders.Count == 0)
        {
            return;
        }

        var ordersWithPosterIds = trackedOrders
            .Where(order => !string.IsNullOrWhiteSpace(order.PosterTransactionId) ||
                !string.IsNullOrWhiteSpace(order.PosterIncomingOrderId))
            .ToList();

        if (ordersWithPosterIds.Count == 0)
        {
            return;
        }

        _logger.LogDebug(
            "Polling Poster statuses for {OrderCount} tracked orders (lookback {LookbackDays}d)",
            ordersWithPosterIds.Count,
            lookbackDays);

        var dateFrom = cutoff.ToString("yyyy-MM-dd");
        var dateTo = now.ToString("yyyy-MM-dd");

        var transactionsResponse = await posterService.GetTransactionsAsync(
            courierId: string.Empty,
            dateFrom: dateFrom,
            dateTo: dateTo,
            cancellationToken,
            includeDelivery: true,
            includeProducts: false,
            includeHistory: true);

        if (transactionsResponse == null)
        {
            _logger.LogWarning(
                "Poster transactions unavailable for {DateFrom}..{DateTo}",
                dateFrom,
                dateTo);
            return;
        }

        using (transactionsResponse)
        {
            if (!TryBuildPosterStatusLookups(
                    transactionsResponse,
                    out var transactionStatuses,
                    out var incomingOrderStatuses,
                    out var transactionCount))
            {
                _logger.LogWarning("Poster transactions response did not include a transaction list");
                return;
            }

            _logger.LogDebug("Poster transactions fetched: {TransactionCount}", transactionCount);

            foreach (var order in ordersWithPosterIds)
            {
                try
                {
                    var posterStatus = ResolvePosterStatus(
                        order.PosterTransactionId,
                        order.PosterIncomingOrderId,
                        transactionStatuses,
                        incomingOrderStatuses);
                    if (posterStatus == null)
                    {
                        continue;
                    }

                    if (!ShouldUpdateStatus(order.CurrentStatus, posterStatus.Value))
                    {
                        continue;
                    }

                    var dbUpdated = await UpdateOrderStatusInDbAsync(
                        dbContext,
                        order.OrderId,
                        posterStatus.Value,
                        cancellationToken);

                    if (!dbUpdated)
                    {
                        continue;
                    }

                    UpdateTrackedOrderStatus(order.OrderId, posterStatus.Value);

                    if (!ShouldNotifyStatus(posterStatus.Value))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(order.FcmToken))
                    {
                        _logger.LogWarning(
                            "Order has no FCM token, push notification skipped: OrderId={OrderId}, OrderNumber={OrderNumber}, Status={Status}",
                            order.OrderId,
                            order.OrderNumber,
                            posterStatus.Value);
                        continue;
                    }

                    await SendStatusNotificationsAsync(
                        notificationService,
                        order,
                        order.CurrentStatus,
                        posterStatus.Value,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error polling status for order {OrderId}", order.OrderId);
                }
            }
        }
    }

    private static OrderStatus? ResolvePosterStatus(
        string? posterTransactionId,
        string? posterIncomingOrderId,
        IReadOnlyDictionary<string, OrderStatus> transactionStatuses,
        IReadOnlyDictionary<string, OrderStatus> incomingOrderStatuses)
    {
        var transactionId = NormalizePosterIdentifier(posterTransactionId);
        if (!string.IsNullOrWhiteSpace(transactionId) &&
            transactionStatuses.TryGetValue(transactionId, out var transactionStatus))
        {
            return transactionStatus;
        }

        var incomingOrderId = NormalizePosterIdentifier(posterIncomingOrderId);
        if (!string.IsNullOrWhiteSpace(incomingOrderId) &&
            incomingOrderStatuses.TryGetValue(incomingOrderId, out var incomingStatus))
        {
            return incomingStatus;
        }

        return null;
    }

    private TrackedOrder BuildTrackedOrder(Order order)
    {
        return new TrackedOrder
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            PosterIncomingOrderId = order.PosterIncomingOrderId,
            PosterTransactionId = order.PosterTransactionId,
            FcmToken = order.FcmToken,
            Phone = order.Phone,
            CurrentStatus = order.Status,
            Language = ResolveLanguage(order.FcmToken)
        };
    }

    private string ResolveLanguage(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token) &&
            _tokenStore.TryGet(token, out var entry) &&
            !string.IsNullOrWhiteSpace(entry.Language) &&
            NotificationService.IsLanguageSupported(entry.Language))
        {
            return entry.Language!.Trim().ToLowerInvariant();
        }

        return "en";
    }

    private void UpdateTrackedOrderStatus(string orderId, OrderStatus status)
    {
        if (!_orderTracker.IsTracked(orderId))
        {
            return;
        }

        _orderTracker.UpdateOrderStatus(orderId, status);
        if (IsTerminalStatus(status))
        {
            _orderTracker.UntrackOrder(orderId);
        }
    }

    private async Task HydrateTrackedOrdersAsync(CancellationToken cancellationToken)
    {
        if (_orderTracker.TrackedCount > 0)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var lookbackDays = Math.Max(1, _options.ActiveOrdersLookbackDays);
        var cutoff = now.AddDays(-lookbackDays);

        var openOrders = await dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Status != OrderStatus.Delivered &&
                order.Status != OrderStatus.Cancelled &&
                order.CreatedAt >= cutoff)
            .ToListAsync(cancellationToken);

        if (openOrders.Count == 0)
        {
            _logger.LogInformation("No active orders found to hydrate polling tracker");
            return;
        }

        foreach (var order in openOrders)
        {
            _orderTracker.TrackOrder(BuildTrackedOrder(order));
        }

        _logger.LogInformation(
            "Hydrated {OrderCount} active orders for polling (created since {Cutoff})",
            openOrders.Count,
            cutoff);
    }

    private void PruneTrackedOrders(DateTime now)
    {
        if (_options.MaxTrackingMinutes <= 0)
        {
            return;
        }

        var maxTrackingTime = TimeSpan.FromMinutes(_options.MaxTrackingMinutes);
        foreach (var trackedOrder in _orderTracker.GetTrackedOrders())
        {
            if (now - trackedOrder.TrackedAt > maxTrackingTime)
            {
                _logger.LogInformation(
                    "Removing order {OrderId} from tracking - exceeded max tracking time",
                    trackedOrder.OrderId);
                _orderTracker.UntrackOrder(trackedOrder.OrderId);
            }
        }
    }

    private async Task<IReadOnlyCollection<TrackedOrder>> SyncTrackedOrdersFromDbAsync(
        AppDbContext dbContext,
        IReadOnlyCollection<TrackedOrder> trackedOrders,
        CancellationToken cancellationToken)
    {
        if (trackedOrders.Count == 0)
        {
            return trackedOrders;
        }

        var trackedIds = trackedOrders
            .Select(order => order.OrderId)
            .Where(orderId => !string.IsNullOrWhiteSpace(orderId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (trackedIds.Count == 0)
        {
            return trackedOrders;
        }

        var statuses = await dbContext.Orders
            .AsNoTracking()
            .Where(order => trackedIds.Contains(order.Id))
            .Select(order => new { order.Id, order.Status })
            .ToListAsync(cancellationToken);

        var statusLookup = statuses.ToDictionary(entry => entry.Id, entry => entry.Status, StringComparer.Ordinal);

        foreach (var trackedOrder in trackedOrders)
        {
            if (!statusLookup.TryGetValue(trackedOrder.OrderId, out var dbStatus))
            {
                _logger.LogInformation(
                    "Removing order {OrderId} from tracking - missing in database",
                    trackedOrder.OrderId);
                _orderTracker.UntrackOrder(trackedOrder.OrderId);
                continue;
            }

            if (trackedOrder.CurrentStatus != dbStatus)
            {
                _orderTracker.UpdateOrderStatus(trackedOrder.OrderId, dbStatus);
            }

            if (IsTerminalStatus(dbStatus))
            {
                _orderTracker.UntrackOrder(trackedOrder.OrderId);
            }
        }

        return _orderTracker.GetTrackedOrders();
    }

    private static bool TryBuildPosterStatusLookups(
        JsonDocument response,
        out Dictionary<string, OrderStatus> transactionStatuses,
        out Dictionary<string, OrderStatus> incomingOrderStatuses,
        out int transactionCount)
    {
        transactionStatuses = new Dictionary<string, OrderStatus>(StringComparer.OrdinalIgnoreCase);
        incomingOrderStatuses = new Dictionary<string, OrderStatus>(StringComparer.OrdinalIgnoreCase);
        transactionCount = 0;

        if (!TryGetTransactionsArray(response, out var transactions))
        {
            return false;
        }

        if (transactions.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        transactionCount = transactions.GetArrayLength();
        foreach (var transaction in transactions.EnumerateArray())
        {
            var status = ResolveTransactionStatus(transaction);
            if (status == null)
            {
                continue;
            }

            if (TryGetTransactionId(transaction, out var transactionId))
            {
                var normalized = NormalizePosterIdentifier(transactionId);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    transactionStatuses[normalized] = status.Value;
                }
            }

            if (TryGetIncomingOrderId(transaction, out var incomingOrderId))
            {
                var normalized = NormalizePosterIdentifier(incomingOrderId);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    incomingOrderStatuses[normalized] = status.Value;
                }
            }
        }

        return true;
    }

    private static OrderStatus? ResolveTransactionStatus(JsonElement transaction)
    {
        OrderStatus? historyStatus = null;
        if (TryMapHistoryStatus(transaction, out var mappedHistoryStatus))
        {
            historyStatus = mappedHistoryStatus;
        }

        OrderStatus? processingStatus = null;
        if (TryMapProcessingStatus(transaction, out var mappedProcessingStatus))
        {
            processingStatus = mappedProcessingStatus;
        }

        OrderStatus? incomingStatus = null;
        if (TryMapIncomingOrderStatus(transaction, out var mappedIncomingStatus))
        {
            incomingStatus = mappedIncomingStatus;
        }

        OrderStatus? posterStatus = null;
        if (TryMapPosterStatus(transaction, out var mappedPosterStatus))
        {
            posterStatus = mappedPosterStatus;
        }

        int? statusValue = null;
        if (TryGetInt(transaction, "status", out var statusValueParsed))
        {
            statusValue = statusValueParsed;
        }

        if (statusValue == 4 &&
            historyStatus == null &&
            processingStatus == null &&
            incomingStatus == null)
        {
            posterStatus = null;
        }

        var resolved = SelectBestStatus(historyStatus, processingStatus, incomingStatus, posterStatus);
        if (resolved != null)
        {
            return resolved;
        }

        if (processingStatus != null)
        {
            return processingStatus;
        }

        if (statusValue != null)
        {
            return MapPosterTransactionStatusCode(statusValue.Value);
        }

        return null;
    }

    private static bool TryGetTransactionsArray(JsonDocument response, out JsonElement transactions)
    {
        if (response.RootElement.TryGetProperty("response", out var responseElement) &&
            responseElement.ValueKind == JsonValueKind.Array)
        {
            transactions = responseElement;
            return true;
        }

        if (response.RootElement.ValueKind == JsonValueKind.Array)
        {
            transactions = response.RootElement;
            return true;
        }

        transactions = default;
        return false;
    }

    private static bool TryGetTransactionId(JsonElement element, out string id)
    {
        id = string.Empty;

        if (TryGetLong(element, "transaction_id", out var numeric))
        {
            id = numeric.ToString();
            return true;
        }

        if (TryGetString(element, "transaction_id", out var text))
        {
            id = text;
            return true;
        }

        return false;
    }

    private static bool TryGetIncomingOrderId(JsonElement element, out string id)
    {
        id = string.Empty;

        if (TryGetLong(element, "incoming_order_id", out var numeric))
        {
            id = numeric.ToString();
            return true;
        }

        if (TryGetString(element, "incoming_order_id", out var text))
        {
            id = text;
            return true;
        }

        if (TryGetLong(element, "incomingOrderId", out numeric))
        {
            id = numeric.ToString();
            return true;
        }

        if (TryGetString(element, "incomingOrderId", out text))
        {
            id = text;
            return true;
        }

        return false;
    }

    private static string? NormalizePosterIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed[1..] : trimmed;
    }

    private async Task<OrderStatus?> GetPosterOrderStatusAsync(
        PosterService posterService,
        string? incomingOrderId,
        string? transactionId,
        OrderStatus currentStatus,
        CancellationToken cancellationToken)
    {
        // Use dash.getTransactions API to get real-time status updates.
        // The incoming orders API (incomingOrders.getIncomingOrder) doesn't return
        // updated processing status - it only shows "Accepted" and never updates.
        // The dash.getTransactions API returns the correct processing_status field.

        var resolvedTransactionId = transactionId;
        if (string.IsNullOrWhiteSpace(resolvedTransactionId) &&
            !string.IsNullOrWhiteSpace(incomingOrderId))
        {
            var resolution = await ResolveIncomingOrderAsync(
                posterService,
                incomingOrderId,
                cancellationToken);

            resolvedTransactionId = resolution.TransactionId;
            if (resolvedTransactionId is null && resolution.Status is not null)
            {
                return resolution.Status;
            }
        }

        if (!string.IsNullOrWhiteSpace(resolvedTransactionId))
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var transactionsResponse = await posterService.GetTransactionsAsync(
                courierId: string.Empty,
                dateFrom: today,
                dateTo: today,
                cancellationToken,
                includeDelivery: true,
                includeProducts: false,
                includeHistory: true);

            if (transactionsResponse != null)
            {
                using (transactionsResponse)
                {
                    var status = FindTransactionStatus(transactionsResponse, resolvedTransactionId);
                    if (status != null)
                    {
                        _logger.LogDebug(
                            "Found transaction {TransactionId} with processing_status -> {Status}",
                            resolvedTransactionId, status.Value);
                        if (status == OrderStatus.Pending &&
                            StatusRank(currentStatus) < StatusRank(OrderStatus.Preparing))
                        {
                            var historyStatus = await GetTransactionStatusAsync(
                                posterService,
                                resolvedTransactionId,
                                cancellationToken);
                            if (historyStatus != null &&
                                StatusRank(historyStatus.Value) >= StatusRank(status.Value))
                            {
                                return historyStatus;
                            }
                        }
                        return status;
                    }
                }
            }
        }

        // Fallback: try single transaction API if batch lookup fails
        if (!string.IsNullOrWhiteSpace(resolvedTransactionId))
        {
            return await GetTransactionStatusAsync(posterService, resolvedTransactionId, cancellationToken);
        }

        return null;
    }

    private async Task<OrderStatus?> GetTransactionStatusAsync(
        PosterService posterService,
        string transactionId,
        CancellationToken cancellationToken)
    {
        var transactionResponse = await posterService.GetTransactionAsync(transactionId, cancellationToken);
        if (transactionResponse == null)
        {
            return null;
        }

        using (transactionResponse)
        {
            return ParsePosterStatus(transactionResponse);
        }
    }

    private async Task<(string? TransactionId, OrderStatus? Status)> ResolveIncomingOrderAsync(
        PosterService posterService,
        string incomingOrderId,
        CancellationToken cancellationToken)
    {
        var incomingOrderResponse = await posterService.GetIncomingOrderAsync(incomingOrderId, cancellationToken);
        if (incomingOrderResponse == null)
        {
            return (null, null);
        }

        using (incomingOrderResponse)
        {
            if (TryExtractTransactionId(incomingOrderResponse.RootElement, out var resolvedTransactionId))
            {
                return (resolvedTransactionId, null);
            }

            var status = ParsePosterStatus(incomingOrderResponse);
            return (null, status);
        }
    }

    private static bool TryExtractTransactionId(JsonElement element, out string transactionId)
    {
        transactionId = string.Empty;

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("transaction_id", out var transactionIdElement))
            {
                transactionId = transactionIdElement.ValueKind switch
                {
                    JsonValueKind.Number => transactionIdElement.GetInt64().ToString(),
                    JsonValueKind.String => transactionIdElement.GetString() ?? string.Empty,
                    _ => string.Empty
                };

                if (!string.IsNullOrWhiteSpace(transactionId))
                {
                    return true;
                }
            }

            if (element.TryGetProperty("response", out var responseElement))
            {
                return TryExtractTransactionId(responseElement, out transactionId);
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in element.EnumerateArray())
            {
                if (TryExtractTransactionId(entry, out transactionId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Find a specific transaction in the dash.getTransactions response and extract its status.
    /// The processing_status field contains the real delivery workflow status.
    /// </summary>
    private OrderStatus? FindTransactionStatus(JsonDocument response, string transactionId)
    {
        if (!response.RootElement.TryGetProperty("response", out var transactions))
        {
            return null;
        }

        foreach (var tx in transactions.EnumerateArray())
        {
            // Match by transaction_id
            if (!tx.TryGetProperty("transaction_id", out var txIdElement))
            {
                continue;
            }

            var txId = txIdElement.ValueKind == JsonValueKind.Number
                ? txIdElement.GetInt64().ToString()
                : txIdElement.GetString();

            if (!string.Equals(txId, transactionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int? statusValue = null;
            if (tx.TryGetProperty("status", out var statusProp))
            {
                statusValue = statusProp.ValueKind == JsonValueKind.Number
                    ? statusProp.GetInt32()
                    : int.TryParse(statusProp.GetString(), out var parsed) ? parsed : null;
            }

            OrderStatus? processingStatus = null;
            if (tx.TryGetProperty("processing_status", out var processingStatusProp))
            {
                var processingValue = processingStatusProp.ValueKind == JsonValueKind.Number
                    ? processingStatusProp.GetInt32()
                    : int.TryParse(processingStatusProp.GetString(), out var parsed) ? parsed : 0;

                processingStatus = MapDashProcessingStatus(processingValue);
            }

            // processing_status is authoritative for completed orders.
            if (processingStatus is OrderStatus.Delivered or OrderStatus.Cancelled)
            {
                return processingStatus;
            }

            if (statusValue == 4)
            {
                // status=4 can be ambiguous; defer to detailed transaction history.
                return null;
            }

            if (processingStatus != null)
            {
                return processingStatus;
            }
        }

        return null;
    }

    /// <summary>
    /// Map processing_status codes from dash.getTransactions API to OrderStatus.
    /// Based on actual Poster history events:
    /// - 10: Initial/Pending (before acceptance)
    /// - 20: Accepted (after settable + changeorderstatus)
    /// - 30: Preparing (changeprocessingstatus value=30)
    /// - 40: On The Way (changeprocessingstatus value=40, after changecourier)
    /// - 50: Delivered (changeprocessingstatus value=50)
    /// - 60: Cancelled
    /// </summary>
    private static OrderStatus? MapDashProcessingStatus(int code)
    {
        if (code >= 61)
        {
            return OrderStatus.Delivered;
        }

        return code switch
        {
            0 => OrderStatus.Pending,     // Initial state before any processing
            10 => OrderStatus.Pending,    // Pending/New
            20 => OrderStatus.Accepted,   // Accepted by restaurant
            30 => OrderStatus.Preparing,  // Being prepared in kitchen
            40 => OrderStatus.OnTheWay,   // Handed to courier, on the way
            50 => OrderStatus.Delivered,  // Delivered to customer
            60 => OrderStatus.Cancelled,  // Cancelled
            _ => null
        };
    }

    private static OrderStatus? ParsePosterStatus(JsonDocument response)
    {
        if (response.RootElement.TryGetProperty("response", out var responseElement))
        {
            var status = MapPosterStatusFromElement(responseElement);
            if (status != null)
            {
                return status;
            }
        }

        return MapPosterStatusFromElement(response.RootElement);
    }

    private static OrderStatus? MapPosterStatusFromElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            OrderStatus? best = null;
            foreach (var entry in element.EnumerateArray())
            {
                var mapped = MapPosterStatusFromElement(entry);
                if (mapped == null)
                {
                    continue;
                }

                if (best == null || StatusRank(mapped.Value) > StatusRank(best.Value))
                {
                    best = mapped.Value;
                }
            }

            return best;
        }

        OrderStatus? historyStatus = null;
        if (TryMapHistoryStatus(element, out var mappedHistoryStatus))
        {
            historyStatus = mappedHistoryStatus;
        }

        OrderStatus? processingStatus = null;
        if (TryMapProcessingStatus(element, out var mappedProcessingStatus))
        {
            processingStatus = mappedProcessingStatus;
        }

        OrderStatus? incomingStatus = null;
        if (TryMapIncomingOrderStatus(element, out var mappedIncomingStatus))
        {
            incomingStatus = mappedIncomingStatus;
        }

        OrderStatus? posterStatus = null;
        if (TryMapPosterStatus(element, out var mappedPosterStatus))
        {
            posterStatus = mappedPosterStatus;
        }

        return SelectBestStatus(historyStatus, processingStatus, incomingStatus, posterStatus);
    }

    private static OrderStatus? SelectBestStatus(
        OrderStatus? historyStatus,
        OrderStatus? processingStatus,
        OrderStatus? incomingStatus,
        OrderStatus? posterStatus)
    {
        if (historyStatus is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            return historyStatus;
        }

        if (processingStatus is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            return processingStatus;
        }

        OrderStatus? best = null;
        best = ChooseHigher(best, historyStatus);
        best = ChooseHigher(best, processingStatus);

        if (best is not null &&
            StatusRank(best.Value) < StatusRank(OrderStatus.Preparing))
        {
            if (incomingStatus == OrderStatus.Preparing)
            {
                incomingStatus = null;
            }

            if (posterStatus == OrderStatus.Preparing)
            {
                posterStatus = null;
            }
        }

        if (best is not null &&
            StatusRank(best.Value) < StatusRank(OrderStatus.Delivered))
        {
            if (incomingStatus == OrderStatus.Delivered)
            {
                incomingStatus = null;
            }

            if (posterStatus == OrderStatus.Delivered)
            {
                posterStatus = null;
            }
        }

        best = ChooseHigher(best, incomingStatus);
        best = ChooseHigher(best, posterStatus);
        return best;
    }

    private static OrderStatus? ChooseHigher(OrderStatus? current, OrderStatus? candidate)
    {
        if (candidate == null)
        {
            return current;
        }

        if (current == null)
        {
            return candidate;
        }

        return StatusRank(candidate.Value) > StatusRank(current.Value) ? candidate : current;
    }

    private static bool TryMapHistoryStatus(JsonElement element, out OrderStatus status)
    {
        status = default;

        if (!TryGetHistoryArray(element, out var historyArray))
        {
            return false;
        }

        OrderStatus? latestStatus = null;
        long? latestTime = null;
        int? latestIndex = null;
        var index = 0;

        foreach (var entry in historyArray.EnumerateArray())
        {
            index++;
            if (!TryGetHistoryType(entry, out var type))
            {
                continue;
            }

            if (!TryMapHistoryEntry(type, entry, out var mapped))
            {
                continue;
            }

            if (TryGetHistoryTimestamp(entry, out var timestamp))
            {
                if (latestTime is null || timestamp >= latestTime)
                {
                    latestTime = timestamp;
                    latestStatus = mapped;
                    latestIndex = index;
                }

                continue;
            }

            if (latestTime is null && (latestIndex is null || index >= latestIndex))
            {
                latestStatus = mapped;
                latestIndex = index;
            }
        }

        if (latestStatus is null)
        {
            return false;
        }

        status = latestStatus.Value;
        return true;
    }

    private static bool TryGetHistoryArray(JsonElement element, out JsonElement historyArray)
    {
        if (TryGetArray(element, "history", out historyArray) ||
            TryGetArray(element, "transactions_history", out historyArray))
        {
            return true;
        }

        if (TryGetEmbeddedArray(element, "history", out historyArray) ||
            TryGetEmbeddedArray(element, "transactions_history", out historyArray))
        {
            return true;
        }

        historyArray = default;
        return false;
    }

    private static bool TryGetHistoryType(JsonElement entry, out string type)
    {
        if (TryGetString(entry, "type_history", out type))
        {
            return true;
        }

        if (TryGetString(entry, "type", out type))
        {
            return true;
        }

        type = string.Empty;
        return false;
    }

    private static bool TryGetHistoryTimestamp(JsonElement entry, out long timestamp)
    {
        timestamp = 0;

        if (TryGetLong(entry, "time", out timestamp))
        {
            return true;
        }

        if (TryGetLong(entry, "time_set", out timestamp))
        {
            return true;
        }

        return false;
    }

    private static bool TryMapHistoryEntry(string type, JsonElement entry, out OrderStatus status)
    {
        status = default;
        var normalized = NormalizeStatus(type);

        switch (normalized)
        {
            case "changeprocessingstatus":
                if (TryGetHistoryNumericValue(entry, out var processingCode))
                {
                    var mapped = MapPosterProcessingStatusCode(processingCode);
                    if (mapped != null)
                    {
                        status = mapped.Value;
                        return true;
                    }
                }

                return false;

            case "changeorderstatus":
                if (TryGetHistoryNumericValue(entry, out var orderStatusCode))
                {
                    return TryMapOrderStatusChangeCode(orderStatusCode, out status);
                }

                return false;

            case "settable":
                status = OrderStatus.Accepted;
                return true;

            case "open":
                status = OrderStatus.Pending;
                return true;

            case "close":
                status = MapHistoryCloseStatus(entry);
                return true;

            default:
                return false;
        }
    }

    private static bool TryMapOrderStatusChangeCode(int code, out OrderStatus status)
    {
        switch (code)
        {
            case 1:
                status = OrderStatus.Accepted;
                return true;
            case 4:
                status = OrderStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    private static OrderStatus MapHistoryCloseStatus(JsonElement entry)
    {
        if (TryGetObject(entry, "value_text", out var valueText) ||
            TryGetEmbeddedObject(entry, "value_text", out valueText))
        {
            if (TryGetString(valueText, "reason", out var reason))
            {
                return reason switch
                {
                    "1" => OrderStatus.Cancelled,
                    "2" => OrderStatus.Cancelled,
                    "4" => OrderStatus.Cancelled,
                    "3" => OrderStatus.Delivered,
                    _ => OrderStatus.Delivered
                };
            }
        }

        return OrderStatus.Delivered;
    }

    private static bool TryGetHistoryNumericValue(JsonElement entry, out int value)
    {
        if (TryGetInt(entry, "value", out value))
        {
            return true;
        }

        if (TryGetInt(entry, "value2", out value))
        {
            return true;
        }

        if (TryGetInt(entry, "value_text", out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        switch (propertyElement.ValueKind)
        {
            case JsonValueKind.String:
                value = propertyElement.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            case JsonValueKind.Number:
                value = propertyElement.GetRawText();
                return !string.IsNullOrWhiteSpace(value);
            default:
                return false;
        }
    }

    private static bool TryGetInt(JsonElement element, string property, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        return propertyElement.ValueKind switch
        {
            JsonValueKind.Number => propertyElement.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(propertyElement.GetString(), out value),
            _ => false
        };
    }

    private static bool TryGetLong(JsonElement element, string property, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        return propertyElement.ValueKind switch
        {
            JsonValueKind.Number => propertyElement.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(propertyElement.GetString(), out value),
            _ => false
        };
    }

    private static bool TryGetObject(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        if (propertyElement.ValueKind == JsonValueKind.Object)
        {
            value = propertyElement;
            return true;
        }

        return false;
    }

    private static bool TryGetArray(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        if (propertyElement.ValueKind == JsonValueKind.Array)
        {
            value = propertyElement;
            return true;
        }

        return false;
    }

    private static bool TryGetEmbeddedObject(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetEmbeddedArray(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static OrderStatus? MapPosterProcessingStatus(JsonElement statusElement)
    {
        var statusValue = statusElement.ValueKind == JsonValueKind.Number
            ? statusElement.GetInt32().ToString()
            : statusElement.GetString();

        if (string.IsNullOrWhiteSpace(statusValue))
        {
            return null;
        }

        if (int.TryParse(statusValue, out var numeric))
        {
            return MapPosterProcessingStatusCode(numeric);
        }

        var normalized = NormalizeStatus(statusValue);
        return normalized switch
        {
            "new" or "pending" => OrderStatus.Pending,
            "accepted" => OrderStatus.Accepted,
            "cooking" or "preparing" => OrderStatus.Preparing,
            "ready" => OrderStatus.Preparing,
            "ontheway" or "onway" => OrderStatus.OnTheWay,
            "delivered" or "completed" or "closed" => OrderStatus.Delivered,
            "cancelled" or "canceled" => OrderStatus.Cancelled,
            _ => null
        };
    }

    private static OrderStatus? MapPosterStatus(JsonElement statusElement)
    {
        var statusValue = statusElement.ValueKind == JsonValueKind.Number
            ? statusElement.GetInt32().ToString()
            : statusElement.GetString();

        if (string.IsNullOrWhiteSpace(statusValue))
        {
            return null;
        }

        if (int.TryParse(statusValue, out var numeric))
        {
            return numeric >= 10
                ? MapPosterProcessingStatusCode(numeric)
                : MapPosterTransactionStatusCode(numeric);
        }

        var normalized = NormalizeStatus(statusValue);
        return normalized switch
        {
            "new" or "pending" => OrderStatus.Pending,
            "accepted" => OrderStatus.Accepted,
            "cooking" or "preparing" => OrderStatus.Preparing,
            "ready" => OrderStatus.Preparing,
            "ontheway" or "onway" => OrderStatus.OnTheWay,
            "delivered" or "completed" or "closed" => OrderStatus.Delivered,
            "cancelled" or "canceled" => OrderStatus.Cancelled,
            _ => null
        };
    }

    private static OrderStatus? MapPosterIncomingOrderStatus(JsonElement statusElement)
    {
        var statusValue = statusElement.ValueKind == JsonValueKind.Number
            ? statusElement.GetInt32().ToString()
            : statusElement.GetString();

        if (string.IsNullOrWhiteSpace(statusValue))
        {
            return null;
        }

        if (int.TryParse(statusValue, out var numeric))
        {
            if (numeric >= 10)
            {
                return MapPosterProcessingStatusCode(numeric);
            }

            // Poster incoming order status:
            // 0 = new, 1 = accepted, 2 = cooking, 3 = ready, 4 = on the way, 5 = delivered, 6 = cancelled, 7 = deleted
            return numeric switch
            {
                0 => OrderStatus.Pending,
                1 => OrderStatus.Accepted,
                2 => OrderStatus.Preparing,
                3 => OrderStatus.Preparing, // Ready
                4 => OrderStatus.OnTheWay,
                5 => OrderStatus.Delivered,
                6 => OrderStatus.Cancelled,
                7 => OrderStatus.Cancelled,
                _ => null
            };
        }

        var normalized = NormalizeStatus(statusValue);
        return normalized switch
        {
            "new" or "pending" => OrderStatus.Pending,
            "accepted" => OrderStatus.Accepted,
            "cooking" or "preparing" => OrderStatus.Preparing,
            "ready" => OrderStatus.Preparing,
            "ontheway" or "onway" => OrderStatus.OnTheWay,
            "delivered" or "completed" or "closed" => OrderStatus.Delivered,
            "cancelled" or "canceled" => OrderStatus.Cancelled,
            _ => null
        };
    }

    private static bool TryMapIncomingOrderStatus(JsonElement element, out OrderStatus status)
    {
        status = default;

        if (element.TryGetProperty("incoming_order_status", out var incomingStatusElement))
        {
            var mapped = MapPosterIncomingOrderStatus(incomingStatusElement);
            if (mapped != null)
            {
                status = mapped.Value;
                return true;
            }
        }

        if (element.TryGetProperty("incoming_order_id", out _) &&
            !element.TryGetProperty("transaction_id", out _) &&
            element.TryGetProperty("status", out var statusElement))
        {
            var mapped = MapPosterIncomingOrderStatus(statusElement);
            if (mapped != null)
            {
                status = mapped.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryMapProcessingStatus(JsonElement element, out OrderStatus status)
    {
        status = default;

        if (!element.TryGetProperty("processing_status", out var processingStatusElement) &&
            !element.TryGetProperty("processingStatus", out processingStatusElement))
        {
            return false;
        }

        var mapped = MapPosterProcessingStatus(processingStatusElement);
        if (mapped != null)
        {
            status = mapped.Value;
            return true;
        }

        return false;
    }

    private static bool TryMapPosterStatus(JsonElement element, out OrderStatus status)
    {
        status = default;

        if (!element.TryGetProperty("status", out var statusElement))
        {
            return false;
        }

        var mapped = MapPosterStatus(statusElement);
        if (mapped != null)
        {
            status = mapped.Value;
            return true;
        }

        return false;
    }

    private static OrderStatus? MapPosterTransactionStatusCode(int statusValue) =>
        statusValue switch
        {
            0 => OrderStatus.Pending,
            1 => OrderStatus.Accepted,
            2 => OrderStatus.Preparing,
            3 => OrderStatus.Preparing,
            4 => OrderStatus.Cancelled,
            5 => OrderStatus.Delivered,
            _ => null
        };

    private static OrderStatus? MapPosterProcessingStatusCode(int statusValue)
    {
        if (statusValue >= 61)
        {
            return OrderStatus.Delivered;
        }

        return statusValue switch
        {
            0 => OrderStatus.Pending,     // Initial state
            1 => OrderStatus.Accepted,    // Legacy processing status
            2 => OrderStatus.Preparing,   // Legacy processing status
            3 => OrderStatus.Preparing,   // Legacy processing status
            4 => OrderStatus.OnTheWay,    // Legacy processing status
            5 => OrderStatus.Delivered,   // Legacy processing status
            6 => OrderStatus.Cancelled,   // Legacy processing status
            10 => OrderStatus.Pending,    // Pending/New
            20 => OrderStatus.Accepted,   // Accepted
            30 => OrderStatus.Preparing,  // Preparing
            40 => OrderStatus.OnTheWay,   // On the way
            50 => OrderStatus.Delivered,  // Delivered
            60 => OrderStatus.Cancelled,  // Cancelled
            _ => null
        };
    }

    private static string NormalizeStatus(string value) =>
        value.Trim()
            .ToLowerInvariant()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

    private async Task<bool> UpdateOrderStatusInDbAsync(
        AppDbContext dbContext,
        string orderId,
        OrderStatus newStatus,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order == null)
        {
            return false;
        }

        if (order.Status == newStatus)
        {
            return false;
        }

        // Prevent backward status transitions (cancelled wins unless already terminal).
        // This prevents flickering when Poster API returns stale/cached data
        if (!ShouldUpdateStatus(order.Status, newStatus))
        {
            _logger.LogDebug(
                "Ignoring backward status transition for order {OrderId}: {CurrentStatus} -> {NewStatus}",
                orderId, order.Status, newStatus);
            return false;
        }

        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Determines if a status update should be applied.
    /// Only allows forward progression (higher rank) or Cancelled status.
    /// Prevents flickering when Poster API returns stale data.
    /// </summary>
    private static bool ShouldUpdateStatus(OrderStatus current, OrderStatus incoming)
    {
        if (current is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            return false;
        }

        // Cancelled always wins unless order is already terminal.
        if (incoming == OrderStatus.Cancelled)
        {
            return true;
        }

        // Don't allow going backward in the workflow
        return StatusRank(incoming) > StatusRank(current);
    }

    private static int StatusRank(OrderStatus status) => status switch
    {
        OrderStatus.AwaitingPayment => -1,
        OrderStatus.Pending => 0,
        OrderStatus.Accepted => 1,
        OrderStatus.Preparing => 2,
        OrderStatus.OnTheWay => 3,
        OrderStatus.Delivered => 4,
        OrderStatus.Cancelled => 5,
        _ => 0
    };

    private async Task SendStatusNotificationAsync(
        NotificationService notificationService,
        TrackedOrder trackedOrder,
        OrderStatus newStatus,
        CancellationToken cancellationToken)
    {
        var statusString = newStatus switch
        {
            OrderStatus.AwaitingPayment => "awaitingPayment",
            OrderStatus.Pending => "pending",
            OrderStatus.Accepted => "accepted",
            OrderStatus.Preparing => "preparing",
            OrderStatus.OnTheWay => "onTheWay",
            OrderStatus.Delivered => "delivered",
            OrderStatus.Cancelled => "cancelled",
            _ => "pending"
        };

        try
        {
            await notificationService.SendOrderStatusUpdateAsync(
                trackedOrder.FcmToken!,
                trackedOrder.OrderId,
                trackedOrder.OrderNumber,
                trackedOrder.PosterIncomingOrderId,
                trackedOrder.PosterTransactionId,
                statusString,
                trackedOrder.Language,
                cancellationToken);

            _logger.LogInformation(
                "Sent status notification for order {OrderNumber}: {Status} (OrderId: {OrderId})",
                trackedOrder.OrderNumber,
                statusString,
                trackedOrder.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send status notification for order {OrderNumber} (OrderId: {OrderId})",
                trackedOrder.OrderNumber,
                trackedOrder.OrderId);
        }
    }

    private async Task SendStatusNotificationsAsync(
        NotificationService notificationService,
        TrackedOrder trackedOrder,
        OrderStatus currentStatus,
        OrderStatus incomingStatus,
        CancellationToken cancellationToken)
    {
        foreach (var status in BuildNotificationSequence(currentStatus, incomingStatus))
        {
            await SendStatusNotificationAsync(notificationService, trackedOrder, status, cancellationToken);
        }
    }

    private static IReadOnlyList<OrderStatus> BuildNotificationSequence(
        OrderStatus currentStatus,
        OrderStatus incomingStatus)
    {
        if (!ShouldNotifyStatus(incomingStatus))
        {
            return Array.Empty<OrderStatus>();
        }

        if (incomingStatus == OrderStatus.Cancelled)
        {
            return new[] { OrderStatus.Cancelled };
        }
        return new[] { incomingStatus };
    }

    private static bool ShouldNotifyStatus(OrderStatus status) =>
        status is OrderStatus.Accepted
            or OrderStatus.Preparing
            or OrderStatus.OnTheWay
            or OrderStatus.Delivered
            or OrderStatus.Cancelled;

    private static bool IsTerminalStatus(OrderStatus status) =>
        status is OrderStatus.Delivered or OrderStatus.Cancelled;
}
