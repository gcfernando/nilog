// -----------------------------------------------------------------------------
//  Nilog demo — a runnable, commented feature tour that walks through every
//  public Nilog feature end to end.
//
//  File        : Program.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
// =============================================================================
//  Nilog — Real-World Feature Tour
// -----------------------------------------------------------------------------
//  This program models a small slice of an e-commerce "checkout" service and
//  logs it the way you actually would in production. Every public Nilog feature
//  is demonstrated against a realistic scenario — order placement, payments,
//  inventory, shipping, request correlation, and failure handling.
//
//      dotnet run -c Release --project Nilog.Demo
// =============================================================================

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nilog;

// -----------------------------------------------------------------------------
// 0. Wire up logging the normal Microsoft.Extensions.Logging way.
//    Nilog rides on top of ILogger, so this is exactly the setup you already use.
//    SingleLine is left false so the multi-line exception reports render properly.
//    We create one logger per subsystem, just like real services do.
// -----------------------------------------------------------------------------
using ILoggerFactory factory = LoggerFactory.Create(builder =>
{
    _ = builder
        .SetMinimumLevel(LogLevel.Trace) // verbose here so the tour shows every level
        .AddSimpleConsole(o =>
        {
            o.SingleLine = false;
            o.IncludeScopes = true; // print scope context (section 8) so correlation is visible
            o.TimestampFormat = "HH:mm:ss ";
        });
});

ILogger startup = factory.CreateLogger("Checkout.Startup");
ILogger orders = factory.CreateLogger("Checkout.Orders");
ILogger payments = factory.CreateLogger("Checkout.Payments");
ILogger inventory = factory.CreateLogger("Checkout.Inventory");
ILogger shipping = factory.CreateLogger("Checkout.Shipping");
ILogger http = factory.CreateLogger("Checkout.Http");
ILogger profiles = factory.CreateLogger("Checkout.Profiles");
ILogger reports = factory.CreateLogger("Checkout.Reports");

// A realistic order we will follow through the whole tour.
var orderId = Guid.Parse("8f3b2c10-7d4e-4a9b-9b1a-2c0f5e6d7a81");
const int customerId = 480215;

Section("1. The six levels — a service starting up and handling load");
{
    // Plain messages take Nilog's fast, no-allocation delegate path.
    startup.WriteTrace("DI container built: 87 services, 12 hosted workers registered");
    startup.WriteDebug("Configuration loaded from appsettings.Production.json");
    startup.WriteInformation("Checkout service started — listening on https://0.0.0.0:8443");
    inventory.WriteWarning("Warehouse queue depth is high; ingestion is falling behind");
    payments.WriteError("Stripe webhook signature verification failed — event rejected");
    startup.WriteCritical("Redis connection pool exhausted — shedding load");
}

Section("2. Structured logging with typed arguments (1–4, zero array allocation)");
{
    // One to four values bind to strongly-typed overloads: no object[] is built,
    // and {Named} placeholders flow through as structured properties to your sink.
    orders.WriteInformation("Order {OrderId} placed by customer {CustomerId}", orderId, customerId);

    // Format specifiers work just like the framework (Nilog renders with invariant culture).
    payments.WriteInformation("Authorized {Amount:N2} {Currency} on card ending {Last4}", 129.95m, "EUR", "4242");

    orders.WriteInformation("Order {OrderId}: {ItemCount} items, total {Total:N2} EUR", orderId, 3, 129.95m);

    // Four-argument typed overload — zero object[], zero boxing on the disabled path.
    // Real scenario: user performed an action in a region, iteration count included.
    shipping.WriteInformation(
        "User {UserId} shipped order {OrderId} to {Region} — attempt #{Attempt}",
        customerId, orderId, "EU-WEST", 1);
}

Section("3. More than four values — the familiar params path");
{
    // Five+ arguments transparently use the params object[] overload (one array,
    // exactly like the framework). Handy when an event genuinely has many fields.
    var shipmentId = Guid.Parse("1a2b3c4d-5e6f-4071-8899-aabbccddeeff");
    shipping.WriteInformation(
        "Shipment {ShipmentId} via {Carrier} ({Service}) to {City}, {Country} — ETA {Eta:yyyy-MM-dd}",
        shipmentId, "DHL", "Express", "Berlin", "DE", new DateOnly(2026, 6, 16));
}

Section("4. Runtime log level — Nilogger.Log when severity is decided in code");
{
    // A request-logging middleware decides the level from the HTTP status code.
    LogRequest(http, "POST", "/api/orders", 201, 128);
    LogRequest(http, "GET", "/api/orders/99999", 404, 11);
    LogRequest(http, "POST", "/api/payments", 502, 3070);
}

