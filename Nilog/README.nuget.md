# ⚡ Nilog

### Zero-allocation, high-performance logging for `Microsoft.Extensions.Logging`

**Same `ILogger`. Same `{Named}` templates. None of the garbage.**

[![NuGet](https://img.shields.io/badge/NuGet-v1.0.0-004880?logo=nuget&logoColor=white)](https://www.nuget.org/packages/Nilog)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/gcfernando/Nilog/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)

> 📖 **Full documentation, diagrams, and recipes:** [github.com/gcfernando/Nilog](https://github.com/gcfernando/Nilog)

---

The stock `ILogger` extensions allocate a **`params object[]` on every single call** — even when
the level is switched off and the message is thrown straight in the bin. On a hot path that is
millions of pointless allocations and a busy garbage collector.

**Nilog swaps that array for a stack-only struct.** A disabled call now allocates **nothing** and
returns in **under a nanosecond**.

```csharp
using Nilog;

logger.WriteInformation("User {UserId} ordered {Count} items", userId, count);
//      ^ no object[] allocated, ever — and nothing at all when Information is disabled
```

### Before / After

**Plain `Microsoft.Extensions.Logging`** — allocates an `object[]` and boxes the ints on every
call, even when `Debug` is off:

```csharp
logger.LogDebug("User {Id} did {Action}", id, action);
```

**Nilog** — stack-only struct, zero allocation when disabled, ~30% less when enabled:

```csharp
logger.WriteDebug("User {Id} did {Action}", id, action);
```

---

## ⚡ Why Nilog?

| | |
|--|--|
| 🚀 **Zero-alloc disabled path** | A filtered-out call returns in under a nanosecond and allocates **0 bytes**. |
| 🧠 **Lean enabled path** | Strongly-typed structs render **~20–40% faster** and use **~30% less** memory than `params`. |
| 🔌 **Drop-in** | Same `ILogger`, same `{Named}` templates, same structured output to Serilog / OTel / Seq / App Insights. |
| 🧩 **Zero setup** | Just `using Nilog;` — no DI, no registration, no config files. |
| 🧯 **Never throws** | A bad template falls back to raw text instead of throwing a `FormatException`. |
| 🧵 **Thread-safe & AOT-ready** | Immutable static state, no reflection. Safe under contention, friendly to trimming and Native AOT. |
| 🎯 **Multi-target** | .NET 8 · 9 · 10, as a single `Nilog.dll` with XML docs and SourceLink. |

---

## 🆚 Nilog vs the alternatives

Every row is phrased so **✅ is always the good result** (✅ yes/good · ❌ no · ➖ partial).

| Question | Microsoft `ILogger` | Serilog | **Nilog** |
|----------|:---:|:---:|:---:|
| Plugs into your existing `ILogger` & DI? | ✅ | ➖ | ✅ |
| Supports `{Named}` templates + structured properties? | ✅ | ✅ | ✅ |
| **Avoids the `object[]` allocation per call (1–3 args)?** | ❌ | ❌ | ✅ |
| **Allocates nothing when the level is disabled?** | ❌ | ❌ | ✅ |
| `LoggerMessage` speed with no boilerplate? | ❌ | ❌ | ✅ |
| Built-in formatted exception report? | ➖ | ➖ | ✅ |
| Zero-allocation single-key scope object? | ❌ | ❌ | ✅ |
| Needs zero setup (just `using Nilog;`)? | ✅ | ❌ | ✅ |

*These marks describe design behaviour, not a head-to-head benchmark of other libraries. The hard
numbers below are measured directly against the Microsoft extensions.*

---

## 📊 Benchmarks

Measured with BenchmarkDotNet v0.15.8 on **.NET 8.0.27**, AMD Ryzen AI 9 365, Windows 11.

### The headline: a disabled log call

```text
Time per call — lower is better (nanoseconds)
Microsoft  ████████████████████████████████████████████████  142.48 ns
Nilog      ▏                                                    0.48 ns   ← ~297× faster

Allocations per call — lower is better (bytes)
Microsoft  ████████████████████████████████████████████████  208 B
Nilog                                                           0 B     ← nothing
```

| Method | Mean | Allocated |
|--------|-----:|----------:|
| Microsoft `.LogInformation` | 142.48 ns | 208 B |
| **Nilog `.WriteInformation`** | **0.48 ns** | **0 B** |

### Enabled calls (level on, message rendered)

| Scenario | Library | Mean | Allocated |
|----------|---------|-----:|----------:|
| 1 argument  | Microsoft | 72.03 ns | 112 B |
| 1 argument  | **Nilog** | **56.31 ns** `0.78×` | **80 B** `0.71×` |
| 3 arguments | Microsoft | 120.65 ns | 152 B |
| 3 arguments | **Nilog** | **72.16 ns** `0.60×` | **104 B** `0.68×` |

### Throughput & contention

| Benchmark | Microsoft | Nilog | Result |
|-----------|----------:|------:|:------:|
| 100,000 sequential logs | 9.71 ms · 15.22 MB | **7.02 ms · 11.41 MB** | **1.38× faster · 25% less RAM** |
| 50,000 logs across all cores | 1.60 ms · 5.35 MB | **1.52 ms · 3.82 MB** | **29% less RAM** |

### Scopes & exceptions

| Operation | Mean | Allocated |
|-----------|-----:|----------:|
| `WriteScope("RequestId", value)` — single pair | 11.40 ns | 24 B † |
| `WriteScope(dictionary)` — 3 entries | 62.80 ns | 152 B |
| `WriteError(msg, ex, arg)` | 49.47 ns | 72 B |
| `WriteErrorException(ex)` — summary | 176.6 ns | 992 B |
| `WriteErrorException(ex, more: true)` — full report | 4.20 µs | 8.13 KB |

† The scope object itself is allocation-free; the 24 B is just boxing the value-type value
(`int` here). A reference-type value adds nothing.

---

## 📦 Install

```bash
dotnet add package Nilog
```

Targets **.NET 8.0, 9.0, and 10.0**. Dependencies: only `Microsoft.Extensions.Logging.Abstractions`
and `Microsoft.Extensions.ObjectPool`. Native AOT / trimming friendly. Thread-safe.

---

## 🚀 Quick start

```csharp
using Microsoft.Extensions.Logging;
using Nilog; // <- that's the whole setup

ILogger logger = LoggerFactory
    .Create(b => b.AddConsole())
    .CreateLogger("App");

// Plain message
logger.WriteInformation("Service started");

// Structured, strongly-typed, zero array allocation
logger.WriteInformation("User {UserId} signed in from {Ip}", 42, "10.0.0.1");

// An exception with context
try { Risky(); }
catch (Exception ex)
{
    logger.WriteError("Checkout failed for cart {CartId}", ex, cartId);
}
```

Everything flows through the standard pipeline, so structured properties and the original template
reach Serilog, OpenTelemetry, Seq, Application Insights, or any other sink exactly as usual.

---

## 🧭 Choosing the right method

| I want to… | Call | Allocates? |
|------------|------|:----------:|
| Log a constant message | `logger.WriteInformation("Started")` | none |
| Log 1–3 structured values | `logger.WriteInformation("User {Id}", id)` | **none** (typed) |
| Log 4+ structured values | `logger.WriteInformation("{A} {B} {C} {D}", …)` | one `object[]` |
| Log an error **with** an exception | `logger.WriteError("Failed {Id}", ex, id)` | **none** (typed) |
| Log an error **without** an exception | `logger.WriteError("Bad request")` | none (no args) |
| Produce a full exception report | `logger.WriteErrorException(ex, "Title", more: true)` | report buffer only |
| Decide the level at runtime | `Nilogger.Log(logger, level, "…", a, b)` | **none** for 1–3 typed |
| Attach context to a block | `using (logger.WriteScope("Key", value)) { … }` | only the boxed value (~24 B) |

> **Tip:** Keep templates to ≤ 3 named holes to stay on the zero-array typed path. For
> error/critical, pass the exception to get the typed (zero-array) overload.

---

## ✨ Features

### Six levels, two styles

```csharp
logger.WriteTrace("...");
logger.WriteDebug("...");
logger.WriteInformation("...");
logger.WriteWarning("...");
logger.WriteError("...");
logger.WriteCritical("...");
```

When the level is decided at runtime, use the static API:

```csharp
LogLevel level = isVerbose ? LogLevel.Debug : LogLevel.Information;
Nilogger.Log(logger, level, "Processing {Job}", jobId);
```

### Structured logging without the allocation

One to three arguments bind to strongly-typed overloads — no `object[]`, and nothing at all when
the level is off. Four or more fall back to the familiar `params` form. Escaping and
alignment/format specifiers all work:

```csharp
logger.WriteInformation("Order {Id} total {Amount:C}", orderId, amount);
logger.WriteInformation("Progress {Percent,3}% of {{total}}", 7);   // "Progress   7% of {total}"
logger.WriteInformation("Id {Id:000}", 42);                          // "Id 042"
```

### Rich exception reports

```csharp
catch (Exception ex)
{
    logger.WriteErrorException(ex, title: "Payment failed");                         // one-line summary
    logger.WriteErrorException(ex, title: "Payment failed", moreDetailsEnabled: true); // + stack & inner
}
```

The built-in formatter produces an aligned, multi-line block (timestamp, title, type, message,
HResult, source, target site, and — in verbose mode — stack trace and inner/aggregate exceptions).

> **Note:** If a console shows the report on one line, that's the console formatter, not Nilog.
> Microsoft's `SimpleConsole` replaces newlines with spaces when `SingleLine = true`. Use
> `o.SingleLine = false` (the default). File sinks, Azure (App Insights / Log Analytics), Seq, and
> JSON sinks preserve the line breaks regardless.

Swap the format globally (set back to `null` to restore the default):

```csharp
Nilogger.ExceptionFormatter = (ex, title, verbose) =>
    JsonSerializer.Serialize(new { title, type = ex.GetType().Name, ex.Message });
```

### Scopes

```csharp
using (logger.WriteScope("RequestId", requestId))
{
    logger.WriteInformation("Handling request");   // carries RequestId

    using (logger.WriteScope(new Dictionary<string, object> { ["UserId"] = userId, ["Tenant"] = tenant }))
    {
        logger.WriteWarning("Quota at {Percent}%", 90);  // carries RequestId + UserId + Tenant
    }
}
```

The scope object itself is allocation-free; a single value-type value only costs its boxing.
Values are copied, so mutating your dictionary afterwards never corrupts a scope.

### Async / extensibility hooks

```csharp
Nilogger.UseAsyncSinkProvider((level, message, ex) => level >= LogLevel.Information);
await Nilogger.FlushAsync();   // no-op today; keeps your shutdown code correct tomorrow
```

---

## 🧩 Structured logging, end to end

A single call carries the rendered message, the named properties, and the original template
(`{OriginalFormat}`) — with no array in sight:

```csharp
logger.WriteInformation("User {UserId} bought {Sku} x{Qty}", 42, "A-100", 3);
```

| What the sink receives | Value |
|------------------------|-------|
| Rendered message | `User 42 bought A-100 x3` |
| `UserId` | `42` |
| `Sku` | `"A-100"` |
| `Qty` | `3` |
| `{OriginalFormat}` | `User {UserId} bought {Sku} x{Qty}` |

---

## ⚙️ Global configuration

Static, process-wide settings on `Nilogger` — set once at startup, no DI required.

```csharp
// Customise how exceptions are rendered (null restores the built-in formatter).
Nilogger.ExceptionFormatter = (exception, title, verbose) =>
    $"[{title}] {exception.GetType().Name}: {exception.Message}";

// Filter for a custom async/batch sink (default: keep everything).
Nilogger.UseAsyncSinkProvider((level, message, exception) => level >= LogLevel.Information);

await Nilogger.FlushAsync();   // optional flush hook
Nilogger.ShutdownUtcTimer();   // optional deterministic teardown (auto on process exit)
```

| Setting | Default | Controls |
|---------|---------|----------|
| `Nilogger.ExceptionFormatter` | built-in aligned report | How exception reports render. `null` restores the default. |
| `Nilogger.UseAsyncSinkProvider(filter)` | keep everything | Predicate for a custom async/batch sink. `null` leaves it unchanged. |
| `Nilogger.FlushAsync(token)` | no-op | Awaitable flush hook. Never throws. |
| `Nilogger.ShutdownUtcTimer()` | auto on exit | Stops the timestamp-cache timer. Idempotent. |

> **Note:** Log levels and category filters are **not** a Nilog setting. Configure minimum levels
> the usual way (logging builder or `appsettings.json`); Nilog's `IsEnabled` checks honour all of it.

---

## ✅ Best practices

| Do ✅ | Don't ❌ |
|------|---------|
| `logger.WriteInformation("User {Id}", id)` — templated & structured | `logger.WriteInformation($"User {id}")` — interpolation kills structure and always allocates |
| Keep to ≤ 3 named holes for the zero-array path | Pack 6 values into one line and take the `params` array |
| Pass the exception: `WriteError("msg {X}", ex, x)` | `WriteError("msg " + value)` — string concatenation |
| Let the level filter decide; Nilog checks `IsEnabled` for you | Wrap calls in your own `if (logger.IsEnabled(...))` |
| Use `WriteScope` for request/correlation context | Re-log the same ids on every line |

---

## 🔀 Migrating to Nilog

Mostly a find-and-replace. Templates and semantics are identical — only the method name (and, for
errors, the argument order) changes.

| Microsoft | Nilog |
|-----------|-------|
| `logger.LogInformation("…")` | `logger.WriteInformation("…")` |
| `logger.LogWarning("…")` | `logger.WriteWarning("…")` |
| `logger.LogError(ex, "Failed {Id}", id)` | `logger.WriteError("Failed {Id}", ex, id)` — exception moves **after** the message |
| `logger.LogCritical(ex, "…")` | `logger.WriteCritical("…", ex)` |
| `logger.BeginScope(state)` | `logger.WriteScope(key, value)` / `logger.WriteScope(dictionary)` |

> **Important:** Microsoft's `LogError(exception, message, …)` takes the exception **first**;
> Nilog's `WriteError(message, exception, …)` takes the message **first**. Double-check that swap.

---

## 📖 API reference

All members live in namespace `Nilog`. `Write*` are extension methods on `ILogger`; `Nilogger`
also exposes a static API and the global settings.

**Level methods (Trace / Debug / Information / Warning)** — typed for 1–3 args, params for the rest:

```csharp
void WriteTrace(this ILogger logger, string message, params object[] args);
void WriteTrace<T0>(this ILogger logger, string message, T0 arg0);
void WriteTrace<T0,T1>(this ILogger logger, string message, T0 arg0, T1 arg1);
void WriteTrace<T0,T1,T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2);
// identical shape for WriteDebug, WriteInformation, WriteWarning
```

**Error / Critical** — without an exception (params), or with an exception (typed for 1–3):

```csharp
void WriteError(this ILogger logger, string message, params object[] args);
void WriteError(this ILogger logger, string message, Exception exception, params object[] args);
void WriteError<T0>(this ILogger logger, string message, Exception exception, T0 arg0);
void WriteError<T0,T1>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1);
void WriteError<T0,T1,T2>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1, T2 arg2);
// identical shape for WriteCritical
```

**Exception reports, scopes, static Log, and global settings:**

```csharp
void WriteErrorException(this ILogger logger, Exception ex, string title = "System Error", bool moreDetailsEnabled = false);
void WriteCriticalException(this ILogger logger, Exception ex, string title = "Critical System Error", bool moreDetailsEnabled = false);

IDisposable WriteScope(this ILogger logger, string key, object value);
IDisposable WriteScope(this ILogger logger, IDictionary<string, object> context);

void Nilogger.Log(ILogger logger, LogLevel level, string message);
void Nilogger.Log<T1>(ILogger logger, LogLevel level, string message, T1 arg1);            // + <T1,T2>, <T1,T2,T3>
void Nilogger.Log(ILogger logger, LogLevel level, string message, Exception exception, params object[] args);
void Nilogger.Log(ILogger logger, LogLevel level, Exception exception, string messageTemplate, params object[] args);

static Func<Exception, string, bool, string> Nilogger.ExceptionFormatter { get; set; }
static Func<LogLevel, string, Exception, bool> Nilogger.AsyncSinkFilter { get; }
static void Nilogger.UseAsyncSinkProvider(Func<LogLevel, string, Exception, bool> filter);
static Task Nilogger.FlushAsync(CancellationToken cancellationToken = default);
static void Nilogger.ShutdownUtcTimer();
```

---

## 🔬 How it works

- **Strongly-typed overloads beat `params`.** A 1–3 argument call binds to a generic overload in
  *normal form*, which the compiler prefers over the *expanded* params form — no array at the call site.
- **`IsEnabled` is checked first, always.** A disabled call returns before any argument is touched,
  so it boxes nothing and allocates nothing.
- **Stack-only log state.** Arguments are wrapped in a `readonly struct` that implements
  `IReadOnlyList<KeyValuePair<string, object>>`, so structured sinks still get named properties and
  `{OriginalFormat}` — without a heap allocation for the carrier.
- **Cached everything.** Parsed templates, per-level `LoggerMessage` delegates, `EventId`s, and a
  pooled `StringBuilder` are shared process-wide.
- **Logging never throws.** A template/argument mismatch falls back to the raw template.

---

## ❓ FAQ

**Does Nilog replace my logging framework?**
No. It is a set of `ILogger` extensions. Your provider/sink stays exactly as it is.

**Why `Write*` and not `Log*`?**
`LogInformation`, `LogError`, etc. are taken by Microsoft's own extensions; reusing them causes
ambiguous-call errors when both namespaces are imported. `Write*` keeps Nilog unambiguous.

**Is it really zero allocation?**
On the disabled path, yes — 0 bytes and under a nanosecond. On the enabled path Nilog still
allocates the rendered string (every logger must) but avoids the `object[]` and, for 1–3 args, any
extra carrier — roughly 30% less than the framework.

**What about 4+ arguments?**
No typed overload past three, so it falls back to a `params object[]` array — same as the framework.

**Is it AOT / trimming safe?**
Yes — generics, `LoggerMessage`, pooling, `string.Format`; no reflection.

---

## 📄 License

MIT © Gehan Fernando. Full docs at [github.com/gcfernando/Nilog](https://github.com/gcfernando/Nilog).
