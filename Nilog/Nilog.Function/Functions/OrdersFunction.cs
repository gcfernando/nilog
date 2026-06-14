// -----------------------------------------------------------------------------
//  Nilog.Function — the checkout endpoint. This single HTTP function exercises
//  most of Nilog's surface against a realistic flow: parse -> validate -> price
//  -> reserve -> charge -> ship. The per-request correlation scope is opened by
//  RequestLoggingMiddleware, so the lines below stay focused on business events.
//
//      POST /api/orders
//
//  File        : OrdersFunction.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nilog;
using Nilog.Function.Models;
using Nilog.Function.Services;

namespace Nilog.Function.Functions;

public sealed class OrdersFunction(
    ILogger<OrdersFunction> logger,
    IInventoryService inventory,
    IPaymentGateway payments)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly ILogger<OrdersFunction> _logger = logger;
    private readonly IInventoryService _inventory = inventory;
    private readonly IPaymentGateway _payments = payments;

    [Function("PlaceOrder")]
    public async Task<IActionResult> PlaceOrder(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders")] HttpRequest req)
    {
        // Debug: a development-time detail. Filtered out in production, so on the hot
        // path it returns right after the IsEnabled check — no array, no boxing, no string.
        _logger.WriteDebug("Reading checkout payload ({Bytes} bytes)", req.ContentLength ?? 0);

        CheckoutRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<CheckoutRequest>(req.Body, _json);
        }
        catch (JsonException ex)
        {
            // Warning + business context; no exception object needed for a client error.
            _logger.WriteWarning("Rejected malformed checkout payload: {Reason}", ex.Message);
            return new BadRequestObjectResult(new { error = "Invalid JSON body." });
        }

        string? validationError = null;
        if (request is null || !request.IsValid(out validationError))
        {
            _logger.WriteWarning("Checkout validation failed: {Reason}", validationError ?? "empty body");
            return new BadRequestObjectResult(new { error = validationError ?? "Empty body." });
        }

        var orderId = Guid.NewGuid();

        // A nested, request-scoped context bag. These fields are merged with the
        // middleware's scope, so every line below is tagged with all of them.
        using (_logger.WriteScope(new Dictionary<string, object>
        {
            ["OrderId"] = orderId,
            ["CustomerId"] = request.CustomerId,
            ["Currency"] = request.Currency,
        }))
        {
            // Information with two strongly-typed args (zero array allocation).
            _logger.WriteInformation("Order {OrderId} opened for customer {CustomerId}", orderId, request.CustomerId);

            // --- Pricing: a tight loop. Debug is off in production, so each iteration is
            //     effectively free. This is the core reason Nilog exists. ---
            decimal total = 0m;
            foreach (OrderLine line in request.Items)
            {
                total += line.UnitPrice * line.Quantity;
                _logger.WriteDebug("Priced {Sku} x{Qty} @ {Price:N2}", line.Sku, line.Quantity, line.UnitPrice);
            }

            // Template niceties: column alignment + numeric format specifiers.
            _logger.WriteInformation("Cart {LineCount,2} line(s)  total {Total,10:N2} {Currency}",
                request.Items.Count, total, request.Currency);

            // --- Inventory reservation (may throw a typed domain exception). ---
            try
            {
                _inventory.Reserve(orderId, request.Items);
            }
            catch (OutOfStockException ex)
            {
                // Error WITH exception + business context, all in one zero-allocation call
                // (message + exception + three typed args).
                _logger.WriteError("Reservation failed for order {OrderId}: {Sku} short by {Missing}",
                    ex, orderId, ex.Sku, ex.MissingQuantity);
                return new ConflictObjectResult(new { error = "Out of stock", sku = ex.Sku });
            }

            // --- Payment (may decline, with a nested transport failure underneath). ---
            try
            {
                string auth = _payments.Charge(orderId, total, request.Currency, request.CardLast4);
                _logger.WriteInformation("Payment captured {Auth} for order {OrderId}", auth, orderId);
            }
            catch (PaymentDeclinedException ex)
            {
                // A full, verbose incident report rendered through the JSON ExceptionFormatter
                // configured at startup. moreDetailsEnabled walks the inner exception chain.
                _logger.WriteErrorException(ex, title: "payment.declined", moreDetailsEnabled: true);
                _inventory.Release(orderId);
                return new ObjectResult(new { error = "Payment declined", reason = ex.Reason })
                {
                    StatusCode = StatusCodes.Status402PaymentRequired,
                };
            }

            // --- Shipment: an event with many fields -> the familiar params path (4+ args,
            //     one array, exactly like the framework). Handy for genuinely wide events. ---
            var shipmentId = Guid.NewGuid();
            var eta = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
            _logger.WriteInformation(
                "Shipment {ShipmentId} via {Carrier} to {City}, {Country} — ETA {Eta:yyyy-MM-dd}",
                shipmentId, request.Carrier, request.City, request.Country, eta);

            _logger.WriteInformation("Order {OrderId} confirmed", orderId);

            return new CreatedResult($"/api/orders/{orderId}", new
            {
                orderId,
                total,
                currency = request.Currency,
                shipmentId,
                eta,
            });
        }
    }
}