Section("5. WriteError / WriteCritical — all three call forms");
{
    // ── Form 1: WITH exception + typed args ─────────────────────────────────────
    // The most common form in business error handling. Exception is attached to the
    // log entry (structured sinks can index it). Args are typed, zero-array.
    try
    {
        ChargeCard(orderId, amountEur: 129.95m, cardLast4: "0341");
    }
    catch (PaymentDeclinedException ex)
    {
        payments.WriteError("Payment declined for order {OrderId}: {Reason}", ex, orderId, ex.Reason);
    }

    // ── Form 2: WITH exception, plain message (Feature C — fast path) ───────────
    // No template placeholders means no args to bind; Nilog resolves directly to the
    // no-args with-exception overload — faster than any params form.
    try
    {
        VerifyInventoryNode(orderId);
    }
    catch (Exception ex)
    {
        inventory.WriteError("Inventory node check failed — circuit breaker open", ex);
    }

    // ── Form 3: WITHOUT exception, typed args (v1.0.1 NEW) ──────────────────────
    // Before v1.0.1, `WriteError("Failed {Id}", id)` fell back to params object[],
    // boxing the int and building an array. Now it routes to the typed overload
    // (same zero-array path as WriteInformation/WriteWarning) via
    // [OverloadResolutionPriority(-1)] — the compiler only picks it when no
    // with-exception overload is applicable at priority 0.
    orders.WriteError("Order {OrderId} exceeds line-count limit: {Count} lines (max 50)",
        orderId, 51);
    payments.WriteError("Idempotency key collision for merchant {MerchantId}", "MERCH-9912");

    // WriteCritical has the same three forms — shown here with the new typed path.
    startup.WriteCritical("Config reload failed: missing required key {Key}", "ConnectionStrings:Primary");
}

Section("6. Rich exception reports — full diagnostics for the on-call engineer");
{
    try
    {
        LoadCustomerProfile(customerId);
    }
    catch (Exception ex)
    {
        // Aligned, multi-line report. moreDetailsEnabled adds the stack trace and a
        // walk of inner / aggregate exceptions — ideal for an Error-level incident.
        profiles.WriteErrorException(ex, title: "Customer profile load failed", moreDetailsEnabled: true);
    }

    try
    {
        OpenPrimaryDatabase();
    }
    catch (Exception ex)
    {
        // Critical variant — same report, escalated severity.
        startup.WriteCriticalException(ex, title: "Primary database unavailable", moreDetailsEnabled: true);
    }
}

Section("7. Custom exception format — emit JSON for a structured sink");
{
    // Swap the global formatter to match your log pipeline (e.g. JSON for Seq/ELK).
    // Setting it back to null restores Nilog's built-in aligned formatter.
    Nilogger.ExceptionFormatter = (ex, title, verbose) => JsonSerializer.Serialize(new
    {
        title,
        type = ex.GetType().FullName ?? ex.GetType().Name,
        ex.Message,
        inner = ex.InnerException?.Message,
        stack = verbose ? ex.StackTrace : null,
    });

    try
    {
        ConfirmShipment(orderId);
    }
    catch (Exception ex)
    {
        shipping.WriteErrorException(ex, title: "shipment.confirm.failed", moreDetailsEnabled: false);
    }
    finally
    {
        Nilogger.ExceptionFormatter = null!; // restore the default for the rest of the tour
    }
}

Section("8. Scopes — correlate every line in a request without repeating yourself");
{
    var correlationId = Guid.Parse("c0ffee00-1234-4567-89ab-0123456789ab");

    // A single key/value pair (here a correlation id) wraps an entire request.
    using (http.WriteScope("CorrelationId", correlationId))
    {
        http.WriteInformation("Received POST /api/orders");

        // A richer context bag — small bags (<= 4 entries) take an allocation-light path.
        var requestContext = new Dictionary<string, object>
        {
            ["CustomerId"] = customerId,
            ["Tenant"] = "acme-eu",
            ["ClientIp"] = "203.0.113.42",
        };
        using (http.WriteScope(requestContext))
        {
            orders.WriteInformation("Order {OrderId} created", orderId);
            payments.WriteInformation("Payment captured for order {OrderId}", orderId);
        }

        // Feature A: IReadOnlyDictionary (and any IEnumerable<KVP>) routes to the
        // IEnumerable overload. Dictionary<K,V> typed as IDictionary still uses the
        // more-derived IDictionary overload, so existing callers are unaffected.
        IReadOnlyDictionary<string, object> traceCtx = new Dictionary<string, object>
        {
            ["TraceId"] = "4bf92f3577b34da6a3ce929d0e0e4736",
            ["SpanId"] = "00f067aa0ba902b7",
        };
        using (http.WriteScope(traceCtx))
        {
            orders.WriteInformation("Distributed trace context propagated");
        }

        http.WriteInformation("Responded 201 Created in 128 ms");
    }
}

