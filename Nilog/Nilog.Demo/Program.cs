// -----------------------------------------------------------------------------
//  Nilog demo — a runnable, commented feature tour that walks through every
//  public Nilog feature end to end.
//
//  File        : Program.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
// =============================================================================
//  Nilog - Feature Tour
// -----------------------------------------------------------------------------
//  This program is a hands-on tutorial. Run it top to bottom and read the
//  comments next to each call - by the end you will have seen every public
//  feature of Nilog and know exactly when to reach for each one.
//
//      dotnet run -c Release --project Nilog.Demo
// =============================================================================

using Microsoft.Extensions.Logging;
using Nilog;

// -----------------------------------------------------------------------------
// 0. Set up a logger.
//    Nilog rides on top of Microsoft.Extensions.Logging, so any ILogger works -
//    console here, but Serilog/OpenTelemetry/etc. behave identically. We turn the
//    minimum level down to Trace so every example below is actually emitted.
// -----------------------------------------------------------------------------
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
{
    _ = builder
        .SetMinimumLevel(LogLevel.Trace)
        .AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
});

ILogger logger = loggerFactory.CreateLogger("Demo");

Section("1. The six log levels (plain messages)");
{
    // The Write* extension methods mirror the standard levels. With no arguments
    // they take the fast, no-allocation path through a pre-compiled delegate.
    logger.WriteTrace("Trace - the most detailed, usually off in production.");
    logger.WriteDebug("Debug - useful while developing.");
    logger.WriteInformation("Information - the normal heartbeat of the app.");
    logger.WriteWarning("Warning - something looks off but we carried on.");
    logger.WriteError("Error - an operation failed.");
    logger.WriteCritical("Critical - the app (or a subsystem) is going down.");
}

Section("2. Structured logging with strongly-typed arguments (zero array allocation)");
{
    // One, two, or three arguments bind to strongly-typed overloads. No object[]
    // is allocated, and the named placeholders ({UserId}, {Amount}, ...) flow
    // through as structured properties to any structured sink.
    logger.WriteInformation("User {UserId} signed in", 42);
    logger.WriteInformation("Order {OrderId} totalled {Amount:C}", 1001, 19.95m);
    logger.WriteInformation("Cache {Name} hit {Hits} miss {Misses}", "users", 980, 20);
}

Section("3. More than three arguments (params fallback)");
{
    // Need four or more? There is still an overload - it just allocates the usual
    // params array, exactly like the framework's own logging extensions.
    logger.WriteInformation("Grid {A} {B} {C} {D}", 1, 2, 3, 4);
}

Section("4. The static Nilogger.Log API (when the level is a variable)");
{
    // Sometimes the level is decided at runtime. The static Log overloads take the
    // level as a parameter and otherwise behave just like the extensions above.
    LogLevel level = DateTime.UtcNow.Second % 2 == 0 ? LogLevel.Information : LogLevel.Warning;
    Nilogger.Log(logger, level, "Dynamic level chosen at runtime");
    Nilogger.Log(logger, LogLevel.Information, "Templated static log: {Value}", 7);
}

Section("5. Logging exceptions alongside a message");
{
    try
    {
        _ = ParseBudget("not-a-number");
    }
    catch (Exception ex)
    {
        // Attach the exception and still use a structured template.
        logger.WriteError("Failed to parse budget for tenant {TenantId}", ex, 7);
    }
}

Section("6. Rich exception reports (WriteErrorException / WriteCriticalException)");
{
    try
    {
        LoadProfile();
    }
    catch (Exception ex)
    {
        // A formatted, aligned report. Pass moreDetailsEnabled: true to also include
        // the stack trace and a walk of inner exceptions.
        logger.WriteErrorException(ex, title: "Profile load failed", moreDetailsEnabled: true);
    }
}

Section("7. Customising how exceptions are rendered");
{
    // Swap in your own formatter (JSON, key=value, whatever your sink prefers).
    // Setting it back to null restores the built-in renderer.
    Nilogger.ExceptionFormatter = (ex, title, _) => $"[{title}] {ex.GetType().Name}: {ex.Message}";
    try
    {
        _ = ParseBudget("oops");
    }
    catch (Exception ex)
    {
        logger.WriteErrorException(ex, title: "Compact");
    }
    finally
    {
        Nilogger.ExceptionFormatter = null!; // restore the default
    }
}

Section("8. Scopes - attach context to everything logged inside a block");
{
    // A single key/value pair...
    using (logger.WriteScope("RequestId", Guid.NewGuid()))
    {
        logger.WriteInformation("Handling request");

        // ...or a whole bag of context. Small bags (<= 4 entries) take an
        // allocation-light path; larger ones fall back to a list.
        Dictionary<string, object> context = new()
        {
            ["UserId"] = 42,
            ["Tenant"] = "acme",
            ["Region"] = "eu-west-1",
        };
        using (logger.WriteScope(context))
        {
            logger.WriteWarning("Quota at {Percent}%", 90);
        }
    }
}

Section("9. Disabled levels cost (almost) nothing");
{
    // Spin up a logger that only listens at Warning and above. Trace/Debug/Info
    // calls return immediately after the IsEnabled check - no array, no boxing,
    // no formatting. This is the whole point of Nilog on a hot path.
    using ILoggerFactory quietFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning).AddSimpleConsole(o => o.SingleLine = true));
    ILogger quiet = quietFactory.CreateLogger("Quiet");

    quiet.WriteInformation("You will NOT see this - filtered out for free.");
    quiet.WriteWarning("You WILL see this one.");
}

Section("10. Async / extensibility hooks");
{
    // A filter you can use when wiring Nilog into a custom async or batching sink.
    Nilogger.UseAsyncSinkProvider((level, _, _) => level >= LogLevel.Information);
    logger.WriteInformation("AsyncSinkFilter(Info) = {Keep}", Nilogger.AsyncSinkFilter(LogLevel.Information, "", null!));
    logger.WriteInformation("AsyncSinkFilter(Trace) = {Keep}", Nilogger.AsyncSinkFilter(LogLevel.Trace, "", null!));

    // Await a flush. It is a no-op today but keeps your shutdown code correct if a
    // buffering sink is added later.
    await Nilogger.FlushAsync();
}

// -----------------------------------------------------------------------------
// 11. Clean shutdown (optional).
//     Nilog hooks process exit automatically, so this is only needed when you want
//     deterministic teardown (tests, short-lived hosts). Safe to call more than once.
// -----------------------------------------------------------------------------
Nilogger.ShutdownUtcTimer();

Console.WriteLine();
Console.WriteLine("Tour complete. You have now seen every Nilog feature. Happy (fast) logging!");

// =============================================================================
//  Helpers used by the examples above.
// =============================================================================

static int ParseBudget(string raw)
{
    return int.TryParse(raw, out int value)
        ? value
        : throw new FormatException($"'{raw}' is not a valid budget.");
}

static void LoadProfile()
{
    try
    {
        throw new KeyNotFoundException("profile 'alice' not found in cache");
    }
    catch (Exception inner)
    {
        throw new InvalidOperationException("Could not load user profile", inner);
    }
}

static void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine("== " + title + " ".PadRight(Math.Max(0, 74 - title.Length), '='));
}
