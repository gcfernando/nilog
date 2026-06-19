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

// -----------------------------------------------------------------------------
// Best practice: never pass an interpolated string ($"...") as the message.
//     logger.WriteInformation($"User {userId} signed in");   // ❌ defeats the template
//                                                             //    cache and loses
//                                                             //    structured properties
//     logger.WriteInformation("User {UserId} signed in", userId); // ✅ do this instead
// Add the `Nilog.Analyzers` package and the first form raises NILOG001 at compile time
// (with a one-click "convert to template + args" fix). The analyzer also flags
// NILOG002 (placeholder/argument count mismatch), NILOG003 (concatenated/string.Format
// templates), and NILOG004 (a duplicate {Name}), so these mistakes never reach review.
// -----------------------------------------------------------------------------

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

Section("2. Structured logging with typed arguments (1–5, zero array allocation)");
{
    // One to five values bind to strongly-typed overloads: no object[] is built,
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

    // Five-argument typed overload (NEW in v1.0.2) — still zero object[], zero boxing
    // on the disabled path. Before v1.0.2 this fell back to params object[]; now it
    // stays on the typed LogState<T0..T4> path all the way out to 5 arguments.
    var shipmentId = Guid.Parse("1a2b3c4d-5e6f-4071-8899-aabbccddeeff");
    shipping.WriteInformation(
        "Shipment {ShipmentId} via {Carrier} to {City}, {Country} — ETA {Eta:yyyy-MM-dd}",
        shipmentId, "DHL", "Berlin", "DE", new DateOnly(2026, 6, 16));
}

Section("3. Six to eight values — still zero-array typed (NEW in v1.0.3)");
{
    // Six, seven, and eight arguments now bind to source-generated strongly-typed
    // overloads (Nilog.SourceGenerators) — no object[] is built and nothing at all is
    // allocated on the disabled path, exactly like the hand-written 1–5 arg overloads.
    // Before v1.0.3 these fell back to params object[].
    var shipmentId = Guid.Parse("2b3c4d5e-6f70-4182-99aa-bbccddeeff00");
    shipping.WriteInformation(
        "Shipment {ShipmentId} via {Carrier} ({Service}) to {City}, {Country} — ETA {Eta:yyyy-MM-dd}",
        shipmentId, "DHL", "Express", "Berlin", "DE", new DateOnly(2026, 6, 16)); // 6 args, typed

    shipping.WriteInformation(
        "Shipment {ShipmentId} {Carrier} {Service} {City} {Country} {Eta} weight {Kg:N1}kg",
        shipmentId, "DHL", "Express", "Berlin", "DE", new DateOnly(2026, 6, 16), 2.4); // 7 args, typed
}

Section("3b. Nine or more values — the familiar params path");
{
    // Nine+ arguments transparently use the params object[] overload (one array, exactly
    // like the framework). Handy when an event genuinely has many fields — but prefer a
    // scope (Section 8) plus ≤ 8 inline values where you can.
    var shipmentId = Guid.Parse("3c4d5e6f-7081-4293-aabb-ccddeeff0011");
    shipping.WriteInformation(
        "Shipment {ShipmentId} {Carrier} {Service} {City} {Country} {Eta} {Kg} {Zone} {Priority}",
        shipmentId, "DHL", "Express", "Berlin", "DE", new DateOnly(2026, 6, 16), 2.4, "EU", "P1");
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

    // ── Form 3: WITHOUT exception, typed args (1–5, extended in v1.0.2) ─────────
    // Before v1.0.1, `WriteError("Failed {Id}", id)` fell back to params object[],
    // boxing the int and building an array. Now it routes to the typed overload
    // (same zero-array path as WriteInformation/WriteWarning, extended out to 5
    // arguments in v1.0.2) via [OverloadResolutionPriority(-1)] — the compiler only
    // picks it when no with-exception overload is applicable at priority 0.
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

    // Real FlushAsync (v1.0.3): a buffering/batching sink registers how to drain itself, and
    // Nilogger.FlushAsync() awaits every registered sink. With nothing registered it stays a
    // zero-allocation no-op. Here a fake batching sink shows the wiring.
    int pendingBatched = 3;
    Func<CancellationToken, Task> drainBatch = async _ =>
    {
        await Task.Yield();             // pretend to ship the batch off-box
        pendingBatched = 0;
    };
    Nilogger.RegisterFlush(drainBatch);

    // Await a flush during graceful shutdown so buffered work is drained.
    await Nilogger.FlushAsync();
    startup.WriteInformation("Flushed batching sink — pending entries now {Pending}", pendingBatched);

    Nilogger.UnregisterFlush(drainBatch); // tidy up the demo's global registration
}

Section("11. Template niceties — alignment, format, and literal braces in a report");
{
    // A daily summary line, formatted for a fixed-width report.
    reports.WriteInformation("Revenue {Revenue:N2} EUR  ({Growth:P1} vs. yesterday)", 18_450.75m, 0.073);
    reports.WriteInformation("{Rank,2}. {Product,-22} {Units,7:N0} sold", 1, "Wireless Mouse", 12_500);

    // Double braces are emitted literally — useful when logging code/JSON snippets.
    reports.WriteInformation("Template {{OrderId}} expands to {OrderId}", orderId);
}