Section("9. Disabled levels are (almost) free — the whole point on a hot path");
{
    // A production logger configured at Information, like a real deployment.
    using ILoggerFactory prodFactory = LoggerFactory.Create(b =>
        b.SetMinimumLevel(LogLevel.Information).AddSimpleConsole(o => o.SingleLine = true));
    ILogger prod = prodFactory.CreateLogger("Checkout.Pricing");

    // This Debug line runs millions of times an hour. Because Debug is filtered out,
    // Nilog returns after the IsEnabled check — no array, no boxing, no formatting.
    for (int i = 0; i < 1_000_000; i++)
    {
        prod.WriteDebug("Price rule {RuleId} evaluated for sku {Sku}", i, "TSHIRT-BLK-L");
    }

    prod.WriteInformation("Repriced catalogue: 1,000,000 rules evaluated, 0 bytes logged at Debug");
}

Section("10. Async / batching hooks for a custom sink");
{
    // Tell a custom async sink which entries are worth shipping off-box (e.g. only
    // Warning and above go to your alerting pipeline). The core path is unaffected.
    Nilogger.UseAsyncSinkProvider((level, message, exception) => level >= LogLevel.Warning);

    startup.WriteInformation("AsyncSinkFilter keeps Information? {Keep}",
        Nilogger.AsyncSinkFilter(LogLevel.Information, "", null!));
    startup.WriteInformation("AsyncSinkFilter keeps Error?       {Keep}",
        Nilogger.AsyncSinkFilter(LogLevel.Error, "", null!));

    // Await a flush during graceful shutdown so buffered work is drained.
    await Nilogger.FlushAsync();
}

Section("11. Template niceties — alignment, format, and literal braces in a report");
{
    // A daily summary line, formatted for a fixed-width report.
    reports.WriteInformation("Revenue {Revenue:N2} EUR  ({Growth:P1} vs. yesterday)", 18_450.75m, 0.073);
    reports.WriteInformation("{Rank,2}. {Product,-22} {Units,7:N0} sold", 1, "Wireless Mouse", 12_500);

    // Double braces are emitted literally — useful when logging code/JSON snippets.
    reports.WriteInformation("Template {{OrderId}} expands to {OrderId}", orderId);
}

// -----------------------------------------------------------------------------
// 12. Graceful shutdown (optional).
//     Nilog hooks process exit automatically; call this only when you want
//     deterministic teardown (tests, short-lived hosts). Safe to call twice.
// -----------------------------------------------------------------------------
Nilogger.ShutdownUtcTimer();

Console.WriteLine();
Console.WriteLine("Checkout tour complete — every Nilog feature shown against a real scenario.");

// =============================================================================
//  Simulated subsystem operations used by the scenarios above.
// =============================================================================

// Request-logging middleware: pick the level from the HTTP status, then log with
// the static API. 1–3 typed args keep this allocation-free even under heavy traffic.
static void LogRequest(ILogger logger, string method, string path, int status, int elapsedMs)
{
    LogLevel level = status >= 500 ? LogLevel.Error
                   : status >= 400 ? LogLevel.Warning
                   : LogLevel.Information;

    Nilogger.Log(logger, level, "HTTP {Method} {Path} responded {Status}", method, path, status);

    // A second line carrying timing, using the typed static overload.
    Nilogger.Log(logger, LogLevel.Debug, "  -> handled in {ElapsedMs} ms", elapsedMs);
}

// Pretend to charge a card; declines an obviously-test card number.
static void ChargeCard(Guid orderId, decimal amountEur, string cardLast4)
{
    if (cardLast4 is "0341")
    {
        throw new PaymentDeclinedException("insufficient_funds");
    }

    _ = (orderId, amountEur);
}

// Loads a customer profile; here the cache misses and the database lookup fails,
// producing a realistic nested exception chain.
static void LoadCustomerProfile(int customerId)
{
    try
    {
        throw new KeyNotFoundException($"profile {customerId} not in cache");
    }
    catch (Exception cacheMiss)
    {
        throw new InvalidOperationException(
            $"Could not load profile for customer {customerId}", cacheMiss);
    }
}

// Simulates a failed primary database connection (timeout under the hood).
static void OpenPrimaryDatabase()
{
    try
    {
        throw new TimeoutException("connect timeout after 5000 ms to db-primary:5432");
    }
    catch (Exception inner)
    {
        throw new InvalidOperationException("Primary database connection failed", inner);
    }
}

// Confirms a shipment by calling two carrier endpoints in parallel; both fail, so
// the caller sees an AggregateException (which the report formatter expands).
static void ConfirmShipment(Guid orderId)
{
    var failures = new Exception[]
    {
        new TimeoutException("DHL label API timed out"),
        new InvalidOperationException("UPS rate API returned 503"),
    };

    throw new AggregateException($"Shipment confirmation failed for order {orderId}", failures);
}

static void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine("== " + title + " " + new string('=', Math.Max(0, 78 - title.Length)));
}

// Simulates an inventory node health check that times out.
static void VerifyInventoryNode(Guid orderId)
{
    _ = orderId;
    throw new TimeoutException("inventory-node-1:6379 did not respond within 200 ms");
}

// A domain-specific exception, the kind you would define in a real payments module.
internal sealed class PaymentDeclinedException(string reason)
    : Exception($"Payment declined ({reason})")
{
    public string Reason { get; } = reason;
}
