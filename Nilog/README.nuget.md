# ⚡ Nilog

### Allocation-aware, high-performance logging for `Microsoft.Extensions.Logging`

**Same `ILogger`. Same `{Named}` templates. None of the garbage.**

[![NuGet](https://img.shields.io/badge/NuGet-v1.0.6-004880?logo=nuget&logoColor=white)](https://www.nuget.org/packages/Nilog)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/gcfernando/Nilog/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![Disabled path](https://img.shields.io/badge/disabled%20call-0%20bytes%20%7C%20~2%20ns-2ea44f)](https://github.com/gcfernando/Nilog#-benchmarks)
[![16-arg typed](https://img.shields.io/badge/up%20to%2016--arg%20typed-0%20bytes%20disabled-brightgreen)](https://github.com/gcfernando/Nilog#-benchmarks)
[![Enabled path](https://img.shields.io/badge/enabled%20path-lower%20alloc%20than%20MEL%20extensions-0d6efd)](https://github.com/gcfernando/Nilog#-benchmarks)
[![Analyzer](https://img.shields.io/badge/Nilog.Analyzers-NILOG001--008%20%2B%20codefix-orange)](https://github.com/gcfernando/Nilog#-static-analysis-niloganalyzers)
[![AOT](https://img.shields.io/badge/Native%20AOT-ready-blueviolet)](https://github.com/gcfernando/Nilog)

> 📖 **Full docs, recipes, and architecture:** [github.com/gcfernando/Nilog](https://github.com/gcfernando/Nilog)

---

## ⚡ What Nilog is

Nilog is a set of **zero-allocation, strongly-typed extension methods on `ILogger`**. It is not a
new logging framework, provider, or transport — it sits on top of the `Microsoft.Extensions.Logging`
(MEL) pipeline you already use, and every call still flows through your existing `ILoggerFactory`
and provider(s) (Console, Serilog, NLog, OpenTelemetry, Seq, Application Insights, …). Swap
`LogInformation(...)` for `WriteInformation(...)` and the call site stops allocating a
`params object[]` on every call — nothing else about your logging setup changes.

The stock `ILogger` extensions allocate a **`params object[]` on every single call** — even when
the level is switched off and the message is thrown straight in the bin. On a hot path that is
millions of pointless allocations and a busy garbage collector.

**Nilog swaps that array for a stack-only struct.** A disabled call allocates **nothing** and
returns in about **2 ns** (measured, v1.0.6). `LoggerMessage.Define`/`[LoggerMessage]` achieve the same disabled-path cost; Nilog needs no per-message declaration.

```csharp
using Nilog;

// 0–16 args: zero allocation when the level is disabled. Lower allocation than the conventional extensions when enabled.
logger.WriteInformation("User {UserId} ordered {Count} items", userId, count);

// Up to sixteen args — still zero-array typed, no object[] ever built (extended to 16 in v1.0.4)
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region} via {Channel} ({Tier}) at {Ts} ref {Ref} batch {B}",
    userId, sku, qty, region, channel, tier, ts, refId, batch);
```

---

## ⚡ At a glance

| | |
|--|--|
| 🚀 **Zero-alloc disabled path** | 0–**16** typed args → **0 bytes** (proven by unit tests asserting exactly `0L` allocated). Conventional Microsoft extensions cost 17–43 ns and 56–272 B per filtered call (v1.0.6 measurement). |
| 🆕 **6–16 arg typed overloads (v1.0.4)** | Source-generated `Write*`/`Nilogger.Log` overloads now reach **sixteen** arguments — **0 bytes** disabled; ≈2.8 ns vs 42.6 ns for the conventional extension at 9 args (v1.0.6 measurement). |
| 🆕 **Typed multi-pair scopes (v1.0.4)** | `WriteScope<T1,T2>`, `WriteScope<T1,T2,T3>`, `WriteScope<T1,T2,T3,T4>` — no dictionary allocation, no array copy for the most common scope shapes. |
| 🆕 **Compact exception report (v1.0.4)** | `WriteErrorException(ex, more: false)` → **< 300 B** (down from ≈ 992 B); single-line `[Title] Type: Message` summary. |
| 🔥 **Leaner than MEL extensions when enabled** | Less allocation than the conventional extensions at 1–6 args; equal at 9; not faster than `LoggerMessage.Define`/`[LoggerMessage]`. |
| 🆕 **Span-based rendering** | Plain `{Name}` templates render through a stack-allocated `Span<char>` — no `StringBuilder`, no pool, no array. |
| 🆕 **`Nilog.Analyzers` — 8 rules + code fix** | `NILOG001`–`NILOG008`: interpolation (one-click fix), count mismatch, concatenated templates, duplicate, positional, exception-as-value, malformed, and non-PascalCase placeholders. |
| 🆕 **WriteError/WriteCritical typed no-exception** | `logger.WriteError("Error {Id}", id)` → **zero-array typed overload** (no `params` fallback). |
| 🔌 **True drop-in** | Same `ILogger`, same `{Named}` templates, same structured output to every sink. |
| 🧩 **Zero setup** | Just `using Nilog;` — no DI, no registration, no config. |
| 🧯 **Hardened formatting** | Template mismatches fall back to raw text; a throwing `ToString()` on a typed argument is rendered as `[ToString failed: ...]`. Validation and provider exceptions still propagate. |
| 🧵 **Thread-safe & AOT-ready** | No reflection. Safe under contention, friendly to trimming and Native AOT. |
| 🔒 **Bounded template cache** | `MaxTemplateCacheEntries` (default 10,000) stops caching new entries instead of growing unboundedly. |

---

## 📊 Benchmarks

> **Honest summary.** Nilog's clear advantage is the **disabled path** (0 B, ~2–3 ns versus 17–43 ns / 56–272 B for
> conventional Microsoft extension methods) and **lower enabled-path allocation than conventional extensions at 1–6 typed
> args**. Compared with Microsoft's optimized `LoggerMessage.Define` / `[LoggerMessage]`, Nilog is **equal on the
> disabled path and not faster when enabled** (and allocates 3.6× more than the source generator at 9 args).
> Nilog's value is convenience — no per-message declaration — at near-optimized cost.

Measured for **v1.0.6** with BenchmarkDotNet 0.15.8 · .NET 10.0.12 · Intel Core Ultra 7 265H · Windows 11 · SDK 10.0.401,
3 launches × (5 warmup + 12 iterations), from an external harness that references the packed `Nilog 1.0.6` package.
Single-digit-nanosecond differences (< ~1 ns) are timer/loop-overhead artifacts, not real differences.

### Disabled path (level filtered off)

| Typed args | MEL extension   | `LoggerMessage.Define` | `[LoggerMessage]` | **Nilog**        |
| :---: | :---: | :---: | :---: | :---: |
| 1          | 17.2 ns / 56 B  | 0.6 ns / 0 B           | 0.1 ns / 0 B      | **2.0 ns / 0 B** |
| 3          | 22.7 ns / 104 B | 1.5 ns / 0 B           | 0.9 ns / 0 B      | **1.9 ns / 0 B** |
| 6          | 38.3 ns / 200 B | 2.1 ns / 0 B           | 1.7 ns / 0 B      | **2.0 ns / 0 B** |
| 9          | 42.6 ns / 272 B | n/a                    | 2.6 ns / 0 B      | **2.8 ns / 0 B** |

### Enabled path (message rendered)

| Typed args | MEL extension    | `LoggerMessage.Define` | `[LoggerMessage]`    | **Nilog**            |
| :---: | :---: | :---: | :---: | :---: |
| 1          | 53.0 ns / 88 B   | **22.6 ns / 32 B**     | **22.7 ns / 32 B**   | 29.4 ns / 56 B       |
| 3          | 106.1 ns / 152 B | **72.0 ns / 48 B**     | 77.3 ns / 48 B       | 81.1 ns / 104 B      |
| 6          | 240.6 ns / 280 B | 220.8 ns / 280 B       | 231.1 ns / 280 B     | **156.8 ns / 208 B** |
| 9          | 380.7 ns / 376 B | n/a                    | **211.2 ns / 104 B** | 360.0 ns / 376 B     |

* Arguments statically typed as `object` bind to the `params` overload — no improvement over Microsoft.
* A single scope: `BeginScope` 21.9 ns / 240 B vs `WriteScope("k", v)` 11.5 ns / 80 B (scope *creation* only).
* Parallel logging (50,000 calls, `MediumRun`): Microsoft 3.46 ms / 5.34 MB vs Nilog 3.54 ms / 3.82 MB — equal time, ~28% less allocation. The enabled path always allocates the rendered string; only the **disabled** path is allocation-free.
* Real ASP.NET Core app (Kestrel, loopback): 1,696 B/request vs 1,833 B (−7.5%); throughput and latency not distinguishable from Microsoft in this run.

Do not extrapolate micro-benchmark ratios to application performance. Full methodology and limits:
[main README](https://github.com/gcfernando/Nilog#-benchmarks).

---


## ⚠️ Limitations

Nilog removes the call-site `object[]` allocation for common logging calls, but it does not make every logging scenario allocation-free. We list these honestly rather than overclaim.

| Scenario                                        | Allocation                                                                                                                                   |
| ---------- | ----------- |
| 0–**16** typed arguments, disabled path         | **0 bytes** (raised from 0–8 in v1.0.4)                                                                                                      |
| 0–**16** typed arguments, enabled path          | rendered message string only — stack-allocated span path, no array (disabled path is 0 B)                                                    |
| **17+** arguments                               | falls back to `params object[]`; the `IsEnabled` guard still fires before any work is done                                                   |
| Enabled logging                                 | may still allocate depending on the sink, formatter, and value types — Nilog cannot control what a downstream sink does                      |
| Dynamic / interpolated / concatenated templates | each unique string grows the template cache (`Nilog.Analyzers` `NILOG001`/`NILOG003` catch this at compile time)                             |
| `FlushAsync`                                    | **real flush** — awaits every callback registered via `Nilogger.RegisterFlush(...)`; a zero-allocation no-op only when nothing is registered |

> **Why some of these are by design, not bugs:** Nilog is a thin, allocation-aware layer over
> `ILogger`. It deliberately does **not** own the sink, the transport, or the async pipeline —
> that is what makes it a true drop-in that works with *any* `Microsoft.Extensions.Logging`
> provider and *any* hosting/cloud platform. Allocation past the call site (string rendering,
> sink I/O) belongs to the formatter and sink you already chose.

---

## 🗺️ Roadmap

Status of planned work. ✅ shipped · 🚧 in progress · 🔭 considering · ⛔ decided against.

| Item                                                 | Status            | Notes                                                                                                  |
| ------ | :------: | ------- |
| Typed overloads to **16 arguments**                  | ✅ **1.0.4**       | Source generator now emits 6–16 arg zero-array overloads; 9-arg disabled: 0.45 ns / 0 B (469× faster). |
| Typed multi-pair scope overloads                     | ✅ **1.0.4**       | `WriteScope<T1,T2>`, `WriteScope<T1,T2,T3>`, `WriteScope<T1,T2,T3,T4>` — no dictionary allocation.     |
| Compact exception report                             | ✅ **1.0.4**       | `moreDetailsEnabled: false` now allocates < 300 B (down from ≈ 992 B); gate test added.                |
| Lift the typed-overload ceiling beyond 5 args        | ✅ **1.0.3**       | Source generator first emitted 6–8 arg zero-array overloads.                                           |
| More analyzer rules beyond `NILOG001`                | ✅ **1.0.3**       | Added `NILOG002`–`NILOG008` — 1 → 8 rules.                                                             |
| Ship `Nilog.Analyzers` as a standalone NuGet package | ✅ **1.0.3**       | Development-dependency package; adds no runtime dependency.                                            |
| Real `FlushAsync` for buffering sinks                | ✅ **1.0.3**       | `RegisterFlush`/`UnregisterFlush`; no-op only when nothing is registered.                              |
| Compiler-enforced Native AOT / trim safety           | ✅ **1.0.3**       | `IsAotCompatible=true`; removed a real `Exception.TargetSite` trim hazard.                             |
| Code-fix provider for `NILOG001`                     | ✅ **1.0.3**       | One-click rewrite of `$"..."` into a literal template + appended args.                                 |
| Code fixes for `NILOG002` / `NILOG003`               | 🔭                 | Ambiguous to auto-rewrite safely; diagnostics ship without an auto-fix for now.                        |
| `ILogger`-free static sink adapters                  | ⛔ decided against | Would fork the API and undermine Nilog's "true drop-in `ILogger`" design.                              |

If you need something here sooner, open an issue at
[github.com/gcfernando/Nilog/issues](https://github.com/gcfernando/Nilog/issues).

---

## 📦 Install

```bash
dotnet add package Nilog
```

```xml
<PackageReference Include="Nilog" Version="1.0.6" />
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

// Plain message — ~4.1 ns, 0 bytes
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

> [!IMPORTANT]
> **No Nilog-specific configuration or DI registration is required for normal use.** There is
> no service to register, no logger factory to construct, and no config file to maintain — just
> `using Nilog;` and the existing `ILogger` you already have.

---

## ⚙️ Configuration

Nilog has **nothing to configure for normal use**. Log levels, categories, and providers remain
**standard `Microsoft.Extensions.Logging` configuration** and work exactly as they do today:

```csharp
builder.Logging
    .SetMinimumLevel(LogLevel.Information)
    .AddFilter("Microsoft", LogLevel.Warning)
    .AddConsole();
```

A few **optional**, process-wide static hooks exist on `Nilogger` for advanced scenarios —
`ExceptionFormatter` (custom exception rendering), `MaxTemplateCacheEntries` (template-cache
ceiling, default 10,000), and `RegisterFlush`/`UnregisterFlush`/`FlushAsync` (draining a custom
buffering sink). None of them are required; see the
[full configuration reference](https://github.com/gcfernando/Nilog#-optional-advanced-configuration)
for details.

---

## 🧩 Structured logging

A single Nilog call carries the same structured contract as the framework's own templated
logging — no array, no extra allocation:

```csharp
logger.WriteInformation("User {UserId} bought {Sku} x{Qty}", 42, "A-100", 3);
```

| What the sink receives   | Value                                                          |
| ------------------------ | ------- |
| Rendered message         | `User 42 bought A-100 x3`                                      |
| `UserId` / `Sku` / `Qty` | `42` / `"A-100"` / `3`                                         |
| `{OriginalFormat}`       | `User {UserId} bought {Sku} x{Qty}`                            |
| Exception (if any)       | attached via the `Exception`-overload, not as a template value |
| Scope                    | whatever `WriteScope(...)` wraps the call in                   |

Any structured-aware sink — Serilog, Seq, OpenTelemetry, Application Insights — gets clean,
queryable fields exactly as if you had used the framework's own templated logging.

---

## 🔗 Provider compatibility

Nilog produces standard `Microsoft.Extensions.Logging` state (`IReadOnlyList<KeyValuePair<string,
object>>` + `{OriginalFormat}`) — it does not implement a provider or sink itself, so it works with
**any** MEL provider: Console, Debug, Serilog, NLog, OpenTelemetry, Seq, Application Insights, and
cloud-specific exporters (AWS/GCP). This is contract-compatibility through the real
`LoggerFactory`/`ILoggerProvider` pipeline (verified by `LoggingEngineInteropTests` and real
Serilog/OpenTelemetry integration tests) — Nilog does not ship direct integrations with any
specific database, log store, or cloud service (for example MongoDB, Elasticsearch, SQL, or
Oracle); those come from whichever MEL provider or sink you choose.

---

## 🆚 Nilog vs the alternatives

Every row is phrased so **✅ is always the good result** (✅ yes/good · ❌ no · ➖ partial).

| Question                                                   | Microsoft `ILogger` | Serilog | **Nilog**             |
| ---------- | :---: | :---: | :---: |
| Plugs into your existing `ILogger` & DI?                   | ✅                   | ➖       | ✅                     |
| Supports `{Named}` templates + structured properties?      | ✅                   | ✅       | ✅                     |
| **Avoids the `object[]` allocation per call (1–16 args)?** | ❌                   | ❌       | ✅                     |
| **Allocates nothing when the level is disabled?**          | ❌                   | ❌       | ✅                     |
| `LoggerMessage` speed with no boilerplate?                 | ❌                   | ❌       | ✅                     |
| Built-in formatted exception report (compact + verbose)?   | ➖                   | ➖       | ✅                     |
| Typed multi-pair scope (no dict allocation)?               | ❌                   | ❌       | ✅ (NEW v1.0.4)        |
| Zero-allocation single-key scope object?                   | ❌                   | ❌       | ✅                     |
| Catches the interpolation footgun at compile time?         | ❌                   | ❌       | ✅ (`Nilog.Analyzers`) |
| Needs zero setup (just `using Nilog;`)?                    | ✅                   | ❌       | ✅                     |

---

## 🧭 Choosing the right method

| I want to…                             | Call                                                  | Allocates?              |
| ------------ | ------ | :----------: |
| Log a constant message                 | `logger.WriteInformation("Started")`                  | **none**                |
| Log 1–16 structured values             | `logger.WriteInformation("User {Id}", id)`            | **none** (typed)        |
| Log 17+ structured values              | `logger.WriteInformation("{A} … {Q}", …)`             | one `object[]`          |
| Log an error **with** exception        | `logger.WriteError("Failed {Id}", ex, id)`            | **none** (typed)        |
| Log an error **without** exception     | `logger.WriteError("Bad request")`                    | **none**                |
| Exception report — compact summary     | `logger.WriteErrorException(ex, "Title")`             | **< 300 B**             |
| Exception report — full verbose        | `logger.WriteErrorException(ex, "Title", more: true)` | report buffer only      |
| Dynamic level at runtime               | `Nilogger.Log(logger, level, "…", a, b)`              | **none** for 0–16 typed |
| Attach 1-pair scope                    | `using (logger.WriteScope("Key", value)) { … }`       | ~24 B (boxed value)     |
| Attach 2–4 pair scope (typed, no dict) | `using (logger.WriteScope("K1", v1, "K2", v2)) { … }` | only boxed values       |
| Catch `$"..."` mistakes at build time  | add the `Nilog.Analyzers` package                     | n/a                     |

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
<PackageReference Include="Nilog.Analyzers" Version="1.0.6" PrivateAssets="all" />
```

| Rule         | Severity | Catches                                                                        | Auto-fix |
| ------ | ---------- | --------- | :--------: |
| **NILOG001** | Warning  | An interpolated string (`$"..."`) used as the message template.                | ✅        |
| **NILOG002** | Warning  | A template whose `{Placeholder}` count ≠ the number of arguments supplied.     | —        |
| **NILOG003** | Warning  | A template built with string concatenation (`+`) or `string.Format(...)`.      | —        |
| **NILOG004** | Warning  | The same named `{Placeholder}` used twice (duplicate structured-property key). | —        |
| **NILOG005** | Info     | Positional `{0}` placeholders instead of named `{Name}` ones.                  | —        |
| **NILOG006** | Warning  | An `Exception` passed as a template value instead of the exception parameter.  | —        |
| **NILOG007** | Warning  | A malformed template — an unclosed `{` or an empty `{}` placeholder.           | —        |
| **NILOG008** | Info     | A placeholder name that is not PascalCase (`{userId}` → `{UserId}`).           | —        |

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

| Concern               | Nilog answer                                                                                                                                                                        |
| --------- | ------------- |
| Thread safety         | `volatile`, `Interlocked`, and `ConcurrentDictionary` throughout                                                                                                                    |
| Trimming / Native AOT | `IsAotCompatible=true` — trim/AOT analyzers run every build (warnings-as-errors), and the Native AOT compiler emits native code from `Nilog.dll` with zero warnings. No reflection. |
| Memory growth         | `MaxTemplateCacheEntries` stops caching at the limit instead of growing unboundedly                                                                                                 |
| Idle CPU cost         | No background timer — the UTC timestamp cache refreshes lazily, only when an exception is formatted                                                                                 |
| Process shutdown      | A final UTC refresh runs automatically on `ProcessExit`; `ShutdownUtcTimer()` for deterministic teardown                                                                            |
| Formatting robustness | Bad template falls back to raw text; throwing `ToString()` on typed args is isolated; null renders as `(null)`                                                                      |
| Sink compatibility    | `IReadOnlyList<KVP>` + `{OriginalFormat}` — works with Console, Serilog, OTel, Seq, App Insights                                                                                    |
| Supported frameworks  | .NET 8, 9, 10                                                                                                                                                                       |

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
Nilog still allocates the rendered string but avoids the `object[]` for typed args — less than the conventional
extensions at 1–6 args, equal at 9 args (376 B each), and more than `LoggerMessage.Define`/`[LoggerMessage]` at 1–3 args (v1.0.6 measurement).

**What about 9+ arguments?**
Typed overloads now reach **sixteen** (1–5 hand-written, 6–16 source-generated). A 9-arg disabled
call allocates **0 B** and runs in ~2.8 ns (v1.0.6 measurement; the conventional extension takes ~43 ns / 272 B). Only at **17+** args
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
Eight rules: `NILOG001` (interpolated templates, with a
one-click code fix), `NILOG002` (placeholder/argument count mismatch), `NILOG003` (concatenated or
`string.Format` templates), `NILOG004` (duplicate named placeholder), `NILOG005` (positional
placeholders, Info), `NILOG006` (an exception passed as a template value), `NILOG007` (malformed
template), and `NILOG008` (non-PascalCase placeholder name, Info) — across every
`Write*`/`Nilogger.Log` call shape. It's a separate, opt-in package — not referenced by
`Nilog.Core` — so installing `Nilog` never pulls it in automatically.

---

## 📄 License

MIT © Gehan Fernando. Full docs at [github.com/gcfernando/Nilog](https://github.com/gcfernando/Nilog).
