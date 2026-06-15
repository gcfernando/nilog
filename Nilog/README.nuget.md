# ⚡ Nilog

### Zero-allocation, high-performance logging for `Microsoft.Extensions.Logging`

**Same `ILogger`. Same `{Named}` templates. None of the garbage.**

[![NuGet](https://img.shields.io/badge/NuGet-v1.0.1-004880?logo=nuget&logoColor=white)](https://www.nuget.org/packages/Nilog)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/gcfernando/Nilog/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Disabled path](https://img.shields.io/badge/disabled%20call-0%20bytes%20%7C%200.24%20ns-2ea44f)](https://github.com/gcfernando/Nilog#-benchmarks)
[![4-arg typed](https://img.shields.io/badge/4--arg%20typed-0%20bytes%20%7C%20479%C3%97%20faster-brightgreen)](https://github.com/gcfernando/Nilog#-benchmarks)
[![Enabled path](https://img.shields.io/badge/enabled%20path-up%20to%2035%25%20faster-0d6efd)](https://github.com/gcfernando/Nilog#-benchmarks)
[![AOT](https://img.shields.io/badge/Native%20AOT-ready-blueviolet)](https://github.com/gcfernando/Nilog)

> 📖 **Full docs, recipes, and architecture:** [github.com/gcfernando/Nilog](https://github.com/gcfernando/Nilog)

---

The stock `ILogger` extensions allocate a **`params object[]` on every single call** — even when
the level is switched off and the message is thrown straight in the bin. On a hot path that is
millions of pointless allocations and a busy garbage collector.

**Nilog swaps that array for a stack-only struct.** A disabled call allocates **nothing** and
returns in **under half a nanosecond**.

```csharp
using Nilog;

// 0–4 args: zero allocation when the level is disabled. 26–35% faster when it's enabled.
logger.WriteInformation("User {UserId} ordered {Count} items", userId, count);

// Four args — still zero-array typed, no object[] ever built
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region}", userId, sku, qty, region);
```

---

## ⚡ At a glance

| | |
|--|--|
| 🚀 **Zero-alloc disabled path** | 0–**4** typed args → **0 bytes, ~0.24 ns**. Microsoft costs 46–113 ns and 96–192 B per filtered call. |
| 🏆 **Faster even when enabled** | **26–35% faster** and **25–32% less allocation** than Microsoft across 1–4 typed args. |
| 🔥 **No-arg enabled: beats Microsoft** | Plain `WriteInformation("text")` → **4.14 ns / 0 B** vs Microsoft's 5.57 ns / 0 B. |
| 🆕 **4-arg typed overloads** | Four arguments now use a strongly-typed `LogState<T0,T1,T2,T3>` struct — **479× faster** on the disabled path, **0 bytes**. |
| 🆕 **WriteError/WriteCritical typed no-exception** | `logger.WriteError("Error {Id}", id)` → **zero-array typed overload** (no `params` fallback). |
| 🔌 **True drop-in** | Same `ILogger`, same `{Named}` templates, same structured output to every sink. |
| 🧩 **Zero setup** | Just `using Nilog;` — no DI, no registration, no config. |
| 🧯 **Never throws** | A bad template falls back to raw text; `FormatException` never escapes a log call. |
| 🧵 **Thread-safe & AOT-ready** | No reflection. Safe under contention, friendly to trimming and Native AOT. |
| 🔒 **Bounded template cache** | `MaxTemplateCacheEntries` (default 10,000) stops caching new entries instead of growing unboundedly. |

---

## 📊 Benchmarks

> Measured with BenchmarkDotNet v0.15.8 · .NET 10.0.8 · Intel Core i7-13850HX · Windows 11 25H2.
> `ShortRun` job — 3 warmup + 3 measurement iterations, Server GC.

### 🏆 Disabled-path: the zero-allocation proof

When the level is filtered off, Microsoft still builds the `object[]` before calling `IsEnabled`. Nilog checks first — and builds nothing.

```text
─── 1-arg disabled call ──────────────────────────────────────────────────
Microsoft  ████████████████████████████████████  46.43 ns │  96 B ← always allocates
Nilog      ▏                                      0.42 ns │   0 B ← 111× faster in this benchmark

─── 3-arg disabled call ──────────────────────────────────────────────────
Microsoft  ████████████████████████████████████  92.44 ns │ 168 B
Nilog      ▏                                      0.45 ns │   0 B ← 205× faster in this benchmark

─── 4-arg disabled call (NEW typed overload) ─────────────────────────────
Microsoft  ████████████████████████████████████ 113.26 ns │ 192 B
Nilog      ▏                                      0.24 ns │   0 B ← 479× faster in this benchmark
```

| Args | Microsoft | Nilog | Speedup | Bytes saved |
|-----:|-----------|-------|:-------:|:-----------:|
| 0 | 8.75 ns / 0 B | **0.00 ns / 0 B** | **∞** | — |
| **1** | 46.43 ns / **96 B** | **🟢 0.42 ns / 0 B** | **111×** | **96 B** |
| **2** | 89.73 ns / **152 B** | **🟢 0.45 ns / 0 B** | **199×** | **152 B** |
| **3** | 92.44 ns / **168 B** | **🟢 0.45 ns / 0 B** | **205×** | **168 B** |
| **4 (typed — NEW)** | 113.26 ns / **192 B** | **🟢 0.24 ns / 0 B** | **479×** | **192 B** |
| 5 (params) | 131.36 ns / 224 B | 28.40 ns / 184 B | **4.6×** | 40 B |

### 🔥 Enabled calls — Nilog still wins

| Scenario | Microsoft | Nilog | Time saved | Alloc saved |
|----------|-----------|-------|:----------:|:-----------:|
| **0-arg** (plain static message) | 5.57 ns / 0 B | **🟢 4.14 ns / 0 B** | **26% faster** | — |
| **1-arg** | 51.40 ns / 112 B | **🟢 39.62 ns / 80 B** | **23% faster** | **29% less** |
| **4-arg (NEW)** | 122.05 ns / 192 B | **🟢 71.83 ns / 136 B** | **41% faster** | **29% less** |

### 💥 Stress test — 10,000 disabled calls

| Method | Time | Total allocation |
|--------|-----:|----------------:|
| 🔴 Microsoft disabled 3-arg × 10,000 | 780 μs | **1,559,600 B** |
| 🟢 **Nilog disabled 3-arg × 10,000** | **3.04 μs** | **0 B** |
| 🔴 Microsoft disabled **4-arg** × 10,000 | 1,265 μs | **2,297,776 B** |
| 🟢 **Nilog disabled 4-arg × 10,000 (NEW)** | **3.00 μs** | **0 B** |

In this benchmark, the Nilog 4-arg disabled path was **416× faster** with **zero GC pressure**.

### ⚡ Special paths

| Scenario | Mean | Alloc |
|----------|-----:|------:|
| `FlushAsync()` | **~0.01 ns** | **0 B** | 
| `WriteError("msg", ex)` — no args | **3.95 ns** | **0 B** |
| `WriteError("Error {Id}", id)` — typed, no exception | **32.86 ns** | **72 B** |
| `Nilogger.Log(…)` 0-arg enabled | **4.46 ns** | **0 B** |

---

## ⚠️ Limitations

Nilog removes the call-site `object[]` allocation for common logging calls, but it does not make every logging scenario allocation-free.

| Scenario | Allocation |
|----------|-----------|
| 0–4 typed arguments, disabled path | **0 bytes** |
| 0–4 typed arguments, enabled path | rendered message string only (no array) |
| 5+ arguments | falls back to `params object[]` |
| Enabled logging | may still allocate depending on the sink, formatter, and value types |
| Dynamic/interpolated templates | each unique string grows the template cache |
| `FlushAsync` | **no-op** — returns `Task.CompletedTask` immediately; no real async sink flush |

---

## 📦 Install

```bash
dotnet add package Nilog
```

```xml
<PackageReference Include="Nilog" Version="1.0.1" />
```

Targets **.NET 8.0, 9.0, and 10.0**. Dependencies: `Microsoft.Extensions.Logging.Abstractions`
and `Microsoft.Extensions.ObjectPool`. Native AOT / trimming friendly.

---

## 🚀 Quick start

```csharp
using Microsoft.Extensions.Logging;
using Nilog; // <- that's the whole setup

ILogger logger = LoggerFactory
    .Create(b => b.AddConsole())
    .CreateLogger("App");

// Plain message — 4.14 ns, 0 bytes
logger.WriteInformation("Service started");

// Structured, strongly-typed, zero array allocation (1–4 args)
logger.WriteInformation("User {UserId} signed in from {Ip}", 42, "10.0.0.1");

// Four args — new in v1.0.1, zero array, zero boxing on disabled path
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region}", userId, sku, qty, region);

// Exception with typed context — no array, no boxing
try { Risky(); }
catch (Exception ex)
{
    logger.WriteError("Checkout failed for cart {CartId}", ex, cartId);
}
```

---

## 🆚 Nilog vs the alternatives

Every row is phrased so **✅ is always the good result** (✅ yes/good · ❌ no · ➖ partial).

| Question | Microsoft `ILogger` | Serilog | **Nilog** |
|----------|:---:|:---:|:---:|
| Plugs into your existing `ILogger` & DI? | ✅ | ➖ | ✅ |
| Supports `{Named}` templates + structured properties? | ✅ | ✅ | ✅ |
| **Avoids the `object[]` allocation per call (1–4 args)?** | ❌ | ❌ | ✅ |
| **Allocates nothing when the level is disabled?** | ❌ | ❌ | ✅ |
| `LoggerMessage` speed with no boilerplate? | ❌ | ❌ | ✅ |
| Built-in formatted exception report? | ➖ | ➖ | ✅ |
| Zero-allocation single-key scope object? | ❌ | ❌ | ✅ |
| Needs zero setup (just `using Nilog;`)? | ✅ | ❌ | ✅ |

---

## 🧭 Choosing the right method

| I want to… | Call | Allocates? |
|------------|------|:----------:|
| Log a constant message | `logger.WriteInformation("Started")` | **none** |
| Log 1–4 structured values | `logger.WriteInformation("User {Id}", id)` | **none** (typed) |
| Log 5+ structured values | `logger.WriteInformation("{A} {B} {C} {D} {E}", …)` | one `object[]` |
| Log an error **with** exception | `logger.WriteError("Failed {Id}", ex, id)` | **none** (typed) |
| Log an error **without** exception | `logger.WriteError("Bad request")` | **none** |
| Full exception report | `logger.WriteErrorException(ex, "Title", more: true)` | report buffer only |
| Dynamic level at runtime | `Nilogger.Log(logger, level, "…", a, b)` | **none** for 0–4 typed |
| Correlation context | `using (logger.WriteScope("Key", value)) { … }` | ~24 B (boxed value) |

> **Tip:** Keep templates to **≤ 4** named holes to stay on the zero-array typed path.

---

## ✨ Features

### Six levels, typed for 0–4 args, params for 5+

```csharp
logger.WriteTrace("Polling queue, {Count} items", count);
logger.WriteDebug("Cache miss for key {Key}", key);
logger.WriteInformation("Order {OrderId} confirmed", orderId);
logger.WriteWarning("Retry {Attempt}/{Max} for {Job}", attempt, max, job);
logger.WriteError("Payment failed for {OrderId}", ex, orderId);
logger.WriteCritical("Database unreachable on {Host}", ex, host);

// Four-arg typed — zero array, zero allocation on disabled path
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region}", userId, sku, qty, region);
```

### Runtime-level API — zero alloc for 0–4 typed args

```csharp
LogLevel level = config.Verbose ? LogLevel.Debug : LogLevel.Information;
Nilogger.Log(logger, level, "Processing {JobId}", jobId);           // 4.02 ns, 0 B
Nilogger.Log(logger, level, "{A} {B} {C} {D}", a, b, c, d);        // NEW — still 0 B when disabled
```

### Bounded template cache

```csharp
// Prevent unbounded memory growth from interpolated templates
Nilogger.MaxTemplateCacheEntries = 10_000;  // default; new entries parsed but not cached beyond limit
```

### FlushAsync — true no-op

```csharp
// Returns Task.CompletedTask synchronously — no async state machine, no allocation
await Nilogger.FlushAsync();
```

---

## 🏭 Production readiness

| Concern | Nilog answer |
|---------|-------------|
| Thread safety | `volatile`, `Interlocked`, and `ConcurrentDictionary` throughout |
| Trimming / Native AOT | No reflection — fully compatible with `PublishTrimmed` |
| Memory growth | `MaxTemplateCacheEntries` stops caching at the limit instead of growing unboundedly |
| Process shutdown | UTC-timer auto-disposes on `ProcessExit`; `ShutdownUtcTimer()` for deterministic teardown |
| Logging never throws | Bad template falls back to raw text — no `FormatException` escapes |
| Sink compatibility | `IReadOnlyList<KVP>` + `{OriginalFormat}` — works with Console, Serilog, OTel, Seq, App Insights |
| Supported frameworks | .NET 8, 9, 10 |

---

## 📖 API at a glance

```csharp
// Extension methods on ILogger — Write* for all six levels (typed 0–4, params 5+)
void WriteInformation(this ILogger logger, string message, params object[] args);
void WriteInformation<T0>(this ILogger logger, string message, T0 arg0);
void WriteInformation<T0,T1>(this ILogger logger, string message, T0 arg0, T1 arg1);
void WriteInformation<T0,T1,T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2);
void WriteInformation<T0,T1,T2,T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3);
// Identical shape for WriteTrace, WriteDebug, WriteWarning

// Error/Critical — without exception (typed, zero-array)
void WriteError<T0,T1,T2,T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3);
// With exception
void WriteError<T0,T1,T2,T3>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1, T2 arg2, T3 arg3);
// Identical shape for WriteCritical

// Exception reports
void WriteErrorException(this ILogger logger, Exception ex,
    string title = "System Error", bool moreDetailsEnabled = false);

// Scopes
IDisposable WriteScope(this ILogger logger, string key, object value);
IDisposable WriteScope(this ILogger logger, IDictionary<string, object> context);

// Static runtime-level API (zero-array for 0–4 typed args)
void Nilogger.Log<T0,T1,T2,T3>(ILogger logger, LogLevel level, string message, T0 a, T1 b, T2 c, T3 d);

// Global settings
static int MaxTemplateCacheEntries { get; set; }            // default 10,000
static Task FlushAsync(CancellationToken token = default);  // no-op: Task.CompletedTask
static void ShutdownUtcTimer();
```

---

## ❓ FAQ

**Is it really zero allocation?**
On the disabled path: yes — **0 bytes** and under 0.5 ns for **0–4** typed args (new in v1.1). On
the enabled path Nilog still allocates the rendered string but avoids the `object[]` — **26–41%
less** than the framework.

**What about 5+ arguments?**
There is no typed overload past four, so the call falls back to `params object[]` — the same as the
framework. Prefer ≤ 4 named holes on hot paths.

**Is it AOT / trimming safe?**
Yes — generics, pooling, `string.Format`; no reflection. Native AOT friendly.

---

## 📄 License

MIT © Gehan Fernando. Full docs at [github.com/gcfernando/Nilog](https://github.com/gcfernando/Nilog).
