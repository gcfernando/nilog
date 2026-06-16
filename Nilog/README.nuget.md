# ⚡ Nilog

### Zero-allocation, high-performance logging for `Microsoft.Extensions.Logging`

**Same `ILogger`. Same `{Named}` templates. None of the garbage.**

[![NuGet](https://img.shields.io/badge/NuGet-v1.0.2-004880?logo=nuget&logoColor=white)](https://www.nuget.org/packages/Nilog)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/gcfernando/Nilog/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Disabled path](https://img.shields.io/badge/disabled%20call-0%20bytes%20%7C%20%3C0.5%20ns-2ea44f)](https://github.com/gcfernando/Nilog#-benchmarks)
[![5-arg typed](https://img.shields.io/badge/5--arg%20typed-0%20bytes%20%7C%20577%C3%97%20faster-brightgreen)](https://github.com/gcfernando/Nilog#-benchmarks)
[![Enabled path](https://img.shields.io/badge/enabled%20path-up%20to%2056%25%20faster-0d6efd)](https://github.com/gcfernando/Nilog#-benchmarks)
[![Analyzer](https://img.shields.io/badge/Nilog.Analyzers-NILOG001-orange)](https://github.com/gcfernando/Nilog#-static-analysis-niloganalyzers)
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

// 0–5 args: zero allocation when the level is disabled. 30–56% faster when it's enabled.
logger.WriteInformation("User {UserId} ordered {Count} items", userId, count);

// Five args — still zero-array typed, no object[] ever built (extended in v1.0.2)
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region} via {Channel}",
    userId, sku, qty, region, channel);
```

---

## ⚡ At a glance

| | |
|--|--|
| 🚀 **Zero-alloc disabled path** | 0–**5** typed args → **0 bytes, &lt;0.5 ns**. Microsoft costs 45–133 ns and 96–224 B per filtered call. |
| 🏆 **Faster even when enabled** | **30–56% faster** and **25–32% less allocation** than Microsoft across 1–5 typed args. |
| 🔥 **No-arg enabled: beats Microsoft** | Plain `WriteInformation("text")` → **~3.8 ns / 0 B** vs Microsoft's ~6.1 ns / 0 B. |
| 🆕 **5-arg typed overloads (v1.0.2)** | Five arguments now use a strongly-typed `LogState<T0..T4>` struct — **577× faster** on the disabled path, **0 bytes**. |
| 🆕 **Span-based rendering (v1.0.2)** | Plain `{Name}` templates render through a stack-allocated `Span<char>` — no `StringBuilder`, no pool, no array. |
| 🆕 **`Nilog.Analyzers` (v1.0.2)** | Opt-in Roslyn analyzer — `NILOG001` flags `WriteInformation($"...")` interpolation at compile time. |
| 🆕 **WriteError/WriteCritical typed no-exception** | `logger.WriteError("Error {Id}", id)` → **zero-array typed overload** (no `params` fallback). |
| 🔌 **True drop-in** | Same `ILogger`, same `{Named}` templates, same structured output to every sink. |
| 🧩 **Zero setup** | Just `using Nilog;` — no DI, no registration, no config. |
| 🧯 **Never throws** | A bad template falls back to raw text; `FormatException` never escapes a log call. |
| 🧵 **Thread-safe & AOT-ready** | No reflection. Safe under contention, friendly to trimming and Native AOT. |
| 🔒 **Bounded template cache** | `MaxTemplateCacheEntries` (default 10,000) stops caching new entries instead of growing unboundedly. |

---

## 📊 Benchmarks

> Measured with BenchmarkDotNet v0.15.8 · .NET 10.0.8 · Intel Core i7-13850HX · Windows 11 25H2.
> `ShortRun` job — 3 warmup + 3 measurement iterations, Server GC. One coherent run, 86 benchmarks.

### 🏆 Disabled-path: the zero-allocation proof

When the level is filtered off, Microsoft still builds the `object[]` before calling `IsEnabled`. Nilog checks first — and builds nothing.

```text
─── 1-arg disabled call ──────────────────────────────────────────────────
Microsoft  ████████████████████████████████████  44.74 ns │  96 B ← always allocates
Nilog      ▏                                       0.46 ns │   0 B ← 97× faster in this benchmark

─── 3-arg disabled call ──────────────────────────────────────────────────
Microsoft  ████████████████████████████████████  93.06 ns │ 168 B
Nilog      ▏                                       0.47 ns │   0 B ← 198× faster in this benchmark

─── 5-arg disabled call (typed overload — extended in v1.0.2) ────────────
Microsoft  ████████████████████████████████████ 132.91 ns │ 224 B
Nilog      ▏                                       0.23 ns │   0 B ← 577× faster in this benchmark
```

| Args | Microsoft | Nilog | Speedup | Bytes saved |
|-----:|-----------|-------|:-------:|:-----------:|
| 0 | 5.54 ns / 0 B | **🟢 0.19 ns / 0 B** | **29×** | — |
| **1** | 44.74 ns / **96 B** | **🟢 0.46 ns / 0 B** | **97×** | **96 B** |
| **2** | 85.95 ns / **152 B** | **🟢 0.26 ns / 0 B** | **336×** | **152 B** |
| **3** | 93.06 ns / **168 B** | **🟢 0.47 ns / 0 B** | **198×** | **168 B** |
| **4 (typed)** | 112.37 ns / **192 B** | **🟢 0.41 ns / 0 B** | **274×** | **192 B** |
| **5 (typed)** | 132.91 ns / **224 B** | **🟢 0.23 ns / 0 B** | **577×** | **224 B** |
| 6 (params) | 153.06 ns / 264 B | 38.68 ns / 216 B | **4.0×** | 48 B |

### 🔥 Enabled calls — Nilog still wins

| Scenario | Microsoft | Nilog | Time saved | Alloc saved |
|----------|-----------|-------|:----------:|:-----------:|
| **0-arg** (plain static message) | 6.11 ns / 0 B | **🟢 3.84 ns / 0 B** | **37% faster** | — |
| **1-arg** | 50.23 ns / 112 B | **🟢 35.19 ns / 80 B** | **30% faster** | **29% less** |
| **3-arg** | 116.78 ns / 152 B | **🟢 51.74 ns / 104 B** | **56% faster** | **32% less** |
| **4-arg** | 106.14 ns / 192 B | **🟢 63.64 ns / 136 B** | **40% faster** | **29% less** |
| **5-arg** (typed, extended in v1.0.2) | 133.96 ns / 224 B | **🟢 79.11 ns / 160 B** | **41% faster** | **29% less** |

### 💥 Stress test — 10,000-call loop, every typed arity

| Scenario | Time | Allocation |
|----------|-----:|----------------:|
| 🔴 Microsoft disabled 3/4/5-arg × 10,000 | 733–1,477 μs | 1.56–2.75 MB |
| 🟢 **Nilog disabled 3/4/5-arg × 10,000** | **~2.9 μs flat** | **0 B** |
| Microsoft enabled 3/4/5-arg × 10,000 | 913–1,377 μs | 1.88–2.75 MB |
| 🟢 **Nilog enabled 3/4/5-arg × 10,000** | **582–896 μs** | 1.40–2.11 MB |

The disabled path stays flat at **~2.9 μs with 0 B**, no matter whether the template carries 3, 4,
or 5 arguments. The enabled loop is consistently **~34–36% faster, ~24–26% less allocation** than
Microsoft across the same range.

### ⚡ Special paths

| Scenario | Mean | Alloc |
|----------|-----:|------:|
| `FlushAsync()` | **~0.2 ns** | **0 B** |
| `WriteError("msg", ex)` — no args | **4.1 ns** | **0 B** |
| `WriteError("Error {Id}", id)` — typed, no exception | **31.1 ns** | **72 B** |
| `Nilogger.Log(…)` 0-arg enabled | **4.0 ns** | **0 B** |
| Sequential 100,000-call loop (same template) | **4.83 ms** | 11.41 MB (**35% faster, 25% less RAM** than Microsoft's 7.38 ms / 15.22 MB) |

---

## ⚠️ Limitations

Nilog removes the call-site `object[]` allocation for common logging calls, but it does not make every logging scenario allocation-free.

| Scenario | Allocation |
|----------|-----------|
| 0–5 typed arguments, disabled path | **0 bytes** |
| 0–5 typed arguments, enabled path | rendered message string only (no array) |
| 6+ arguments | falls back to `params object[]` |
| Enabled logging | may still allocate depending on the sink, formatter, and value types |
| Dynamic/interpolated templates | each unique string grows the template cache (add `Nilog.Analyzers` to catch this at compile time) |
| `FlushAsync` | **no-op** — returns `Task.CompletedTask` immediately; no real async sink flush |

---

## 📦 Install

```bash
dotnet add package Nilog
```

```xml
<PackageReference Include="Nilog" Version="1.0.2" />
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

// Plain message — ~3.8 ns, 0 bytes
logger.WriteInformation("Service started");

// Structured, strongly-typed, zero array allocation (1–5 args)
logger.WriteInformation("User {UserId} signed in from {Ip}", 42, "10.0.0.1");

// Five args — extended in v1.0.2, zero array, zero boxing on disabled path
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region} via {Channel}",
    userId, sku, qty, region, channel);

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
| **Avoids the `object[]` allocation per call (1–5 args)?** | ❌ | ❌ | ✅ |
| **Allocates nothing when the level is disabled?** | ❌ | ❌ | ✅ |
| `LoggerMessage` speed with no boilerplate? | ❌ | ❌ | ✅ |
| Built-in formatted exception report? | ➖ | ➖ | ✅ |
| Zero-allocation single-key scope object? | ❌ | ❌ | ✅ |
| Catches the interpolation footgun at compile time? | ❌ | ❌ | ✅ (`Nilog.Analyzers`) |
| Needs zero setup (just `using Nilog;`)? | ✅ | ❌ | ✅ |

---

## 🧭 Choosing the right method

| I want to… | Call | Allocates? |
|------------|------|:----------:|
| Log a constant message | `logger.WriteInformation("Started")` | **none** |
| Log 1–5 structured values | `logger.WriteInformation("User {Id}", id)` | **none** (typed) |
| Log 6+ structured values | `logger.WriteInformation("{A} {B} {C} {D} {E} {F}", …)` | one `object[]` |
| Log an error **with** exception | `logger.WriteError("Failed {Id}", ex, id)` | **none** (typed) |
| Log an error **without** exception | `logger.WriteError("Bad request")` | **none** |
| Full exception report | `logger.WriteErrorException(ex, "Title", more: true)` | report buffer only |
| Dynamic level at runtime | `Nilogger.Log(logger, level, "…", a, b)` | **none** for 0–5 typed |
| Correlation context | `using (logger.WriteScope("Key", value)) { … }` | ~24 B (boxed value) |
| Catch `$"..."` mistakes at build time | add the `Nilog.Analyzers` package | n/a |

> **Tip:** Keep templates to **≤ 5** named holes to stay on the zero-array typed path.

---

## ✨ Features

### Six levels, typed for 0–5 args, params for 6+

```csharp
logger.WriteTrace("Polling queue, {Count} items", count);
logger.WriteDebug("Cache miss for key {Key}", key);
logger.WriteInformation("Order {OrderId} confirmed", orderId);
logger.WriteWarning("Retry {Attempt}/{Max} for {Job}", attempt, max, job);
logger.WriteError("Payment failed for {OrderId}", ex, orderId);
logger.WriteCritical("Database unreachable on {Host}", ex, host);

// Five-arg typed (v1.0.2) — zero array, zero allocation on disabled path
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region} via {Channel}",
    userId, sku, qty, region, channel);
```

### Runtime-level API — zero alloc for 0–5 typed args

```csharp
LogLevel level = config.Verbose ? LogLevel.Debug : LogLevel.Information;
Nilogger.Log(logger, level, "Processing {JobId}", jobId);              // ~4 ns, 0 B
Nilogger.Log(logger, level, "{A} {B} {C} {D} {E}", a, b, c, d, e);    // v1.0.2 — still 0 B when disabled
```

### Bounded template cache

```csharp
// Prevent unbounded memory growth from interpolated templates
Nilogger.MaxTemplateCacheEntries = 10_000;  // default; new entries parsed but not cached beyond limit
```

### 🔍 Static analysis — catch the interpolation footgun at compile time

Every optimization above depends on the message argument being a stable string literal. One
mistake undoes it all silently:

```csharp
logger.WriteInformation($"User {id} signed in"); // compiles fine, silently undoes everything
```

`Nilog.Analyzers` is a separate, opt-in package (**not** referenced by `Nilog.Core`) that catches
this at build time across every Nilog call shape:

```xml
<PackageReference Include="Nilog.Analyzers" Version="1.0.2" PrivateAssets="all" />
```

```csharp
logger.WriteInformation($"User {id} signed in");        // ❌ NILOG001
logger.WriteError($"Order {id} failed", ex);             // ❌ NILOG001
Nilogger.Log(logger, LogLevel.Warning, $"Retry {n}");    // ❌ NILOG001

logger.WriteInformation("User {UserId} signed in", id); // ✅ no diagnostic
```

Promote it to a build-breaking error in CI without touching any other warning:

```xml
<WarningsAsErrors>$(WarningsAsErrors);NILOG001</WarningsAsErrors>
```

Or via `.editorconfig` for the whole repo: `dotnet_diagnostic.NILOG001.severity = error`.

It's syntax-based — it catches the mistake at the call site, not interpolation hidden behind a
local variable. Full details: [Static analysis](https://github.com/gcfernando/Nilog#-static-analysis-niloganalyzers).

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
| Idle CPU cost | No background timer — the UTC timestamp cache refreshes lazily, only when an exception is formatted |
| Process shutdown | A final UTC refresh runs automatically on `ProcessExit`; `ShutdownUtcTimer()` for deterministic teardown |
| Logging never throws | Bad template falls back to raw text — no `FormatException` escapes |
| Sink compatibility | `IReadOnlyList<KVP>` + `{OriginalFormat}` — works with Console, Serilog, OTel, Seq, App Insights |
| Supported frameworks | .NET 8, 9, 10 |

---

## 📖 API at a glance

```csharp
// Extension methods on ILogger — Write* for all six levels (typed 0–5, params 6+)
void WriteInformation(this ILogger logger, string message, params object[] args);
void WriteInformation<T0>(this ILogger logger, string message, T0 arg0);
void WriteInformation<T0,T1>(this ILogger logger, string message, T0 arg0, T1 arg1);
void WriteInformation<T0,T1,T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2);
void WriteInformation<T0,T1,T2,T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3);
void WriteInformation<T0,T1,T2,T3,T4>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4);
// Identical shape for WriteTrace, WriteDebug, WriteWarning

// Error/Critical — without exception (typed, zero-array)
void WriteError<T0,T1,T2,T3,T4>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4);
// With exception
void WriteError<T0,T1,T2,T3,T4>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4);
// Identical shape for WriteCritical

// Exception reports
void WriteErrorException(this ILogger logger, Exception ex,
    string title = "System Error", bool moreDetailsEnabled = false);

// Scopes
IDisposable WriteScope(this ILogger logger, string key, object value);
IDisposable WriteScope(this ILogger logger, IDictionary<string, object> context);

// Static runtime-level API (zero-array for 0–5 typed args)
void Nilogger.Log<T0,T1,T2,T3,T4>(ILogger logger, LogLevel level, string message, T0 a, T1 b, T2 c, T3 d, T4 e);

// Global settings
static int MaxTemplateCacheEntries { get; set; }            // default 10,000
static Task FlushAsync(CancellationToken token = default);  // no-op: Task.CompletedTask
static void ShutdownUtcTimer();
```

---

## ❓ FAQ

**Is it really zero allocation?**
On the disabled path: yes — **0 bytes** and under 0.5 ns for **0–5** typed args (extended in
v1.0.2). On the enabled path Nilog still allocates the rendered string but avoids the
`object[]` — **25–32% less** than the framework.

**What about 6+ arguments?**
There is no typed overload past five, so the call falls back to `params object[]` — the same as
the framework. Prefer ≤ 5 named holes on hot paths.

**Is it AOT / trimming safe?**
Yes — generics, pooling, `string.Format`, stack-allocated spans; no reflection. Native AOT friendly.

**What does `Nilog.Analyzers` check?**
One rule, `NILOG001`: flags an interpolated string (`$"..."`) passed as the message template,
across every `Write*`/`Nilogger.Log` call shape. It's a separate, opt-in package — not
referenced by `Nilog.Core` — so installing `Nilog` never pulls it in automatically.

---

## 📄 License

MIT © Gehan Fernando. Full docs at [github.com/gcfernando/Nilog](https://github.com/gcfernando/Nilog).
