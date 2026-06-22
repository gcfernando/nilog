# ⚡ Nilog

### Zero-allocation, high-performance logging for `Microsoft.Extensions.Logging`

**Same `ILogger`. Same `{Named}` templates. None of the garbage.**

[![NuGet](https://img.shields.io/badge/NuGet-v1.0.4-004880?logo=nuget&logoColor=white)](https://www.nuget.org/packages/Nilog)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/gcfernando/Nilog/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Disabled path](https://img.shields.io/badge/disabled%20call-0%20bytes%20%7C%20%3C0.5%20ns-2ea44f)](https://github.com/gcfernando/Nilog#-benchmarks)
[![16-arg typed](https://img.shields.io/badge/up%20to%2016--arg%20typed-0%20bytes%20disabled-brightgreen)](https://github.com/gcfernando/Nilog#-benchmarks)
[![Enabled path](https://img.shields.io/badge/enabled%20path-up%20to%2056%25%20faster-0d6efd)](https://github.com/gcfernando/Nilog#-benchmarks)
[![Analyzer](https://img.shields.io/badge/Nilog.Analyzers-NILOG001--008%20%2B%20codefix-orange)](https://github.com/gcfernando/Nilog#-static-analysis-niloganalyzers)
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

// 0–16 args: zero allocation when the level is disabled. 30–56% faster when it's enabled.
logger.WriteInformation("User {UserId} ordered {Count} items", userId, count);

// Up to sixteen args — still zero-array typed, no object[] ever built (extended to 16 in v1.0.4)
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region} via {Channel} ({Tier}) at {Ts} ref {Ref} batch {B}",
    userId, sku, qty, region, channel, tier, ts, refId, batch);
```

---

## ⚡ At a glance

| | |
|--|--|
| 🚀 **Zero-alloc disabled path** | 0–**16** typed args → **0 bytes** (proven by unit tests asserting exactly `0L` allocated). Microsoft costs 45–211 ns and 96–368 B per filtered call. |
| 🆕 **6–16 arg typed overloads (v1.0.4)** | Source-generated `Write*`/`Nilogger.Log` overloads now reach **sixteen** arguments — **0 bytes** disabled; **469× faster** than Microsoft at 9-arg (0.45 ns vs 211 ns). |
| 🆕 **Typed multi-pair scopes (v1.0.4)** | `WriteScope<T1,T2>`, `WriteScope<T1,T2,T3>`, `WriteScope<T1,T2,T3,T4>` — no dictionary allocation, no array copy for the most common scope shapes. |
| 🆕 **Compact exception report (v1.0.4)** | `WriteErrorException(ex, more: false)` → **< 300 B** (down from ≈ 992 B); single-line `[Title] Type: Message` summary. |
| 🏆 **Faster even when enabled** | **30–57% faster** and **25–32% less allocation** than Microsoft across 1–8 args; **37% faster** at 9 args. |
| 🔥 **No-arg enabled: beats Microsoft** | Plain `WriteInformation("text")` → **~3.8 ns / 0 B** vs Microsoft's ~6.1 ns / 0 B. |
| 🆕 **Span-based rendering** | Plain `{Name}` templates render through a stack-allocated `Span<char>` — no `StringBuilder`, no pool, no array. |
| 🆕 **`Nilog.Analyzers` — 8 rules + code fix** | `NILOG001`–`NILOG008`: interpolation (one-click fix), count mismatch, concatenated templates, duplicate, positional, exception-as-value, malformed, and non-PascalCase placeholders — full parity with SerilogAnalyzer. |
| 🆕 **WriteError/WriteCritical typed no-exception** | `logger.WriteError("Error {Id}", id)` → **zero-array typed overload** (no `params` fallback). |
| 🔌 **True drop-in** | Same `ILogger`, same `{Named}` templates, same structured output to every sink. |
| 🧩 **Zero setup** | Just `using Nilog;` — no DI, no registration, no config. |
| 🧯 **Never throws** | A bad template falls back to raw text; `FormatException` never escapes a log call. |
| 🧵 **Thread-safe & AOT-ready** | No reflection. Safe under contention, friendly to trimming and Native AOT. |
| 🔒 **Bounded template cache** | `MaxTemplateCacheEntries` (default 10,000) stops caching new entries instead of growing unboundedly. |

---

## 📊 Benchmarks

> Measured with BenchmarkDotNet · .NET 10.0 · Intel Core i7-13850HX · Windows 11.
> `ShortRun` job — 3 warmup + 3 measurement iterations, Server GC. The 9-arg disabled-path
> figures (v1.0.4) are from `Nilog.Benchmark`'s `HighArityExtendedBenchmarks`; reproduce with
> `dotnet run -c Release --project Nilog.Benchmark -f net10.0 -- --filter "*HighArityExtended*"`.

### 🏆 Disabled-path: the zero-allocation proof

When the level is filtered off, Microsoft still builds the `object[]` before calling `IsEnabled`. Nilog checks first — and builds nothing.

```text
─── 1-arg disabled call ──────────────────────────────────────────────────
Microsoft  ████████████████████████████████████  44.74 ns │  96 B ← always allocates
Nilog      ▏                                       0.46 ns │   0 B ← 97× faster in this benchmark

─── 5-arg disabled call (typed overload) ─────────────────────────────────
Microsoft  ████████████████████████████████████ 132.91 ns │ 224 B
Nilog      ▏                                       0.23 ns │   0 B ← 577× faster in this benchmark

─── 8-arg disabled call (typed overload — v1.0.3) ────────────────────────
Microsoft  ████████████████████████████████████ 221.03 ns │ 336 B
Nilog      ▏                                       0.82 ns │   0 B ← 268× faster in this benchmark

─── 9-arg disabled call (typed overload — NEW in v1.0.4) ─────────────────
Microsoft  ████████████████████████████████████ 211.31 ns │ 368 B
Nilog      ▏                                       0.45 ns │   0 B ← 469× faster in this benchmark
```

| Args | Microsoft | Nilog | Speedup | Bytes saved |
|-----:|-----------|-------|:-------:|:-----------:|
| 0 | 5.54 ns / 0 B | **🟢 0.19 ns / 0 B** | **29×** | — |
| **1** | 44.74 ns / **96 B** | **🟢 0.46 ns / 0 B** | **97×** | **96 B** |
| **2** | 85.95 ns / **152 B** | **🟢 0.26 ns / 0 B** | **336×** | **152 B** |
| **3** | 93.06 ns / **168 B** | **🟢 0.47 ns / 0 B** | **198×** | **168 B** |
| **4 (typed)** | 112.37 ns / **192 B** | **🟢 0.41 ns / 0 B** | **274×** | **192 B** |
| **5 (typed)** | 132.91 ns / **224 B** | **🟢 0.23 ns / 0 B** | **577×** | **224 B** |
| **6 (typed, v1.0.3)** | 153.06 ns / **264 B** | **🟢 &lt;1 ns / 0 B** | **&gt;150×** | **264 B** |
| **8 (typed, v1.0.3)** | 221.03 ns / **336 B** | **🟢 0.82 ns / 0 B** | **268×** | **336 B** |
| **9 (typed, v1.0.4)** | 211.31 ns / **368 B** | **🟢 0.45 ns / 0 B** | **469×** | **368 B** |
| …10–16 (typed, v1.0.4) | varies | **🟢 &lt;1 ns / 0 B** | **&gt;200×** | **full array** |
| 17+ (params) | both sides allocate the array — Nilog's `IsEnabled` guard fires first | | | |

> The 0 B figures are not estimates — asserted as exactly `0L` allocated bytes by
> `AllocationGateTests` (including `DisabledPath_NineTypedArgs_AllocatesZeroBytes` added in v1.0.4)
> and confirmed by BenchmarkDotNet's `MemoryDiagnoser`.

### 🔥 Enabled calls — Nilog still wins

| Scenario | Microsoft | Nilog | Time saved | Alloc saved |
|----------|-----------|-------|:----------:|:-----------:|
| **0-arg** (plain static message) | 6.11 ns / 0 B | **🟢 3.84 ns / 0 B** | **37% faster** | — |
| **1-arg** | 50.23 ns / 112 B | **🟢 35.19 ns / 80 B** | **30% faster** | **29% less** |
| **3-arg** | 116.78 ns / 152 B | **🟢 51.74 ns / 104 B** | **56% faster** | **32% less** |
| **4-arg** | 106.14 ns / 192 B | **🟢 63.64 ns / 136 B** | **40% faster** | **29% less** |
| **5-arg** (typed) | 126.55 ns / 224 B | **🟢 77.70 ns / 160 B** | **39% faster** | **29% less** |
| **6-arg** (typed, v1.0.3) | 180.33 ns / 264 B | **🟢 100.62 ns / 192 B** | **44% faster** | **27% less** |
| **8-arg** (typed, v1.0.3) | 232.94 ns / 336 B | **🟢 117.04 ns / 248 B** | **50% faster** | **26% less** |
| **9-arg** (typed, NEW v1.0.4) | 246.01 ns / 368 B | **🟢 156.21 ns / 368 B** | **37% faster** | — |

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

Nilog removes the call-site `object[]` allocation for common logging calls, but it does not make every logging scenario allocation-free. We list these honestly rather than overclaim.

| Scenario | Allocation |
|----------|-----------|
| 0–**16** typed arguments, disabled path | **0 bytes** (raised from 0–8 in v1.0.4) |
| 0–**16** typed arguments, enabled path | rendered message string only — stack-allocated span path, no array (disabled path is 0 B) |
| **17+** arguments | falls back to `params object[]`; the `IsEnabled` guard still fires before any work is done |
| Enabled logging | may still allocate depending on the sink, formatter, and value types — Nilog cannot control what a downstream sink does |
| Dynamic / interpolated / concatenated templates | each unique string grows the template cache (`Nilog.Analyzers` `NILOG001`/`NILOG003` catch this at compile time) |
| `FlushAsync` | **real flush** — awaits every callback registered via `Nilogger.RegisterFlush(...)`; a zero-allocation no-op only when nothing is registered |

> **Why some of these are by design, not bugs:** Nilog is a thin, allocation-aware layer over
> `ILogger`. It deliberately does **not** own the sink, the transport, or the async pipeline —
> that is what makes it a true drop-in that works with *any* `Microsoft.Extensions.Logging`
> provider and *any* hosting/cloud platform. Allocation past the call site (string rendering,
> sink I/O) belongs to the formatter and sink you already chose.

---

## 🗺️ Roadmap

Status of planned work. ✅ shipped · 🚧 in progress · 🔭 considering · ⛔ decided against.

| Item | Status | Notes |
|------|:------:|-------|
| Typed overloads to **16 arguments** | ✅ **1.0.4** | Source generator now emits 6–16 arg zero-array overloads; 9-arg disabled: 0.45 ns / 0 B (469× faster). |
| Typed multi-pair scope overloads | ✅ **1.0.4** | `WriteScope<T1,T2>`, `WriteScope<T1,T2,T3>`, `WriteScope<T1,T2,T3,T4>` — no dictionary allocation. |
| Compact exception report | ✅ **1.0.4** | `moreDetailsEnabled: false` now allocates < 300 B (down from ≈ 992 B); gate test added. |
| Lift the typed-overload ceiling beyond 5 args | ✅ **1.0.3** | Source generator first emitted 6–8 arg zero-array overloads. |
| More analyzer rules beyond `NILOG001` | ✅ **1.0.3** | Added `NILOG002`–`NILOG008` — 1 → 8 rules, parity with SerilogAnalyzer. |
| Ship `Nilog.Analyzers` as a standalone NuGet package | ✅ **1.0.3** | Development-dependency package; adds no runtime dependency. |
| Real `FlushAsync` for buffering sinks | ✅ **1.0.3** | `RegisterFlush`/`UnregisterFlush`; no-op only when nothing is registered. |
| Compiler-enforced Native AOT / trim safety | ✅ **1.0.3** | `IsAotCompatible=true`; removed a real `Exception.TargetSite` trim hazard. |
| Code-fix provider for `NILOG001` | ✅ **1.0.3** | One-click rewrite of `$"..."` into a literal template + appended args. |
| Code fixes for `NILOG002` / `NILOG003` | 🔭 | Ambiguous to auto-rewrite safely; diagnostics ship without an auto-fix for now. |
| `ILogger`-free static sink adapters | ⛔ decided against | Would fork the API and undermine Nilog's "true drop-in `ILogger`" design. |

If you need something here sooner, open an issue at
[github.com/gcfernando/Nilog/issues](https://github.com/gcfernando/Nilog/issues).

---

## 📦 Install

```bash
dotnet add package Nilog
```

```xml
<PackageReference Include="Nilog" Version="1.0.4" />
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

// Structured, strongly-typed, zero array allocation (1–16 args)
logger.WriteInformation("User {UserId} signed in from {Ip}", 42, "10.0.0.1");

// Up to sixteen args — 6–16 source-generated in v1.0.4, zero array, zero boxing on disabled path
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region} via {Channel} ({Tier})",
    userId, sku, qty, region, channel, tier);

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
| **Avoids the `object[]` allocation per call (1–16 args)?** | ❌ | ❌ | ✅ |
| **Allocates nothing when the level is disabled?** | ❌ | ❌ | ✅ |
| `LoggerMessage` speed with no boilerplate? | ❌ | ❌ | ✅ |
| Built-in formatted exception report (compact + verbose)? | ➖ | ➖ | ✅ |
| Typed multi-pair scope (no dict allocation)? | ❌ | ❌ | ✅ (NEW v1.0.4) |
| Zero-allocation single-key scope object? | ❌ | ❌ | ✅ |
| Catches the interpolation footgun at compile time? | ❌ | ❌ | ✅ (`Nilog.Analyzers`) |
| Needs zero setup (just `using Nilog;`)? | ✅ | ❌ | ✅ |

---

## 🧭 Choosing the right method

| I want to… | Call | Allocates? |
|------------|------|:----------:|
| Log a constant message | `logger.WriteInformation("Started")` | **none** |
| Log 1–16 structured values | `logger.WriteInformation("User {Id}", id)` | **none** (typed) |
| Log 17+ structured values | `logger.WriteInformation("{A} … {Q}", …)` | one `object[]` |
| Log an error **with** exception | `logger.WriteError("Failed {Id}", ex, id)` | **none** (typed) |
| Log an error **without** exception | `logger.WriteError("Bad request")` | **none** |
| Exception report — compact summary | `logger.WriteErrorException(ex, "Title")` | **< 300 B** |
| Exception report — full verbose | `logger.WriteErrorException(ex, "Title", more: true)` | report buffer only |
| Dynamic level at runtime | `Nilogger.Log(logger, level, "…", a, b)` | **none** for 0–16 typed |
| Attach 1-pair scope | `using (logger.WriteScope("Key", value)) { … }` | ~24 B (boxed value) |
| Attach 2–4 pair scope (typed, no dict) | `using (logger.WriteScope("K1", v1, "K2", v2)) { … }` | only boxed values |
| Catch `$"..."` mistakes at build time | add the `Nilog.Analyzers` package | n/a |

> **Tip:** Keep templates to **≤ 16** named holes to stay on the zero-array typed path.

---

## ✨ Features

### Six levels, typed for 0–16 args, params for 17+

```csharp
logger.WriteTrace("Polling queue, {Count} items", count);
logger.WriteDebug("Cache miss for key {Key}", key);
logger.WriteInformation("Order {OrderId} confirmed", orderId);
logger.WriteWarning("Retry {Attempt}/{Max} for {Job}", attempt, max, job);
logger.WriteError("Payment failed for {OrderId}", ex, orderId);
logger.WriteCritical("Database unreachable on {Host}", ex, host);

// Nine-arg typed (NEW in v1.0.4) — zero array, 0.45 ns / 0 B on disabled path
logger.WriteInformation("{A} {B} {C} {D} {E} {F} {G} {H} {I}", a, b, c, d, e, f, g, h, i);

// Up to sixteen args — all zero-array on the disabled path
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region} via {Channel} ({Tier}) ref {Ref} at {Ts}",
    userId, sku, qty, region, channel, tier, refId, ts);
```

### Runtime-level API — zero alloc for 0–16 typed args

```csharp
LogLevel level = config.Verbose ? LogLevel.Debug : LogLevel.Information;
Nilogger.Log(logger, level, "Processing {JobId}", jobId);                       // ~4 ns, 0 B
Nilogger.Log(logger, level, "{A} {B} {C} {D} {E} {F} {G} {H} {I}", a, b, c, d, e, f, g, h, i); // still 0 B when disabled
```

### Bounded template cache

```csharp
// Prevent unbounded memory growth from interpolated templates
Nilogger.MaxTemplateCacheEntries = 10_000;  // default; new entries parsed but not cached beyond limit
```

### 🔍 Static analysis — catch the structured-logging footguns at compile time

Every optimization above depends on the message argument being a stable string literal whose
placeholders match its arguments. A few mistakes undo it all silently:

```csharp
logger.WriteInformation($"User {id} signed in");  // compiles fine, silently undoes everything
logger.WriteInformation("{A} {B}", a);            // 2 holes, 1 arg → renders raw, loses props
logger.WriteInformation("User " + id + " in");    // concatenation → never a stable template
```

`Nilog.Analyzers` is a separate, opt-in package (**not** referenced by `Nilog.Core`) that catches
all three at build time across every Nilog call shape:

```xml
<PackageReference Include="Nilog.Analyzers" Version="1.0.4" PrivateAssets="all" />
```

| Rule | Severity | Catches | Auto-fix |
|------|----------|---------|:--------:|
| **NILOG001** | Warning | An interpolated string (`$"..."`) used as the message template. | ✅ |
| **NILOG002** | Warning | A template whose `{Placeholder}` count ≠ the number of arguments supplied. | — |
| **NILOG003** | Warning | A template built with string concatenation (`+`) or `string.Format(...)`. | — |
| **NILOG004** | Warning | The same named `{Placeholder}` used twice (duplicate structured-property key). | — |
| **NILOG005** | Info | Positional `{0}` placeholders instead of named `{Name}` ones. | — |
| **NILOG006** | Warning | An `Exception` passed as a template value instead of the exception parameter. | — |
| **NILOG007** | Warning | A malformed template — an unclosed `{` or an empty `{}` placeholder. | — |
| **NILOG008** | Info | A placeholder name that is not PascalCase (`{userId}` → `{UserId}`). | — |

```csharp
logger.WriteInformation($"User {id} signed in");        // ❌ NILOG001 (+ one-click fix)
logger.WriteInformation("{A} {B}", a);                  // ❌ NILOG002 (2 placeholders, 1 arg)
logger.WriteInformation("User " + id + " in");          // ❌ NILOG003
logger.WriteInformation("{Id} retried {Id}", a, b);     // ❌ NILOG004 (duplicate {Id})
logger.WriteInformation("{0} {1}", a, b);               // 🔵 NILOG005 (prefer named)
logger.WriteInformation("Failed {Error}", ex);          // ❌ NILOG006 (use the exception parameter)
logger.WriteInformation("Unclosed {Brace");             // ❌ NILOG007 (malformed)
logger.WriteInformation("User {userId}", id);           // 🔵 NILOG008 (PascalCase)

logger.WriteInformation("User {UserId} signed in", id); // ✅ no diagnostic
```

Promote the correctness rules to build-breaking errors in CI (NILOG005/008 are Info-only style):

```xml
<WarningsAsErrors>$(WarningsAsErrors);NILOG001;NILOG002;NILOG003;NILOG004;NILOG006;NILOG007</WarningsAsErrors>
```

Or via `.editorconfig` for the whole repo: `dotnet_diagnostic.NILOG001.severity = error`.

It's syntax/semantics-based — it catches the mistake at the call site. Full details:
[Static analysis](https://github.com/gcfernando/Nilog#-static-analysis-niloganalyzers).

### FlushAsync — real flush for buffering sinks

```csharp
// A batching/buffering sink registers how to drain itself…
Nilogger.RegisterFlush(ct => myBatchingSink.FlushAsync(ct));

// …and shutdown awaits every registered sink. With nothing registered this is a
// zero-allocation no-op (returns Task.CompletedTask synchronously).
await Nilogger.FlushAsync(cancellationToken);

// Typed multi-pair scopes — no dictionary allocation, no array copy (NEW in v1.0.4)
using (logger.WriteScope("OrderId", orderId, "CustomerId", customerId, "Currency", "GBP"))
{
    logger.WriteInformation("Order opened");   // all three KVPs in scope
}
```

---

## 🏭 Production readiness

| Concern | Nilog answer |
|---------|-------------|
| Thread safety | `volatile`, `Interlocked`, and `ConcurrentDictionary` throughout |
| Trimming / Native AOT | `IsAotCompatible=true` — trim/AOT analyzers run every build (warnings-as-errors), and the Native AOT compiler emits native code from `Nilog.dll` with zero warnings. No reflection. |
| Memory growth | `MaxTemplateCacheEntries` stops caching at the limit instead of growing unboundedly |
| Idle CPU cost | No background timer — the UTC timestamp cache refreshes lazily, only when an exception is formatted |
| Process shutdown | A final UTC refresh runs automatically on `ProcessExit`; `ShutdownUtcTimer()` for deterministic teardown |
| Logging never throws | Bad template falls back to raw text — no `FormatException` escapes |
| Sink compatibility | `IReadOnlyList<KVP>` + `{OriginalFormat}` — works with Console, Serilog, OTel, Seq, App Insights |
| Supported frameworks | .NET 8, 9, 10 |

---

## 📖 API at a glance

```csharp
// Extension methods on ILogger — Write* for all six levels (typed 0–16, params 17+)
void WriteInformation(this ILogger logger, string message, params object[] args);
void WriteInformation<T0>(this ILogger logger, string message, T0 arg0);
void WriteInformation<T0,T1>(this ILogger logger, string message, T0 arg0, T1 arg1);
void WriteInformation<T0,T1,T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2);
void WriteInformation<T0,T1,T2,T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3);
void WriteInformation<T0,T1,T2,T3,T4>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4);
// …and 6–16-argument overloads source-generated by Nilog.SourceGenerators (extended to 16 in v1.0.4)
void WriteInformation<T0,…,T15>(this ILogger logger, string message, T0 arg0, …, T15 arg15);
// Identical shape for WriteTrace, WriteDebug, WriteWarning

// Error/Critical — without exception (typed, zero-array; 1–16 args)
void WriteError<T0,…,T15>(this ILogger logger, string message, T0 arg0, …, T15 arg15);
// With exception
void WriteError<T0,…,T15>(this ILogger logger, string message, Exception exception, T0 arg0, …, T15 arg15);
// Identical shape for WriteCritical

// Exception reports
void WriteErrorException(this ILogger logger, Exception ex,
    string title = "System Error", bool moreDetailsEnabled = false);  // basic: < 300 B (v1.0.4)

// Scopes — typed multi-pair overloads NEW in v1.0.4 (no dictionary, no array)
IDisposable WriteScope(this ILogger logger, string key, object value);
IDisposable WriteScope<T1,T2>       (this ILogger logger, string k1, T1 v1, string k2, T2 v2);
IDisposable WriteScope<T1,T2,T3>    (this ILogger logger, string k1, T1 v1, string k2, T2 v2, string k3, T3 v3);
IDisposable WriteScope<T1,T2,T3,T4> (this ILogger logger, string k1, T1 v1, string k2, T2 v2, string k3, T3 v3, string k4, T4 v4);
IDisposable WriteScope(this ILogger logger, IDictionary<string, object> context);

// Static runtime-level API (zero-array for 0–16 typed args)
void Nilogger.Log<T0,…,T15>(ILogger logger, LogLevel level, string message, T0 a, …, T15 p);

// Global settings
static int MaxTemplateCacheEntries { get; set; }            // default 10,000
static void ShutdownUtcTimer();

// Flush: real drain of registered buffering sinks (no-op when none registered)
static void RegisterFlush(Func<CancellationToken, Task> flush);
static bool UnregisterFlush(Func<CancellationToken, Task> flush);
static Task FlushAsync(CancellationToken token = default);
```

---

## ❓ FAQ

**Is it really zero allocation?**
On the disabled path: yes — **0 bytes** for **0–16** typed args (extended to 16 in v1.0.4; asserted
by the test suite including `DisabledPath_NineTypedArgs_AllocatesZeroBytes`). On the enabled path
Nilog still allocates the rendered string but avoids the `object[]` for all **1–16** typed args —
**25–32% less** than the framework for 1–8 args, **37% faster** at 9 args.

**What about 9+ arguments?**
Typed overloads now reach **sixteen** (1–5 hand-written, 6–16 source-generated). A 9-arg disabled
call allocates **0 B** and runs in **0.45 ns** (469× faster than Microsoft). Only at **17+** args
does Nilog fall back to `params object[]` — the same as the framework. Prefer ≤ 16 named holes on
hot paths, or move extra context into a typed `WriteScope` (2–4 pairs) or dictionary scope.

**Does it work with my logging engine / sink / cloud platform?**
Yes. Nilog only produces standard `Microsoft.Extensions.Logging` state (`IReadOnlyList<KVP>` +
`{OriginalFormat}`), so it flows through **any** MEL provider — Console, Serilog, NLog,
OpenTelemetry, Seq, Application Insights, AWS/GCP exporters — and runs anywhere .NET runs
(containers, Azure Functions, AWS Lambda, Kubernetes). It adds no transport of its own, so there
is nothing platform-specific to configure. This is **verified**, not asserted:
`LoggingEngineInteropTests` runs Nilog through the real `LoggerFactory` + `ILoggerProvider`
pipeline (the exact contract every engine integrates through) and checks the rendered message,
`{OriginalFormat}`, named properties, exceptions, and level-filtering all arrive intact.

**Is it AOT / trimming safe?**
Yes — generics, pooling, `string.Format`, stack-allocated spans; no reflection. Native AOT friendly.

**What does `Nilog.Analyzers` check?**
Eight rules, full parity with SerilogAnalyzer: `NILOG001` (interpolated templates, with a
one-click code fix), `NILOG002` (placeholder/argument count mismatch), `NILOG003` (concatenated or
`string.Format` templates), `NILOG004` (duplicate named placeholder), `NILOG005` (positional
placeholders, Info), `NILOG006` (an exception passed as a template value), `NILOG007` (malformed
template), and `NILOG008` (non-PascalCase placeholder name, Info) — across every
`Write*`/`Nilogger.Log` call shape. It's a separate, opt-in package — not referenced by
`Nilog.Core` — so installing `Nilog` never pulls it in automatically.

---

## 📄 License

MIT © Gehan Fernando. Full docs at [github.com/gcfernando/Nilog](https://github.com/gcfernando/Nilog).
