<div align="center">

# ⚡ Nilog — Zero-Allocation ILogger Extensions for .NET

### High-performance C# structured logging extensions for `Microsoft.Extensions.Logging`.

**Typed message-template overloads reduce call-site allocations while preserving your existing `ILogger` pipeline and sinks.**

[![NuGet](https://img.shields.io/badge/NuGet-v1.0.6-004880?logo=nuget&logoColor=white)](https://www.nuget.org/packages/Nilog)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![C#](https://img.shields.io/badge/C%23-latest-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Tests](https://img.shields.io/badge/tests-318%20passing%20per%20TFM-2ea44f?logo=xunit&logoColor=white)](#-build-test-benchmark)
[![Disabled path](https://img.shields.io/badge/disabled%20call-0%20bytes%20%7C%20~2%20ns-2ea44f)](#-benchmarks)
[![Enabled vs MS](https://img.shields.io/badge/enabled%20path-lower%20alloc%20than%20MEL%20extensions-0d6efd)](#-benchmarks)
[![16-arg typed](https://img.shields.io/badge/up%20to%2016--arg%20typed-0%20bytes%20disabled-brightgreen)](#-benchmarks)
[![Analyzer](https://img.shields.io/badge/Nilog.Analyzers-NILOG001--008%20%2B%20codefix-orange)](#-static-analysis-niloganalyzers)
[![AOT](https://img.shields.io/badge/Native%20AOT-enforced-blueviolet)](#-thread-safety-and-lifecycle)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-ff69b4.svg)](#-contributing)

</div>

<div align="center">
<samp>

[**Why?**](#-why-nilog) · [**vs Others**](#-nilog-vs-the-alternatives) · [**Benchmarks**](#-benchmarks) · [**Install**](#-install) · [**Quick start**](#-quick-start) · [**Pick a method**](#-choosing-the-right-method) · [**Features**](#-features) · [**Structured**](#-structured-logging-end-to-end) · [**Config**](#-optional-advanced-configuration) · [**Analyzer**](#-static-analysis-niloganalyzers) · [**Recipes**](#-recipes) · [**Best practices**](#-best-practices) · [**Migrate**](#-migrating-to-nilog) · [**API**](#-api-reference) · [**How**](#-how-it-works) · [**FAQ**](#-faq)

</samp>
</div>

<details>
<summary><b>📑 Full table of contents</b></summary>

<br>

- [⚠️ Limitations](#-limitations)
- [⚡ Why Nilog?](#-why-nilog)
- [🆚 Nilog vs the alternatives](#-nilog-vs-the-alternatives)
- [📊 Benchmarks](#-benchmarks)
  - [Disabled path](#disabled-path-level-filtered-off)
  - [Enabled path](#enabled-path-message-rendered)
  - [Scopes](#scopes-single-keyvalue-null-scope-sink)
  - [Parallel and sustained load](#parallel-and-sustained-load)
  - [Real ASP.NET Core application](#real-aspnet-core-application-kestrel-loopback-32-client-workers-3-alternating-rounds)
- [📦 Install](#-install)
- [🚀 Quick start](#-quick-start)
- [🧭 Choosing the right method](#-choosing-the-right-method)
- [✨ Features](#-features)
- [🧩 Structured logging, end to end](#-structured-logging-end-to-end)
- [⚙️ Optional advanced configuration](#-optional-advanced-configuration)
- [🔍 Static analysis (Nilog.Analyzers)](#-static-analysis-niloganalyzers)
- [🍳 Recipes](#-recipes)
  - [ASP.NET Core](#aspnet-core-controller--minimal-api)
  - [Worker / background service](#worker--background-service-hot-loop)
  - [Azure Functions (isolated worker)](#azure-functions-isolated-worker)
  - [Custom JSON exception reports](#custom-json-exception-reports)
  - [Works with Serilog as the sink](#works-with-serilog-as-the-sink)
- [✅ Best practices](#-best-practices)
- [🔀 Migrating to Nilog](#-migrating-to-nilog)
- [📖 API reference](#-api-reference)
- [🔬 How it works](#-how-it-works)
- [🏭 Production readiness](#-production-readiness)
- [🔒 Thread safety and lifecycle](#-thread-safety-and-lifecycle)
- [❓ FAQ](#-faq)
- [🛠️ Build, test, benchmark](#-build-test-benchmark)
- [🗺️ Roadmap](#-roadmap)

</details>

---

Nilog helps enterprise .NET teams reduce logging overhead without replacing their logging infrastructure. It keeps the familiar `ILogger` pipeline, structured `{Named}` templates, and existing sinks — while avoiding unnecessary allocations on common hot-path logging calls.

> The stock `ILogger` extensions allocate a **`params object[]` on every single call** — even
> when the level is switched off and the message is thrown straight in the bin. On a hot path
> that is millions of pointless allocations and a busy garbage collector.
>
> **Nilog swaps that array for a stack-only struct.** A disabled call allocates **nothing**
> and returns in about **2 ns** (measured, v1.0.6). Microsoft's own `LoggerMessage.Define` /
> `[LoggerMessage]` achieve the same disabled-path cost; Nilog's difference is that it needs no
> per-message declaration.

```csharp
using Nilog;

logger.WriteInformation("User {UserId} ordered {Count} items", userId, count);
//      ^ no object[] allocated, ever — and nothing at all when Information is disabled
```

### ❌ Before / ✅ After

<table>
<tr>
<th>❌ Plain <code>Microsoft.Extensions.Logging</code></th>
<th>✅ Nilog</th>
</tr>
<tr>
<td>

```csharp
// Allocates an object[] + boxes the ints
// on EVERY call — even if Debug is off.
logger.LogDebug(
    "User {Id} did {Action}",
    id, action);
```

</td>
<td>

```csharp
// Stack-only struct. Zero allocation when
// disabled; less allocation when enabled.
logger.WriteDebug(
    "User {Id} did {Action}",
    id, action);
```

</td>
</tr>
</table>

---

## ⚠️ Limitations

Nilog removes the call-site `object[]` allocation for common logging calls, but it does not make every logging scenario allocation-free.

| Scenario                                                | Allocation                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ---------- | ----------- |
| 0–**16** typed arguments, disabled path                 | **0 bytes** (raised from 0–8 in v1.0.4)                                                                                                                                                                                                                                                                                                                                                                                        |
| 0–**16** typed arguments, enabled path                  | rendered message string only — stack-allocated span path, no array (disabled path is always 0 B)                                                                                                                                                                                                                                                                                                                               |
| **17+** arguments                                       | falls back to `params object[]`; the `IsEnabled` guard still fires before any work is done                                                                                                                                                                                                                                                                                                                                     |
| Enabled logging                                         | may still allocate depending on the sink, formatter, and value types — Nilog cannot control a downstream sink                                                                                                                                                                                                                                                                                                                  |
| Dynamic / interpolated / concatenated templates         | each unique string grows the template cache (`Nilog.Analyzers` `NILOG001`/`NILOG003`/`NILOG004` catch this at compile time)                                                                                                                                                                                                                                                                                                    |
| `FlushAsync`                                            | **real flush (v1.0.3)** — awaits every callback registered via `Nilogger.RegisterFlush(...)`; a zero-allocation no-op only when nothing is registered                                                                                                                                                                                                                                                                          |
| `AsyncSinkFilter`                                       | an extension hook; core logging methods do not consult it directly                                                                                                                                                                                                                                                                                                                                                             |
| Arguments statically typed `object`                     | bind to the `params object[]` overload (allocates, as Microsoft); cast to the concrete type or use generics                                                                                                                                                                                                                                                                                                                    |
| **C# ≤ 12 compilers (default for .NET 8 SDK projects)** | `OverloadResolutionPriority` is ignored, so an exception passed with *extra* arguments, or a derived `Exception` type, may bind a typed overload; v1.0.6 detects an `Exception` passed as the *first* argument and attaches it as the log exception (an exception in a later position is treated as a value, NILOG006 flags it). Use C# 13 (`<LangVersion>13</LangVersion>` / .NET 9+ SDK) for the intended overload selection |
| Template / argument-count mismatch                      | typed overloads render the raw template; calls bound to `params` are rendered by Microsoft.Extensions.Logging, which may throw `FormatException` (analyzer rule NILOG002 flags both)                                                                                                                                                                                                                                           |
| Exceptions from your code                               | a throwing formatter/`ToString`/provider is **not swallowed** except where stated (typed-argument `ToString` failures render as `[ToString failed: …]`)                                                                                                                                                                                                                                                                        |
| Template cache                                          | bounded by `MaxTemplateCacheEntries` (default 10,000) and `MaxCachedTemplateLength` (default 1024 chars); templates beyond the limits are parsed per call (correct, slower). No LRU eviction                                                                                                                                                                                                                                   |

---

## ⚡ Why Nilog?

|  |  |
|--|--|
| 🚀 **Zero-alloc disabled path** | A filtered-out call allocates **0 bytes** (no array, no boxing) and takes ~2–3 ns — for **0–16** typed args. Equal to `LoggerMessage.Define` / `[LoggerMessage]` within measurement noise. |
| 🔥 **Leaner than MEL extensions when enabled** | In v1.0.6 measurements Nilog allocates less than the conventional extensions at 1–6 args; it is *not* faster than `Define`/`[LoggerMessage]` at 1–3 args and equals the conventional extension at 9 args. See [Benchmarks](#-benchmarks). |
| 🆕 **6–16 arg typed overloads** | Source-generated overloads reach **sixteen** arguments with a zero-allocation disabled path. |
| 🆕 **Typed multi-pair scopes (v1.0.4)** | `WriteScope<T1,T2>`, `WriteScope<T1,T2,T3>`, `WriteScope<T1,T2,T3,T4>` — no dictionary allocation, no array copy for the most common scope shapes. |
| 🆕 **Compact exception report (v1.0.4)** | `WriteErrorException(ex, more: false)` now allocates **< 300 B** — down from ≈ 992 B — for a single-line `[Title] Type: Message` summary. |
| 🆕 **WriteError/WriteCritical typed no-exception** | `logger.WriteError("Failed {Id}", id)` routes to a **zero-array typed overload** — no `params` fallback, no boxing. |
| 🆕 **Span-based rendering (1–16 args)** | Plain `{Name}` templates render through a stack-allocated `Span<char>` — no `StringBuilder`, no pool, no array. |
| 🆕 **Real `FlushAsync` (v1.0.3)** | `RegisterFlush(...)` lets buffering sinks drain on `FlushAsync()`; a zero-allocation no-op when nothing is registered. |
| 🆕 **8 analyzer rules + a code fix** | `Nilog.Analyzers` flags interpolation (NILOG001, one-click fix), count mismatch (002), concatenated templates (003), duplicate (004), positional (005), exception-as-value (006), malformed (007), and non-PascalCase (008). |
| 🆕 **Native AOT — compiler-enforced** | `IsAotCompatible=true` runs the trim/AOT analyzers on every build; the Native AOT compiler emits native code from `Nilog.dll` with zero warnings. |
| 🔌 **Drop-in (any engine, any cloud)** | Same `ILogger`, same `{Named}` templates, same structured output to Serilog / NLog / OTel / Seq / App Insights — verified through the real MEL pipeline. |
| 🧩 **Zero setup** | Just `using Nilog;` — no DI, no registration, no config files. |
| 🧯 **Hardened formatting** | Template/argument-count mismatches fall back to the raw template, and a throwing `ToString()` on a typed argument renders as `[ToString failed: Type threw Ex]`. Validation errors (null logger/template) and provider exceptions still propagate. |
| 🧵 **Thread-safe & AOT-ready** | Bounded, lock-free static caches; no reflection. Safe under contention, friendly to trimming and Native AOT. |
| 🎯 **Modern & multi-target** | Ships for **.NET 8 · 9 · 10** as a single `Nilog.dll`, with XML docs and SourceLink. |

---

## 🆚 Nilog vs the alternatives

Nilog is **not** a logging framework — it is a thin, zero-allocation front door to the one you
already use. The table below compares the *call-site cost* of writing a log line.

**How to read it:** every row is written so that **✅ is always the good result.**
✅ = yes / good · ❌ = no / not great · ➖ = partial.

| Question                                                             | Microsoft `ILogger` | Serilog        | **Nilog**                                          |
| ---------- | :---: | :---: | :---: |
| Plugs into your existing `ILogger` & DI?                             | ✅                   | ➖ <sup>1</sup> | ✅                                                  |
| Supports `{Named}` templates + structured properties?                | ✅                   | ✅              | ✅                                                  |
| **Avoids the `object[]` allocation per call (1–16 typed args)?**     | ❌                   | ❌              | ✅ (not for args statically typed `object`, or 17+) |
| **Allocates _nothing_ when the level is disabled?**                  | ❌                   | ❌              | ✅                                                  |
| Gets `LoggerMessage`-class *disabled-path* cost with no boilerplate? | ❌                   | ❌              | ✅ (enabled path: not faster than `LoggerMessage`)  |
| Has a built-in formatted exception report?                           | ➖ <sup>2</sup>      | ➖ <sup>2</sup> | ✅                                                  |
| Offers a zero-allocation single-key scope?                           | ❌                   | ❌              | ✅                                                  |
| Catches interpolation / mismatch footguns at compile time?           | ❌                   | ❌              | ✅ (8 rules + code fix)                             |
| Native AOT / trimming, compiler-enforced?                            | ➖                   | ➖              | ✅                                                  |
| Needs zero setup (just `using Nilog;`)?                              | ✅                   | ❌ <sup>3</sup> | ✅                                                  |

<sub>
<sup>1</sup> Serilog runs its own pipeline; it can sit behind <code>ILogger</code> as a provider.
<sup>2</sup> Both log exceptions, just not Nilog's aligned multi-field report.
<sup>3</sup> Serilog needs sink/configuration before first use.
</sub>

> [!NOTE]
> The ✅/❌ marks describe **design behaviour**, not a head-to-head benchmark of other libraries.
> The hard numbers in [Benchmarks](#-benchmarks) are measured directly against the Microsoft extensions.

### In plain words

- **Avoids the `object[]` allocation** → with Microsoft/Serilog, every `logger.Log…("…{X}…", x)`
  call quietly builds a throwaway array to hold your arguments. Nilog's typed overloads don't —
  so there is **less garbage for the GC** on every line.
- **Allocates nothing when disabled** → if `Debug`/`Trace` is switched off, Microsoft/Serilog
  still build that array *before* discarding the message. Nilog checks first and builds nothing —
  a disabled call is **~2 ns and 0 bytes** (`LoggerMessage.Define`/`[LoggerMessage]` match this).

---

## 📊 Benchmarks

> [!IMPORTANT]
> **Honest summary.** Nilog's measurable advantage is the **disabled path** (no allocation, ~2–3 ns, versus
> 17–43 ns / 56–272 B for conventional Microsoft extension methods) and, on the **enabled path, lower
> allocation and time than conventional extensions for 1–6 typed args**. Against Microsoft's *optimized*
> APIs (`LoggerMessage.Define` and `[LoggerMessage]` source generation), Nilog is **roughly equal on
> the disabled path (both ~0 allocation) and not faster on the enabled path**; at 9 args its enabled
> allocation is equal to the conventional extension and 3.6× that of the source generator.
> Nilog's value is convenience (no per-message declaration) with near-optimized cost — not that it beats
> the source generator.

> [!NOTE]
> **v1.0.6 measurements** — BenchmarkDotNet 0.15.8, **.NET 10.0.12**, Intel Core Ultra 7 265H (16 logical cores),
> Windows 11, SDK 10.0.401. Job: **3 launches × (5 warmup + 12 iterations)**, workstation concurrent GC,
> run from an **external harness that references the packed v1.0.6 `Nilog` NuGet package**.
> Sink: a logger whose `IsEnabled` is `false` (disabled) or `true` and which renders the message (enabled).
> Mean ± StdDev shown in the details below. Single-digit-ns rows are close to timer/loop-overhead resolution:
> treat differences below ~1 ns (e.g. `Define` 0.6 ns vs `[LoggerMessage]` 0.1 ns) as *measurement
> artifacts*, not real differences.
> Reproduce: see [Build, test, benchmark](#-build-test-benchmark).

### Disabled path (level filtered off)

| Typed args        | MEL extension   | `LoggerMessage.Define`   | `[LoggerMessage]` | **Nilog**        |
| :---: | :---: | :---: | :---: | :---: |
| 1                 | 17.2 ns / 56 B  | 0.6 ns / 0 B             | 0.1 ns / 0 B      | **2.0 ns / 0 B** |
| 3                 | 22.7 ns / 104 B | 1.5 ns / 0 B             | 0.9 ns / 0 B      | **1.9 ns / 0 B** |
| 6                 | 38.3 ns / 200 B | 2.1 ns / 0 B             | 1.7 ns / 0 B      | **2.0 ns / 0 B** |
| 9                 | 42.6 ns / 272 B | n/a (Define supports ≤6) | 2.6 ns / 0 B      | **2.8 ns / 0 B** |
| exception (1 arg) | 20.8 ns / 56 B  | —                        | —                 | **2.1 ns / 0 B** |

Nilog is ~10–20× faster than the conventional extension methods and **statistically indistinguishable
from the optimized Microsoft APIs** (within ~1 ns). Zero allocation holds for 0–16 typed args and is
asserted by the test suite.

### Enabled path (message rendered)

| Typed args                          | MEL extension    | `LoggerMessage.Define` | `[LoggerMessage]`    | **Nilog**                |
| :---: | :---: | :---: | :---: | :---: |
| 1                                   | 53.0 ns / 88 B   | **22.6 ns / 32 B**     | **22.7 ns / 32 B**   | 29.4 ns / 56 B           |
| 3                                   | 106.1 ns / 152 B | **72.0 ns / 48 B**     | 77.3 ns / 48 B       | 81.1 ns / 104 B          |
| 6                                   | 240.6 ns / 280 B | 220.8 ns / 280 B       | 231.1 ns / 280 B     | **156.8 ns / 208 B**     |
| 9                                   | 380.7 ns / 376 B | n/a                    | **211.2 ns / 104 B** | 360.0 ns / 376 B         |
| exception (1 arg)                   | 58.4 ns / 96 B   | —                      | —                    | 34.6 ns / 64 B           |
| `object`-typed ×3 (params fallback) | 96.0 ns / 96 B   | —                      | —                    | 92.6 ns / 96 B (≈ equal) |

* Versus the **conventional extension**, Nilog is faster/leaner at 1–6 args and with an exception, and equal at 9 args.
* Versus **`Define` / `[LoggerMessage]`**, Nilog is **slower and allocates more at 1–3 args**, ahead only at 6 args
  in this run, and **well behind the source generator at 9 args**.
* Arguments statically typed as `object` bind to the `params` overload: **no improvement over Microsoft** (see
  [Limitations](#-limitations)).
* The confidence intervals of several enabled rows overlap (StdDev 5–15%); only the 3-arg and 6-arg
  allocation figures are deterministic.

### Scopes (single key/value, null scope sink)

|                                          | Mean        | Alloc    |
| --- | ---: | ---: |
| `logger.BeginScope(template, arg)` (MEL) | 21.9 ns     | 240 B    |
| `logger.WriteScope("k", v)` (Nilog)      | **11.5 ns** | **80 B** |

> The scope row measures scope *creation* against a logger that does not consume it. With a real
> `LoggerExternalScopeProvider` (per-scope allocation, `GC.GetAllocatedBytesForCurrentThread`): Nilog `WriteScope("k", int)` **176 B**, string value 152 B, Microsoft `BeginScope("k {V}", int)` **224 B**. Earlier README versions quoted 24 B — that figure was wrong for real providers and is withdrawn.
> Exception reports (measured): compact `WriteErrorException(ex)` **184 B**, verbose (`moreDetailsEnabled: true`) **4,208 B** per call.

### Native Serilog (reference only)

`Serilog` disabled 3-arg: 4.3 ns / 0 B; enabled 3-arg without rendering: 247.6 ns / 496 B. Serilog is shown for
orientation only — its pipeline and sink model are not equivalent to `ILogger`, so no superiority claim is made.

### Parallel and sustained load

| Benchmark                                      | Microsoft          | Nilog              | Result                                               |
| ----------- | ----------- | ------- | -------- |
| Parallel logs, 50,000 (`MediumRun`, net10)     | 3.463 ms / 5.34 MB | 3.542 ms / 3.82 MB | time equal (within noise); ~28% less allocation      |
| Process-wide allocation per enabled 1-arg call | 112 B              | 80 B               | enabled path **does allocate** (the rendered string) |

The earlier "0 B parallel" claim was **incorrect and has been withdrawn**: only the *disabled* path is allocation-free.

### Real ASP.NET Core application (Kestrel, loopback, 32 client workers, 3 alternating rounds)

|                       | Microsoft         | Nilog               |
| --- | ---: | ---: |
| Allocated per request | 1,833 B           | **1,696 B (−7.5%)** |
| Throughput            | 26.2–29.6 k req/s | 23.7–27.0 k req/s   |
| p99 latency           | 3.4–4.7 ms        | 4.4–5.4 ms          |

At application level the difference is a **small allocation reduction**; throughput and latency were **not
improved** (the client shared the machine, so the throughput result is inconclusive). Do not extrapolate
micro-benchmark ratios to application performance.

<details>
<summary><b>📐 Methodology and limits</b></summary>

<br>

| Property        | Value                                                                                                               |
| ---------- | ------- |
| **Machine**     | Intel Core Ultra 7 265H, 16 logical cores, Windows 11 (10.0.26200)                                                  |
| **Runtime**     | .NET 10.0.12 · x64 RyuJIT x86-64-v3 (net8/net9 not re-benchmarked for v1.0.6)                                       |
| **Tool**        | BenchmarkDotNet 0.15.8 · LaunchCount 3, WarmupCount 5, IterationCount 12                                            |
| **Harness**     | external project referencing the packed `Nilog 1.0.6` package + Serilog 4.2.0 (not the in-repo benchmark project)   |
| **Not covered** | Linux/ARM64 timing, net8/net9 timing, real provider fan-out cost, sustained soak, 10–16-arg rows in the new harness |

The in-repo `Nilog.Benchmark` project still contains the **v1.0.5-era suites** (21 classes; short-run). Its historical
numbers (e.g. "240× faster disabled path") compared only against the conventional extension and used a 3-iteration
job; they are **not** reproduced here because they overstated the practical gap versus Microsoft's optimized APIs.

</details>
---

## 📦 Install

```bash
dotnet add package Nilog
```

```xml
<PackageReference Include="Nilog" Version="1.0.6" />
```

Optional, build-time-only static-analysis package:

```xml
<PackageReference Include="Nilog.Analyzers" Version="1.0.6" PrivateAssets="all" />
```

### Compatibility

| | |
|--|--|
| **Runtimes** | .NET 8.0, .NET 9.0, .NET 10.0 |
| **Language** | C# (latest) |
| **Depends on** | `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.ObjectPool` |
| **Works with any sink** | Console, Debug, Serilog, NLog, OpenTelemetry, Seq, Application Insights, … (verified through the real MEL pipeline) |
| **Native AOT / trimming** | ✅ compiler-enforced (`IsAotCompatible=true`, no reflection) |
| **Thread-safe** | ✅ all members |

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

// Structured, strongly-typed, zero array allocation
logger.WriteInformation("User {UserId} signed in from {Ip}", 42, "10.0.0.1");

// An exception with context
try { Risky(); }
catch (Exception ex)
{
    logger.WriteError("Checkout failed for cart {CartId}", ex, cartId);
}
```

Everything still flows through the standard pipeline, so structured properties (`UserId`, `Ip`, …)
and the original template reach Serilog, OpenTelemetry, Seq, Application Insights, or any other
sink exactly as they normally would.

---

## 🧭 Choosing the right method

A quick map from "what I want to log" to "what to call":

| I want to…                                    | Call                                                          | Allocates?                                        |
| ------------ | ------ | :----------: |
| Log a constant message                        | `logger.WriteInformation("Started")`                          | **none**                                          |
| Log 1–16 structured values                    | `logger.WriteInformation("User {Id}", id)`                    | **none** (typed)                                  |
| Log 17+ structured values                     | `logger.WriteInformation("{A} … {Q}", …)`                     | one `object[]`                                    |
| Log an error **with** an exception            | `logger.WriteError("Failed {Id}", ex, id)`                    | **none** (typed)                                  |
| Log an error **without** an exception (typed) | `logger.WriteError("Validation failed {Id}", id)`             | **none** (typed)                                  |
| Log an error **without** an exception (plain) | `logger.WriteError("Bad request")`                            | **none**                                          |
| Exception report — compact summary            | `logger.WriteErrorException(ex, "Title")`                     | **< 300 B**                                       |
| Exception report — full verbose               | `logger.WriteErrorException(ex, "Title", more: true)`         | report buffer only                                |
| Decide the level at runtime                   | `Nilogger.Log(logger, level, "…", a, b)`                      | **none** for 0–16 typed                           |
| Attach 1-pair context to a block              | `using (logger.WriteScope("Key", value)) { … }`               | ~80 B measured in v1.0.6 (scope wrapper + boxing) |
| Attach 2–4 pair context (typed, no dict)      | `using (logger.WriteScope("K1", v1, "K2", v2)) { … }`         | only boxed values                                 |
| Drain a buffering sink on shutdown            | `Nilogger.RegisterFlush(...)` + `await Nilogger.FlushAsync()` | n/a                                               |
| Catch template mistakes at compile time       | add the `Nilog.Analyzers` package                             | n/a — build-time only                             |

> [!TIP]
> Keep templates to **≤ 16 named holes** to stay on the zero-array typed path. For error/critical,
> both the exception and no-exception forms are fully typed — **pass the exception as the second argument** when you have one.

---

## ✨ Features

### 🎚️ Six levels, two styles

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

### 🧱 Structured logging without the allocation

One to **sixteen** arguments bind to **strongly-typed** overloads — no `object[]`, and nothing at all
when the level is off (1–5 are hand-written; 6–16 are source-generated, **extended to 16 in v1.0.4**):

```csharp
logger.WriteInformation("Order {Id} total {Amount:C}", orderId, amount);
logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region}", userId, sku, qty, region);
logger.WriteInformation("Order {Id} for {User} via {Carrier} to {City}, {Country} ({Tier}) ref {Ref} at {Ts}",
    orderId, user, carrier, city, country, tier, refId, ts); // 8 args — zero-array
logger.WriteInformation("{A} {B} {C} {D} {E} {F} {G} {H} {I}", a, b, c, d, e, f, g, h, i);
// 9 args — still typed and zero-alloc on the disabled path (NEW in v1.0.4)
```

Seventeen or more arguments transparently fall back to the familiar `params` form:

```csharp
logger.WriteInformation("{A} {B} {C} … {Q}", a, b, c, /* … */ q); // 17+ → params object[]
```

Standard template niceties all work — escaping and alignment/format specifiers included:

```csharp
logger.WriteInformation("Progress {Percent,3}% of {{total}}", 7);   // "Progress   7% of {total}"
logger.WriteInformation("Id {Id:000}", 42);                          // "Id 042"
```

<details>
<summary><b>🧯 Rich exception reports</b></summary>

<br>

```csharp
catch (Exception ex)
{
    // One-line summary at Error
    logger.WriteErrorException(ex, title: "Payment failed");

    // Or the full report: stack trace + a walk of inner / aggregate exceptions
    logger.WriteErrorException(ex, title: "Payment failed", moreDetailsEnabled: true);
}
```

The built-in formatter produces an aligned, readable block:

```text
Timestamp      : 2026-06-15T10:21:38.5116876Z
Title          : Payment failed
Exception Type : System.InvalidOperationException
Message        : Could not load user profile
HResult        : -2146233079
Source         : MyApp.Billing
Target Site    : LoadProfile

Stack Trace    :
   at MyApp.Billing.LoadProfile() ...

---- Inner Exceptions ----
> Exception Type : System.Collections.Generic.KeyNotFoundException
> Message        : profile 'alice' not found in cache
```

> [!NOTE]
> **Seeing the report squashed onto one line in the console?** That's the console formatter,
> not Nilog — Nilog always emits the full multi-line text. Microsoft's `SimpleConsole` replaces
> every newline with a space when `SingleLine = true`. Use `o.SingleLine = false` (the default)
> to keep the layout above. File sinks, Azure (App Insights / Log Analytics), Seq, and JSON/
> structured sinks all preserve the line breaks regardless.
>
> ```csharp
> builder.AddSimpleConsole(o => o.SingleLine = false); // keep multi-line reports
> ```

Don't like the format? Swap it out globally (set it back to `null` to restore the default):

```csharp
Nilogger.ExceptionFormatter = (ex, title, verbose) =>
    JsonSerializer.Serialize(new { title, type = ex.GetType().Name, ex.Message });
```

</details>

<details>
<summary><b>🏷️ Scopes, the easy way</b></summary>

<br>

```csharp
// Single-pair scope — small wrapper allocation (~80 B measured in v1.0.6)
using (logger.WriteScope("RequestId", requestId))
{
    logger.WriteInformation("Handling request");   // carries RequestId

    // NEW in v1.0.4 — typed multi-pair scopes: no dictionary, no array copy
    using (logger.WriteScope("UserId", userId, "Tenant", tenant))  // WriteScope<T1,T2>
    {
        logger.WriteWarning("Quota at {Percent}%", 90);  // carries RequestId + UserId + Tenant
    }
}

// 3-pair typed scope (WriteScope<T1,T2,T3>) — used in Nilog.Function for correlation context
using (logger.WriteScope("OrderId", orderId, "CustomerId", customerId, "Currency", "GBP"))
{
    logger.WriteInformation("Order opened");
}

// 4-pair typed scope (WriteScope<T1,T2,T3,T4>)
using (logger.WriteScope("Region", "eu-west-1", "Az", "az-1", "Host", "node-01", "Pod", "pod-7b"))
{
    logger.WriteDebug("Infrastructure context attached");
}

// Dictionary scope still supported for larger or dynamic sets
using (logger.WriteScope(new Dictionary<string, object> { ["Key"] = value, /* … */ }))
{
    logger.WriteInformation("Dynamic context");
}
```

Single-pair scopes allocate a small wrapper (~80 B measured in v1.0.6, versus 240 B for `BeginScope`). The typed
2–4 pair overloads (`WriteScope<T1,T2>` etc.) use readonly structs internally — no dictionary heap
allocation, no array copy. For five or more pairs, pass an `IDictionary` or
`IReadOnlyDictionary`; Nilog's allocation-light `SmallScopeWrapper` path handles up to four
entries, `ScopeWrapper` handles five or more. Values are always copied, so mutating a dictionary
afterwards never corrupts a scope in flight.

</details>

<details>
<summary><b>🔌 Optional: draining a buffering sink on shutdown</b></summary>

<br>

```csharp
Nilogger.UseAsyncSinkProvider((level, message, ex) => level >= LogLevel.Information);
await Nilogger.FlushAsync();   // no callbacks registered → zero-allocation no-op
```

`FlushAsync` is a **real flush** (since v1.0.3): a buffering/batching sink calls
`Nilogger.RegisterFlush(cancellationToken => DrainAsync(cancellationToken))` once, and every
subsequent `Nilogger.FlushAsync()` awaits every registered callback in order. If no sink ever
registers — which is the normal case for Console/Serilog/NLog/OpenTelemetry-style sinks that
flush themselves — `FlushAsync` stays exactly what it always was: `Task.CompletedTask` returned
synchronously with **0 allocations**. This hook only matters if you write or consume a custom
buffering sink; most applications never need to touch it.

</details>

---

## 🧩 Structured logging, end to end

A single call carries three things to your sink — the **rendered message**, the **named
properties**, and the **original template** (`{OriginalFormat}`) — with no array in sight:

```csharp
logger.WriteInformation("User {UserId} bought {Sku} x{Qty}", 42, "A-100", 3);
```

| What the sink receives | Value                               |
| ------------------------ | ------- |
| Rendered message       | `User 42 bought A-100 x3`           |
| `UserId`               | `42`                                |
| `Sku`                  | `"A-100"`                           |
| `Qty`                  | `3`                                 |
| `{OriginalFormat}`     | `User {UserId} bought {Sku} x{Qty}` |

That means a JSON/structured sink (Serilog, Seq, OpenTelemetry, Application Insights) gets clean,
queryable fields — exactly as if you had used the framework's own templated logging, just without
the per-call array.

---

## ⚙️ Optional advanced configuration

> [!IMPORTANT]
> **No Nilog-specific configuration or DI registration is required for normal use.** Everything
> in the [Quick start](#-quick-start) works the moment the package is installed — no service
> registration, no logger factory, no config file, no manual initialization, and no
> `IsEnabled(...)` guards around ordinary `Write*` calls. This section is entirely optional and
> most applications will never need it.

### ✅ Normal Nilog usage (no setup)

```csharp
using Nilog;

logger.WriteInformation("User {UserId} logged in", userId);
```

That's it. `logger` is whatever `ILogger` your application already has.

### 🔧 Standard Microsoft.Extensions.Logging configuration

Levels, categories, and providers are **not** Nilog settings — they belong to MEL and keep working
exactly as before:

```csharp
builder.Logging
    .SetMinimumLevel(LogLevel.Information)
    .AddFilter("Microsoft", LogLevel.Warning)
    .AddConsole();
```

Nilog's `IsEnabled` checks honour all of this automatically; there is nothing Nilog-specific to
configure to make filtering work.

### 🧰 Optional Nilog hooks

A handful of **static, process-wide** settings on `Nilogger` exist for advanced scenarios — custom
exception formatting, draining a custom buffering sink, or tuning the template cache. None of them
are required, and sensible defaults apply if you never touch them:

```csharp
using Microsoft.Extensions.Logging;
using Nilog;

// Customise how exceptions are rendered by WriteErrorException / WriteCriticalException.
// Assign null at any time to restore the built-in formatter.
Nilogger.ExceptionFormatter = (exception, title, verbose) =>
    $"[{title}] {exception.GetType().Name}: {exception.Message}";

// Decide which entries a custom async/batch sink should keep (default: keep everything).
Nilogger.UseAsyncSinkProvider((level, message, exception) => level >= LogLevel.Information);

// Only needed if a buffering sink registered a flush callback via RegisterFlush.
// With nothing registered this is a zero-allocation no-op.
await Nilogger.FlushAsync();

// Only needed for deterministic teardown in short-lived hosts/tests. Nilog already performs
// a final timestamp refresh automatically on process exit, so ordinary apps never call this.
Nilogger.ShutdownUtcTimer();
```

| Setting                                                              | Default                      | What it controls                                                                                                                                                                                                                             |
| --------- | --------- | ------------------ |
| `Nilogger.ExceptionFormatter`                                        | built-in aligned report      | How `WriteErrorException` / `WriteCriticalException` render an exception. Set to `null` to restore the default.                                                                                                                              |
| `Nilogger.UseAsyncSinkProvider(filter)` → `Nilogger.AsyncSinkFilter` | keep everything              | Predicate consulted by a custom async/batch sink. Passing `null` leaves the current filter unchanged.                                                                                                                                        |
| `Nilogger.MaxTemplateCacheEntries`                                   | 10,000                       | Maximum parsed templates to cache. When the limit is hit, new templates are still parsed correctly but not stored, preventing unbounded memory growth from interpolated templates.                                                           |
| `Nilogger.RegisterFlush(callback)` / `UnregisterFlush(callback)`     | none registered              | Lets a buffering/batching sink register how to drain itself so `FlushAsync` can actually flush it.                                                                                                                                           |
| `Nilogger.FlushAsync(cancellationToken)`                             | `Task.CompletedTask` (no-op) | **Real flush** when one or more callbacks are registered via `RegisterFlush` — awaits each in order. Stays a zero-allocation no-op when nothing is registered.                                                                               |
| `Nilogger.ShutdownUtcTimer()`                                        | auto on process exit         | There is no background timer to stop — the UTC timestamp cache already refreshes lazily on read. This forces one final refresh for deterministic teardown; kept for source/binary compatibility. Idempotent and safe to call more than once. |

> [!NOTE]
> **Log levels and category filters are _not_ a Nilog setting.** Nilog rides on the standard
> `Microsoft.Extensions.Logging` pipeline, so configure minimum levels the usual way — on the
> logging builder or in `appsettings.json` — and Nilog's `IsEnabled` checks honour all of it.

```csharp
// Standard Microsoft.Extensions.Logging setup — Nilog respects every bit of it.
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Information)
        .AddFilter("Microsoft", LogLevel.Warning)
        .AddConsole();
});
```

```jsonc
// ...or via appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

---

## 🔍 Static analysis (Nilog.Analyzers)

Every optimization above — the zero-array typed overloads, the template cache, the span-based
render path — depends on one thing: **the message argument is a stable string literal.** There
is exactly one mistake that quietly defeats all of it at once, and it compiles cleanly with no
warning:

```csharp
logger.WriteInformation($"User {id} signed in"); // compiles fine. silently undoes everything above.
```

`Nilog.Analyzers` is a separate, **opt-in** Roslyn analyzer package (**8 rules + a code fix** —
full parity with the established SerilogAnalyzer rule set) that exists to catch exactly
this and seven related footguns. It is **not** referenced by `Nilog.Core` — installing `Nilog`
never pulls it in — so adding it is a deliberate choice with zero risk to anyone who doesn't.

### Why this one mistake matters so much

| Without the analyzer                                                                                             | With `Nilog.Analyzers`                                  |
| --- | --- |
| `$"User {id} signed in"` compiles silently                                                                       | `NILOG001` warning at build time, in your IDE and in CI |
| A new literal string is built on **every single call**                                                           | Caught before the PR is even opened                     |
| The template cache (`MaxTemplateCacheEntries`, default 10,000) fills up with one-off entries and stops caching   | Never happens                                           |
| `id` never becomes a structured property — your Seq/Application Insights query for `UserId = 42` returns nothing | Structured properties stay queryable                    |
| Only discoverable by reading rendered log output in production                                                   | Discoverable by reading a compiler warning              |

### What it catches

| ID         | Severity | Condition                                                                                                        | Code fix    |
| ---- | ---------- | ----------- | :--------: |
| `NILOG001` | Warning  | An interpolated string (`$"..."`) is passed as the message-template argument to any `Write*`/`Nilogger.Log` call | ✅ one-click |
| `NILOG002` | Warning  | A constant template's `{Placeholder}` count does not match the number of arguments supplied                      | —           |
| `NILOG003` | Warning  | The template is built with string concatenation (`"a" + b`) or `string.Format(...)`                              | —           |
| `NILOG004` | Warning  | The same named `{Placeholder}` appears more than once (duplicate structured-property key)                        | —           |
| `NILOG005` | Info     | Positional `{0}` placeholders used instead of named `{Name}` ones                                                | —           |
| `NILOG006` | Warning  | An `Exception` is passed as a template value instead of the exception parameter                                  | —           |
| `NILOG007` | Warning  | The template is malformed — an unclosed `{` or an empty `{}` placeholder                                         | —           |
| `NILOG008` | Info     | A placeholder name is not PascalCase (e.g. `{userId}` → `{UserId}`)                                              | —           |

It fires identically across every call shape Nilog exposes — extension methods, the static API,
with or without an exception. **NILOG001** also offers a one-click *"Convert to a literal template
with arguments"* fix that rewrites the interpolation for you:

```csharp
// ❌ NILOG001 — interpolation defeats the template cache (one-click fix available).
logger.WriteInformation($"User {id} signed in");          // → fix → ("User {id} signed in", id)
logger.WriteError($"Order {id} failed", ex);
Nilogger.Log(logger, LogLevel.Warning, $"Retry {attempt} for {job}");

// ❌ NILOG002 — 2 placeholders, 1 argument.
logger.WriteInformation("{A} {B}", a);
// ❌ NILOG003 — concatenated template.
logger.WriteInformation("User " + id + " signed in");
// ❌ NILOG004 — {Id} used twice; the second silently overwrites the first.
logger.WriteInformation("{Id} retried {Id}", a, b);
// 🔵 NILOG005 (Info) — positional placeholders; prefer named ones.
logger.WriteInformation("{0} {1}", a, b);
// ❌ NILOG006 — exception passed as a value; use the exception parameter.
logger.WriteInformation("Failed {Error}", ex);
// ❌ NILOG007 — malformed template (unclosed brace).
logger.WriteInformation("Unclosed {Brace");
// 🔵 NILOG008 (Info) — placeholder name should be PascalCase.
logger.WriteInformation("User {userId}", id);

// ✅ No diagnostic — one cached template per call site, every value a real structured property.
logger.WriteInformation("User {UserId} signed in", id);
logger.WriteError("Order {OrderId} failed", ex, id);
Nilogger.Log(logger, LogLevel.Warning, "Retry {Attempt} for {Job}", attempt, job);
```

### Install it

```xml
<PackageReference Include="Nilog.Analyzers" Version="1.0.6" PrivateAssets="all" />
```

`PrivateAssets="all"` keeps it a build-time-only dependency — it never ships inside your output
or gets pulled transitively into anything that references your project.

Building this repo from source instead of consuming the NuGet package? Reference the analyzer
project directly with `OutputItemType="Analyzer"` so it runs at build time without becoming a
runtime dependency — exactly how [`Nilog.Function`](Nilog/Nilog.Function) wires it in:

```xml
<ProjectReference Include="..\Nilog.Analyzers\Nilog.Analyzers.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

### Confirm it's active

Build a project that has it referenced — any interpolated template should immediately produce:

```text
warning NILOG001: Pass a literal template with '{Name}' placeholders and separate arguments
instead of an interpolated string - interpolation defeats Nilog's zero-allocation template
cache and loses structured properties
```

If you see nothing on a known-bad line, check that the package/project reference actually has
`OutputItemType="Analyzer"` (for project references) and that the file is part of the build
(not excluded, not in `obj`/`bin`).

### Treating it as an error in CI

A warning is easy to miss in a noisy build log. Promote just this rule to an error without
affecting any other warning in the project:

```xml
<PropertyGroup>
  <!-- Promote the correctness rules; NILOG005/008 are Info-only style suggestions. -->
  <WarningsAsErrors>$(WarningsAsErrors);NILOG001;NILOG002;NILOG003;NILOG004;NILOG006;NILOG007</WarningsAsErrors>
</PropertyGroup>
```

Or scope it via `.editorconfig` instead, so it's consistent across every project in the
repository without touching each `.csproj`:

```ini
[*.cs]
dotnet_diagnostic.NILOG001.severity = error
dotnet_diagnostic.NILOG002.severity = error
dotnet_diagnostic.NILOG003.severity = error
dotnet_diagnostic.NILOG004.severity = error
```

### Suppressing a single, deliberate exception

Occasionally a genuinely dynamic message is the right call (rare — most "dynamic" messages
should be a structured argument instead). Suppress just that line, with a reason, rather than
disabling the rule project-wide:

```csharp
#pragma warning disable NILOG001 // Diagnostic-only log line; not on a hot path, structure not needed.
logger.WriteInformation($"Startup environment dump: {Environment.GetEnvironmentVariables()}");
#pragma warning restore NILOG001
```

> [!NOTE]
> The check is syntax-based: it looks at the literal expression passed directly at the call
> site. It does **not** catch interpolation hidden behind a local variable
> (`var msg = $"User {id}"; logger.WriteInformation(msg);`) — that needs data-flow analysis,
> which isn't implemented yet (tracked on the [Roadmap](#-roadmap)). It catches the
> overwhelmingly common case: the mistake made directly at the call site.

---

## 🍳 Recipes

### ASP.NET Core (controller / minimal API)

```csharp
using Nilog;

app.MapPost("/orders", (OrderRequest req, ILogger<OrdersController> logger) =>
{
    using (logger.WriteScope("CorrelationId", req.CorrelationId))
    {
        logger.WriteInformation("Creating order for {CustomerId}", req.CustomerId);
        try
        {
            var id = orders.Create(req);
            logger.WriteInformation("Order {OrderId} created", id);
            return Results.Ok(id);
        }
        catch (Exception ex)
        {
            logger.WriteError("Order creation failed for {CustomerId}", ex, req.CustomerId);
            return Results.Problem("Could not create order");
        }
    }
});
```

### Worker / background service hot loop

```csharp
public sealed class IngestWorker(ILogger<IngestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = await queue.DequeueAsync(stoppingToken);

            // Trace is usually OFF in prod — with Nilog this line is ~2 ns and 0 bytes.
            logger.WriteTrace("Dequeued {Count} messages", batch.Count);

            foreach (var msg in batch)
            {
                logger.WriteDebug("Processing {MessageId}", msg.Id);
            }
        }
    }
}
```

### Azure Functions (isolated worker)

Wire Nilog the way a real function app would: set the global hooks **once** at startup, choose the
minimum level by **build configuration**, and open a correlation **scope per invocation** with
worker middleware — so functions *and* injected services log through `ILogger<T>` and inherit it.

```csharp
// Program.cs
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// Minimum level by build configuration — chatty in Debug, lean in Release.
#if DEBUG
builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

// Global Nilog hooks — set once, process-wide.
Nilogger.ExceptionFormatter = (ex, title, verbose) => /* JSON for App Insights */ ...;
Nilogger.UseAsyncSinkProvider((level, _, _) => level >= LogLevel.Warning);

builder.UseMiddleware<RequestLoggingMiddleware>();   // correlation scope per invocation

var app = builder.Build();

// Drain buffered async work + stop the timestamp timer on graceful shutdown.
app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
{
    Nilogger.FlushAsync().GetAwaiter().GetResult();
    Nilogger.ShutdownUtcTimer();
});

app.Run();
```

```csharp
// One scope wraps the whole invocation — every line (in functions AND services)
// carries CorrelationId + Function without repeating them.
public sealed class RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger)
    : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        using (logger.WriteScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = context.InvocationId,
            ["Function"] = context.FunctionDefinition.Name,
        }))
        {
            try { await next(context); }
            catch (Exception ex)
            {
                logger.WriteCriticalException(ex, "unhandled.function.exception", moreDetailsEnabled: true);
                throw;
            }
        }
    }
}
```

> The full runnable version lives in [`Nilog.Function`](Nilog/Nilog.Function) — an HTTP checkout
> service (`POST /api/orders`) that exercises every Nilog feature against a real flow.

### Custom JSON exception reports

```csharp
// At startup:
Nilogger.ExceptionFormatter = (ex, title, verbose) => JsonSerializer.Serialize(new
{
    title,
    type = ex.GetType().FullName,
    ex.Message,
    stack = verbose ? ex.StackTrace : null,
});

// Anywhere:
logger.WriteCriticalException(ex, "Database unavailable", moreDetailsEnabled: true);
```

### Works with Serilog as the sink

```csharp
// Serilog is the provider; Nilog is the (zero-alloc) call site.
using var loggerFactory = LoggerFactory.Create(b => b.AddSerilog(
    new LoggerConfiguration().WriteTo.Console().CreateLogger()));

ILogger logger = loggerFactory.CreateLogger("App");
logger.WriteInformation("User {UserId} logged in", 42); // structured props reach Serilog
```

---

## ✅ Best practices

| Do ✅                                                                | Don't ❌                                                                                          |
| ------ | --------- |
| `logger.WriteInformation("User {Id}", id)` — templated & structured | `logger.WriteInformation($"User {id}")` — interpolation kills structure **and** always allocates |
| Add `Nilog.Analyzers` to catch interpolated templates at build time | Relying on code review to catch `$"..."` templates                                               |
| Keep to **≤ 16 named holes** for the zero-array path                | Pack 17+ values into one line and take the `params` array                                        |
| Pass the exception: `WriteError("msg {X}", ex, x)`                  | `WriteError("msg " + value)` — string concatenation                                              |
| Let the level filter decide; Nilog checks `IsEnabled` for you       | Wrap calls in your own `if (logger.IsEnabled(...))` — it's redundant                             |
| Use `WriteScope` for request/correlation context                    | Re-log the same ids on every line                                                                |
| `using (logger.WriteScope(...))` so the scope disposes              | Forget to dispose a scope                                                                        |

---

## 🔀 Migrating to Nilog

Mostly a find-and-replace. The semantics and templates are identical — only the method name (and,
for errors, the **argument order**) changes.

### From `Microsoft.Extensions.Logging`

| Microsoft                                | Nilog                                                                               |
| ----------- | ------- |
| `logger.LogTrace("t {A}", a)`            | `logger.WriteTrace("t {A}", a)`                                                     |
| `logger.LogDebug("…")`                   | `logger.WriteDebug("…")`                                                            |
| `logger.LogInformation("…")`             | `logger.WriteInformation("…")`                                                      |
| `logger.LogWarning("…")`                 | `logger.WriteWarning("…")`                                                          |
| `logger.LogError(ex, "Failed {Id}", id)` | `logger.WriteError("Failed {Id}", ex, id)` ⚠️ exception moves **after** the message |
| `logger.LogCritical(ex, "…")`            | `logger.WriteCritical("…", ex)`                                                     |
| `logger.BeginScope(state)`               | `logger.WriteScope(key, value)` / `logger.WriteScope(dictionary)`                   |

### From Serilog

| Serilog                                     | Nilog                                      |
| --------- | ------- |
| `log.Information("User {Id}", id)`          | `logger.WriteInformation("User {Id}", id)` |
| `log.Error(ex, "Failed {Id}", id)`          | `logger.WriteError("Failed {Id}", ex, id)` |
| `using (LogContext.PushProperty("Key", v))` | `using (logger.WriteScope("Key", v))`      |

> [!IMPORTANT]
> Microsoft's `LogError(exception, message, args)` takes the **exception first**; Nilog's
> `WriteError(message, exception, args)` takes the **message first**. Double-check that swap.

---

## 📖 API reference

All members live in namespace `Nilog`. The `Write*` methods are extension methods on `ILogger`;
`Nilogger` also exposes a static API and the global settings.

<details open>
<summary><b>Level methods (Trace / Debug / Information / Warning)</b></summary>

<br>

```csharp
// No-exception levels — typed for 1–16 args (1–5 hand-written, 6–16 source-generated), params for 17+.
void WriteTrace      (this ILogger logger, string message, params object[] args);
void WriteTrace<T0>  (this ILogger logger, string message, T0 arg0);
void WriteTrace<T0,T1>          (this ILogger logger, string message, T0 arg0, T1 arg1);
void WriteTrace<T0,T1,T2>       (this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2);
void WriteTrace<T0,T1,T2,T3>    (this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3);
void WriteTrace<T0,T1,T2,T3,T4> (this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4);
// …and 6–16-argument overloads generated by Nilog.SourceGenerators (extended to 16 in v1.0.4):
void WriteTrace<T0,/*…*/,T15>   (this ILogger logger, string message, T0 arg0, /*…*/, T15 arg15);

// Identical shape for WriteDebug, WriteInformation, WriteWarning.
```

</details>

<details>
<summary><b>Error / Critical methods</b></summary>

<br>

```csharp
// Without an exception — typed for 1–16 args (zero-array), params fallback for 17+:
void WriteError    (this ILogger logger, string message, params object[] args);
void WriteError<T0>(this ILogger logger, string message, T0 arg0);
// … T0,T1 / T0,T1,T2 / … up to …
void WriteError<T0,/*…*/,T15> (this ILogger logger, string message, T0 arg0, /*…*/, T15 arg15);

// With an exception — typed for 1–16 args (zero-array), params fallback for 17+:
void WriteError    (this ILogger logger, string message, Exception exception, params object[] args);
void WriteError<T0>(this ILogger logger, string message, Exception exception, T0 arg0);
// … up to …
void WriteError<T0,/*…*/,T15> (this ILogger logger, string message, Exception exception, T0 arg0, /*…*/, T15 arg15);

// Identical shape for WriteCritical.
```

</details>

<details>
<summary><b>Exception reports</b></summary>

<br>

```csharp
void WriteErrorException   (this ILogger logger, Exception ex,
                            string title = "System Error",          bool moreDetailsEnabled = false);
void WriteCriticalException(this ILogger logger, Exception ex,
                            string title = "Critical System Error", bool moreDetailsEnabled = false);
```

</details>

<details>
<summary><b>Scopes</b></summary>

<br>

```csharp
// Single-pair — small wrapper allocation (~80 B measured in v1.0.6)
IDisposable WriteScope(this ILogger logger, string key, object value);

// Typed multi-pair — NEW in v1.0.4: no dictionary, no array, backed by readonly structs
IDisposable WriteScope<T1,T2>        (this ILogger logger, string k1, T1 v1, string k2, T2 v2);
IDisposable WriteScope<T1,T2,T3>     (this ILogger logger, string k1, T1 v1, string k2, T2 v2, string k3, T3 v3);
IDisposable WriteScope<T1,T2,T3,T4>  (this ILogger logger, string k1, T1 v1, string k2, T2 v2, string k3, T3 v3, string k4, T4 v4);

// Dictionary overloads — for dynamic or large key sets
IDisposable WriteScope(this ILogger logger, IDictionary<string, object> context);
IDisposable WriteScope(this ILogger logger, IReadOnlyDictionary<string, object> context);
```

</details>

<details>
<summary><b>Static <code>Nilogger.Log</code> (runtime level)</b></summary>

<br>

```csharp
void Log                (ILogger logger, LogLevel level, string message);
void Log<T0>            (ILogger logger, LogLevel level, string message, T0 arg0);
// … typed for 1–16 args (6–16 source-generated), all zero-array …
void Log<T0,/*…*/,T15>  (ILogger logger, LogLevel level, string message, T0 arg0, /*…*/, T15 arg15);

void Log(ILogger logger, LogLevel level, string message, Exception exception, params object[] args);
void Log(ILogger logger, LogLevel level, string message, params object[] args);
void Log(ILogger logger, LogLevel level, Exception exception, string messageTemplate, params object[] args);
```

> [!NOTE]
> The `Exception` overload `Log(logger, level, message, Exception, params object[])` carries
> `[OverloadResolutionPriority(1)]` (v1.0.3), so a call with a leading `Exception` argument —
> e.g. `Log(logger, level, "{A}", someException, b, c, d, e)` — binds to it (attaching the
> exception), while a plain typed call binds to the **zero-array** `Log<T0…Tn>` overload. Typed
> overloads now reach **T0…T15** (16 args) via source generator (v1.0.4).

</details>

<details>
<summary><b>Flush (drain buffering sinks)</b></summary>

<br>

```csharp
// A buffering/batching sink registers how to drain itself; FlushAsync awaits them all.
void RegisterFlush  (Func<CancellationToken, Task> flush);
bool UnregisterFlush(Func<CancellationToken, Task> flush);
Task FlushAsync     (CancellationToken cancellationToken = default); // 0-alloc no-op when none registered
```

</details>

<details>
<summary><b>Global settings</b></summary>

<br>

```csharp
static Func<Exception, string, bool, string> ExceptionFormatter { get; set; }
static Func<LogLevel, string, Exception, bool> AsyncSinkFilter { get; }
static int MaxTemplateCacheEntries { get; set; }   // default 10,000; stops caching when limit is hit

static void UseAsyncSinkProvider(Func<LogLevel, string, Exception, bool> filter);
static Task FlushAsync(CancellationToken cancellationToken = default);  // real flush of RegisterFlush callbacks; Task.CompletedTask when none registered
static void ShutdownUtcTimer();  // no background timer to stop; forces one final lazy-refresh for deterministic teardown
```

</details>

---

## 🔬 How it works

```mermaid
flowchart LR
    A["logger.WriteInformation(...)"] --> B{"Level enabled?"}
    B -- No --> C["return immediately<br/><b>0 allocations · &lt;1 ns (0–8 args)</b>"]
    B -- Yes --> D["wrap args in a<br/>stack-only struct<br/><b>no array</b>"]
    D --> E["ILogger pipeline<br/>named props + OriginalFormat"]
    style C fill:#1f6f3d,color:#fff
    style D fill:#0d4f8b,color:#fff
```

A few deliberate design choices do all the work:

- **Strongly-typed overloads beat `params`.** A call with one to **sixteen** arguments binds to a
  generic overload in *normal form* (1–5 hand-written, 6–16 source-generated), which the C# compiler
  prefers over the *expanded* params form. No array is ever created at the call site.
- **`IsEnabled` is checked first, always.** When a level is off, the method returns before any
  argument is touched — so a disabled call boxes nothing and allocates nothing.
- **Stack-only log state.** Arguments are wrapped in a `readonly struct` that implements
  `IReadOnlyList<KeyValuePair<string, object>>`, so structured sinks still get named properties
  and the `{OriginalFormat}` template — without a heap allocation for the carrier.
- **Span-based rendering for plain templates.** A `{Name}`-only template (no `:format`/`,align`
  suffix) renders through a stack-allocated `Span<char>`, copying out exactly one final string —
  no `StringBuilder`, no pool, no array. Anything with a format suffix, an argument-count
  mismatch, or output too large for the stack buffer falls back to the original `string.Format`
  path, so existing rendered output never changes.
- **Cached everything.** Parsed templates, `EventId`s, and a pooled `StringBuilder` are shared
  process-wide. A per-thread reference check skips the template dictionary lookup entirely when
  the same call site fires repeatedly. The UTC timestamp used in exception reports is refreshed
  lazily on read (compared against `Environment.TickCount64`) instead of via a background timer
  that used to tick every millisecond for the life of the process.
- **Identity formatter for no-arg paths.** Plain static messages use `static (s, _) => s` — the
  same trick Microsoft uses internally — bypassing all intermediate state construction.
- **Hardened formatting.** A template/argument mismatch falls back to the raw template, and a throwing `ToString()` on a typed argument is isolated. Statically `object`-typed arguments bind to `params` and still allocate; `object[]`-path rendering is owned by Microsoft.Extensions.Logging.
- **Static analysis closes the remaining gap.** `Nilog.Analyzers` flags interpolated-string
  templates at compile time — the one mistake that silently defeats every optimization above.

---

## 🏭 Production readiness

| Concern               | Nilog answer                                                                                                                                                                                   |
| --------- | ------------- |
| Thread safety         | All public members are thread-safe; shared state uses `volatile`, `Interlocked`, and `ConcurrentDictionary`                                                                                    |
| Trimming / Native AOT | No reflection — fully compatible with `PublishTrimmed` and `PublishSingleFile`                                                                                                                 |
| Memory growth         | Template cache warns and stops caching new entries once `MaxTemplateCacheEntries` is reached                                                                                                   |
| Idle CPU cost         | No background timer — the UTC timestamp cache refreshes lazily on read, only when an exception is actually formatted                                                                           |
| Process shutdown      | A final UTC refresh runs automatically on `ProcessExit` / `DomainUnload`; call `ShutdownUtcTimer()` for deterministic teardown (kept for compatibility — there is no timer to dispose anymore) |
| Formatting robustness | Bad template or arg-count mismatch falls back to the raw template; throwing `ToString()` on typed args is isolated; null arguments render as `(null)` like MEL                                 |
| Sink compatibility    | Structured state (`IReadOnlyList<KVP>`) and `{OriginalFormat}` work with Console, Serilog, OpenTelemetry, Seq, Application Insights                                                            |
| Supported frameworks  | .NET 8, 9, 10                                                                                                                                                                                  |

---

## 🔒 Thread safety and lifecycle

- **Every public member is thread-safe.** The shared state — template cache, pooled
  `StringBuilder`, compiled delegates, cached timestamp — is immutable or concurrency-safe.
- **No reflection.** Nilog is friendly to **trimming** and **Native AOT**.
- **No background timer.** The UTC timestamp cache refreshes lazily when read, not on a
  schedule — there is nothing running, and therefore nothing to leak, while the process is idle.
  A final refresh runs automatically on `ProcessExit` and `DomainUnload`. Call
  `Nilogger.ShutdownUtcTimer()` yourself only for deterministic teardown (it is idempotent).
- **Global settings are process-wide.** `ExceptionFormatter` and `AsyncSinkFilter` affect the
  whole process; set them once at startup.

---

## ❓ FAQ

<details>
<summary><b>Does Nilog replace my logging framework?</b></summary>

No. Nilog is a set of extension methods on `ILogger`. Your provider/sink (Console, Serilog, NLog,
OpenTelemetry, Seq, App Insights, …) stays exactly as it is — Nilog just changes the call site.
</details>

<details>
<summary><b>Why <code>Write*</code> and not <code>Log*</code>?</b></summary>

`LogInformation`, `LogError`, etc. are already taken by Microsoft's own `ILogger` extensions.
Re-using those names would cause ambiguous-call compile errors in any file that imports both
namespaces. `Write*` keeps Nilog unambiguous and lets you use both side by side.
</details>

<details>
<summary><b>Is it really zero allocation?</b></summary>

On the **disabled path**, yes — measured at **0 bytes** and ~2–3 ns for **0–16** typed args
(asserted as exactly `0L` by the test suite). On the enabled path Nilog still allocates the
rendered message string (every logger must), but avoids the `object[]` and any extra carrier —
measurably less than the conventional extensions at 1–6 args (e.g. 104 B vs 152 B at 3 args), **equal at 9 args** (376 B each), and still more than `LoggerMessage.Define`/`[LoggerMessage]` (48 B at 3 args). A single scope allocates ~80 B.
</details>

<details>
<summary><b>What about 9+ arguments?</b></summary>

Typed overloads now reach **sixteen** arguments (1–5 hand-written, 6–16 source-generated). A
9-arg disabled call allocates **0 B** (asserted by `DisabledPath_NineTypedArgs_AllocatesZeroBytes`);
a 9-arg enabled call is **equal to the conventional extension** in the v1.0.6 measurement (376 B each; `[LoggerMessage]` allocates 104 B). Only at **17+** args does Nilog fall back to
`params object[]` — the same as the framework. For hot paths with many fields, consider splitting
extra context into a `WriteScope` typed scope instead of a single long template.
</details>

<details>
<summary><b>Is it AOT / trimming safe?</b></summary>

Yes — and **compiler-enforced**. `Nilog.Core` sets `<IsAotCompatible>true</IsAotCompatible>`, so the
trim/AOT/single-file analyzers run on every build and (with warnings-as-errors in Release) fail the
build on any unsafe construct. No reflection; the Native AOT compiler emits native code from
`Nilog.dll` with zero warnings.
</details>

<details>
<summary><b>Do structured properties still reach my sink?</b></summary>

Yes. Named holes become structured properties and the original template is preserved as
`{OriginalFormat}`, identical to the framework's templated logging. See
[Structured logging, end to end](#-structured-logging-end-to-end).
</details>

<details>
<summary><b>What does <code>Nilog.Analyzers</code> actually check?</b></summary>

Eight rules, full parity with the SerilogAnalyzer set: **NILOG001** (interpolated `$"..."` template,
with a one-click code fix), **NILOG002** (placeholder/argument count mismatch), **NILOG003**
(concatenated or `string.Format` template), **NILOG004** (duplicate named placeholder),
**NILOG005** (positional `{0}` instead of named, Info), **NILOG006** (an exception passed as a
template value), **NILOG007** (malformed template — unclosed/empty placeholder), and **NILOG008**
(non-PascalCase placeholder name, Info) — across every `Write*`/`Nilogger.Log` call shape. The
checks are syntax/semantics-based at the call site; interpolation hidden behind a local
(`var msg = $"..."; logger.Write(msg)`) would need data-flow analysis and is out of scope.
See [Static analysis](#-static-analysis-niloganalyzers).
</details>

---

## 🛠️ Build, test, benchmark

```bash
# Build everything (net8.0 / net9.0 / net10.0)
dotnet build -c Release

# Run the unit tests (252 core tests + 66 analyzer tests = 318 unique tests,
# executed on net8/9/10 = 954 target-framework executions, all green)
dotnet test

# See every feature in action — a real-world e-commerce checkout walkthrough
dotnet run -c Release --project Nilog.Demo -f net10.0

# Reproduce the benchmarks above (all 21 classes)
dotnet run -c Release --project Nilog.Benchmark -f net10.0
#   ...or one suite:  -- --filter "*HighArityExtendedBenchmarks*"
#   ...or faster:     -- --job short
```

**Project layout**

| Project                  | What it is                                                                                                                                                                               |
| --------- | ------------ |
| `Nilog.Core`             | the library (packs as `Nilog`)                                                                                                                                                           |
| `Nilog.SourceGenerators` | build-time generator that emits the 6–16 arg zero-array overloads into `Nilog.dll`                                                                                                       |
| `Nilog.Tests`            | xUnit suite, 252 tests per target framework (net8/9/10) (incl. real-pipeline interop, allocation gate, typed scope tests)                                                                |
| `Nilog.Analyzers`        | Roslyn analyzer package — `NILOG001`–`NILOG008` + a NILOG001 code fix, opt-in, not referenced by `Nilog.Core`                                                                            |
| `Nilog.Analyzers.Tests`  | xUnit suite for the analyzer + code fix, 66 tests per target framework (net8/9/10)                                                                                                       |
| `Nilog.Demo`             | runnable, commented tour — every feature against a real-world checkout scenario                                                                                                          |
| `Nilog.Function`         | Azure Functions (isolated worker) sample — middleware correlation scopes, typed 3-pair scope, config-based levels, the analyzer wired in at build time, real telemetry flush on shutdown |
| `Nilog.Benchmark`        | BenchmarkDotNet suites — 21 classes covering every surface area                                                                                                                          |

---

## 🗺️ Roadmap

**✅ Shipped in v1.0.4:**

- ✅ Source-generated typed overloads extended to **16 arguments** — 9-arg disabled: 0 B (v1.0.4 figure; re-measured v1.0.6: ~2.8 ns / 0 B)
- ✅ **Typed multi-pair scope overloads** — `WriteScope<T1,T2>`, `WriteScope<T1,T2,T3>`, `WriteScope<T1,T2,T3,T4>` with readonly `TwoScope`/`ThreeScope`/`FourScope` backing structs
- ✅ **Compact exception report** — `moreDetailsEnabled: false` now allocates **< 300 B** (down from ≈ 992 B); allocation gate test added
- ✅ Allocation gate extended — `DisabledPath_NineTypedArgs_AllocatesZeroBytes` + `ExceptionBasicReport_AllocatesBelow300Bytes`
- ✅ Benchmark suite expanded — `TemplateCacheBenchmarks`, `TypedScopeBenchmarks`, `ValueVsReferenceArgBenchmarks`, debugger guard

**✅ Shipped in v1.0.3:**

- ✅ Source-generated typed overloads to **8 arguments** (zero array on the disabled path)
- ✅ Four more analyzer rules — `NILOG002`–`NILOG008` — plus a one-click code fix for `NILOG001`
- ✅ `Nilog.Analyzers` shipped as a standalone, development-dependency NuGet package
- ✅ **Real `FlushAsync`** for buffering sinks via `RegisterFlush`/`UnregisterFlush`
- ✅ Compiler-enforced Native AOT / trimming (`IsAotCompatible=true`; removed a real `Exception.TargetSite` trim hazard)
- ✅ Fixed `Nilogger.Log` 5–8 args silently allocating; benchmark/allocation CI gate added

**🔭 Considering next:**

- [ ] First-class async/batching sink built on `AsyncSinkFilter` + `RegisterFlush`
- [ ] Code fixes for `NILOG002`/`NILOG003`, and data-flow analysis for interpolation hidden behind a local
- [ ] Optional `ActivitySource`/OpenTelemetry correlation helpers

**⛔ Decided against:** an `ILogger`-free static sink — it would fork the API and undermine the
"true drop-in `ILogger`" design.

✅ Shipped in v1.0.2: 5-arg typed overloads, `Nilog.Analyzers` (NILOG001), span-based template
rendering, lazy UTC timestamp cache, per-thread template lookup fast path.

See [CHANGELOG.md](CHANGELOG.md) for released changes.

---

## 🤝 Contributing

Issues and pull requests are welcome. If Nilog saves your hot path some garbage, a ⭐ on the repo
goes a long way.

## 📄 License

**Nilog** built with care by **Gehan Fernando** — MIT Licensed

<div align="center"><sub>Built for developers who count their allocations. ⚡</sub></div>