Section("12. Retry with backoff — structured attempt logging (real-world resilience)");
{
    // Best practice: log every retry attempt at Warning with structured fields (Attempt,
    // Delay, Reason) so an on-call engineer can see the whole retry history in one query —
    // never just "retrying..." with no context, and never string-concatenate the reason.
    bool succeeded = CallFlakyCarrierApi(shipping, orderId, maxAttempts: 3);

    if (!succeeded)
    {
        shipping.WriteError("Carrier API call abandoned for order {OrderId} after {Attempts} attempts",
            orderId, 3);
    }
}

Section("13. Redaction — never log secrets, even by accident");
{
    const string cardNumber = "4242424242424242";

    // ❌ Don't: logging the full PAN. Even at Debug, this can end up in a sink you don't
    // control (App Insights, a shipped log file, a support ticket attachment).
    //     payments.WriteDebug("Charging card {Card}", cardNumber);

    // ✅ Do: log a masked, identifying fragment only — enough to correlate, not enough to leak.
    payments.WriteInformation("Charging card ending {Last4}", cardNumber[^4..]);

    // The same rule applies to tokens, connection strings, and full email addresses —
    // log an id or a masked fragment, never the raw secret.
}

Section("14. Testing — assert on structured output without a real sink");
{
    // A minimal in-memory ILogger (see RecordingLogger below) lets a unit test assert on
    // exactly what Nilog handed to the sink: level, exception, and every named property —
    // without spinning up Console/Serilog/OpenTelemetry just to verify a log line.
    RecordingLogger recorder = new();

    recorder.WriteInformation("Order {OrderId} confirmed for {CustomerId}", orderId, customerId);

    RecordingLogger.Entry entry = recorder.Entries[0];
    bool propertyMatches = Equals(entry["OrderId"], orderId) && Equals(entry["CustomerId"], customerId);

    reports.WriteInformation(
        "Test assertion — captured {Count} entries, OrderId/CustomerId match: {Matches}",
        recorder.Entries.Count, propertyMatches);
}

// -----------------------------------------------------------------------------
// 15. Graceful shutdown (optional).
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

// -----------------------------------------------------------------------------
// Retry-with-backoff helper for Section 12. The point isn't the retry mechanics —
// it's that every attempt, success, and final failure carries structured context
// (Attempt, Delay, Reason) instead of an unstructured "retrying..." string.
// -----------------------------------------------------------------------------
static bool CallFlakyCarrierApi(ILogger logger, Guid orderId, int maxAttempts)
{
    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            // Fails the first two attempts, succeeds on the third — a realistic
            // "transient failure that resolves itself" shape.
            if (attempt < 3)
            {
                throw new TimeoutException("carrier-api.dhl.com did not respond within 2000 ms");
            }

            logger.WriteInformation("Carrier API call for order {OrderId} succeeded on attempt {Attempt}",
                orderId, attempt);
            return true;
        }
        catch (TimeoutException ex) when (attempt < maxAttempts)
        {
            int delayMs = 100 * attempt; // simple linear backoff for the demo
            logger.WriteWarning(
                "Carrier API call for order {OrderId} failed on attempt {Attempt}, retrying in {DelayMs} ms: {Reason}",
                orderId, attempt, delayMs, ex.Message);
        }
        catch (TimeoutException)
        {
            // Final attempt exhausted — let the caller decide how to log the abandonment.
            return false;
        }
    }

    return false;
}

// A domain-specific exception, the kind you would define in a real payments module.
internal sealed class PaymentDeclinedException(string reason)
    : Exception($"Payment declined ({reason})")
{
    public string Reason { get; } = reason;
}

// -----------------------------------------------------------------------------
// Minimal in-memory ILogger for Section 14 — demonstrates that you can unit test
// "did this method log the right structured properties?" without a real sink.
// A fuller version of this same idea lives in Nilog.Tests/TestLogger.cs.
// -----------------------------------------------------------------------------
internal sealed class RecordingLogger : ILogger
{
    public sealed class Entry
    {
        public LogLevel Level { get; init; }
        public string Message { get; init; } = "";
        public IReadOnlyList<KeyValuePair<string, object?>> State { get; init; } = [];

        public object? this[string key] =>
            State.FirstOrDefault(kv => kv.Key == key).Value;
    }

    public List<Entry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScopeDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        List<KeyValuePair<string, object?>> kvps = [];
        if (state is IEnumerable<KeyValuePair<string, object>> enumerable)
        {
            foreach (KeyValuePair<string, object> kv in enumerable)
            {
                kvps.Add(new KeyValuePair<string, object?>(kv.Key, kv.Value));
            }
        }

        Entries.Add(new Entry { Level = logLevel, Message = formatter(state, exception), State = kvps });
    }

    private sealed class NullScopeDisposable : IDisposable
    {
        public static readonly NullScopeDisposable Instance = new();
        public void Dispose() { }
    }
}
