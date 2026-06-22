# Changelog

All notable changes to **Nilog** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

_Nothing yet._

## [1.0.4] - 2026-06-22

### Added

- **Typed overloads extended to 16 arguments** — the source generator (`Nilog.SourceGenerators`)
  now emits zero-array `Write*`/`Nilogger.Log` overloads for **6–16 arguments** (raised from 6–8
  in v1.0.3), lifting the typed ceiling from 8 to **16**. A nine-argument call such as
  `WriteInformation("{A}…{I}", 1…9)` now binds to `WriteInformation<T0…T8>` instead of falling
  back to `params object[]` — so the disabled path allocates **0 bytes** and the enabled path
  carries no array. Measured: 9-arg disabled **0.45 ns / 0 B** vs Microsoft 211 ns / 368 B
  (**≈ 469× faster**); 9-arg enabled **156 ns / 368 B** vs Microsoft 246 ns / 368 B (**37%
  faster** — boxing is unavoidable on the enabled path; the struct itself adds nothing).

- **Typed multi-pair scope overloads** — three new `WriteScope` overloads that eliminate the
  dictionary allocation for the most common scope shapes:
  - `WriteScope<T1,T2>(key1, val1, key2, val2)` — backed by a stack-allocated `TwoScope` struct
  - `WriteScope<T1,T2,T3>(key1, val1, key2, val2, key3, val3)` — backed by `ThreeScope`
  - `WriteScope<T1,T2,T3,T4>(k1,v1, k2,v2, k3,v3, k4,v4)` — backed by `FourScope`

  All three surface a scope compatible with the standard MEL `ILoggerProvider` pipeline. The
  underlying readonly structs require no array copy — values are boxed only once each, the
  unavoidable minimum for `ILoggerFactory` interop.

- **Compact exception report (`moreDetailsEnabled: false`)** — `WriteErrorException` and
  `WriteCriticalException` with `moreDetailsEnabled: false` now render a compact single-line
  summary (`[Title] Type: Message (Source=…, HResult=…)`) that allocates **< 300 bytes** per
  call, down from ≈ 992 bytes in v1.0.3. The verbose multi-line report (`moreDetailsEnabled:
  true`) is unchanged. Guarded by a new allocation gate test.

- **`ExceptionBasicReport_AllocatesBelow300Bytes` allocation gate** — a new test in
  `AllocationGateTests` asserts that a single `WriteErrorException(ex, …, moreDetailsEnabled:
  false)` call allocates fewer than 300 bytes (after JIT warmup), using a `CaptureLogger` inner
  class that does not allocate during `Log`. Runs in CI Release to catch any regression.

- **`DisabledPath_NineTypedArgs_AllocatesZeroBytes` test** — added to `AllocationGateTests` to
  assert that a 9-typed-arg disabled call allocates exactly `0L`, covering the newly typed range.

- **Typed scope unit tests** — `TypedTwoPairScope_HasExpectedEntries`,
  `TypedThreePairScope_HasExpectedEntries`, and `TypedFourPairScope_HasExpectedEntries` added to
  `ScopeTests`, verifying key/value ordering, counts, and correct enumeration.

- **Benchmark additions and improvements**:
  - `TwoArgBenchmarks` — new `Enabled (int+int)` category proves the 2-arg enabled path is 34%
    faster than Microsoft when types match (46 ns vs 70 ns); the int+decimal delta (62 ns) is
    explained by decimal boxing being 24 B vs 16 B for int — the code path is identical.
  - `HighArityExtendedBenchmarks` — updated benchmark descriptions from "params" to "typed" to
    reflect the v1.0.4 source-generator change; confirms 9-arg disabled = **0.45 ns / 0 B**.
  - `TemplateCacheBenchmarks` — benchmarks the per-thread single-slot cache hit (`WarmCache`)
    and the full `ConcurrentDictionary` miss path (`ColdParse`).
  - `TypedScopeBenchmarks` — compares single-pair, typed 2-pair vs dict 2-pair, and typed 3-pair.
  - `ValueVsReferenceArgBenchmarks` — compares int, string, and mixed argument boxing cost.
  - **Debugger guard** in `Nilog.Benchmark/Program.cs` — aborts with a clear message if a
    managed debugger is attached, preventing benchmarks from running under the debugger and
    producing misleadingly slow numbers.

### Changed

- **`Nilog.Demo` updated**:
  - Section 3b comment corrected from "Nine or more values — the familiar params path" to "Nine
    values — still typed and zero-allocation (source-generated, 6–16 args)", reflecting that the
    source generator now covers 1–16 args and a 9-arg call binds to `WriteInformation<T0…T8>`.
  - Section 8 (scopes) updated to showcase the new typed `WriteScope<T1,T2/T3/T4>` overloads
    with 2-pair, 3-pair, and 4-pair examples alongside the `IReadOnlyDictionary` fallback.

- **`Nilog.Function` updated** — the per-request 3-entry dictionary scope in `OrdersFunction`
  replaced with `WriteScope("OrderId", orderId, "CustomerId", request.CustomerId, "Currency",
  request.Currency)` (typed `WriteScope<T1,T2,T3>`), eliminating the dictionary allocation for
  the correlation scope that wraps every checkout invocation.

- **`Nilog.Demo`, `Nilog.Function`, and `Nilog.Benchmark`** updated to reflect v1.0.4 changes.

### Performance

Measured with BenchmarkDotNet (ShortRun: 3 warmup + 3 measurement, Server GC), .NET 10.0,
Intel Core i7-13850HX:

| Path | v1.0.3 | v1.0.4 | Δ |
|------|--------|--------|---|
| **9-arg disabled** — `WriteDebug("{A}…{I}", 1…9)` | params, 211 ns / 368 B (≈ Microsoft) | **0.45 ns / 0 B** | **≈ 469× faster, zero alloc** |
| **9-arg enabled** — `WriteInformation("{A}…{I}", 1…9)` | params, 246 ns / 368 B (≈ Microsoft) | **156 ns / 368 B** | **37% faster** (boxing is unavoidable on the enabled path; no array overhead added) |
| **5-arg enabled** | 77.70 ns / 160 B | **77.70 ns / 160 B** | unchanged — confirmed < 140 ns target ✅ |
| **2-arg enabled (int+int)** | n/a | **46.27 ns / 96 B** | 34% faster than Microsoft (70 ns / 136 B) |
| **Compact exception report (basic, `moreDetailsEnabled: false`)** | ≈ 992 B | **< 300 B** | **> 3× less allocation per report** |

The 0–8-arg paths are unchanged from v1.0.3.

## [1.0.3] - 2026-06-19

### Added

- **Typed six-, seven-, and eight-argument overloads** — a new build-time source generator
  (`Nilog.SourceGenerators`) emits zero-array `WriteTrace`/`WriteDebug`/`WriteInformation`/
  `WriteWarning`/`WriteError`/`WriteCritical` and `Nilogger.Log` overloads for 6–8 arguments,
  lifting the typed ceiling from 5 to **8**. They are generated directly into `Nilog.dll`, so
  consumers pick them up with no extra reference. The disabled path allocates **0 bytes** for
  up to 8 typed arguments — asserted as exactly `0L` by
  `HighArityTests.DisabledPath_SixTypedArgs_AllocatesZeroBytes` / `…EightTypedArgs…` and
  confirmed by BenchmarkDotNet (8-arg disabled: **0.82 ns / 0 B** vs Microsoft 221 ns / 336 B).
  6+ arguments previously fell back to `params object[]`; that boundary is now **9+**.

- **Seven new `Nilog.Analyzers` rules** (1 → 8 total — full parity with the SerilogAnalyzer set):
  - **NILOG002** — the `{Placeholder}` count in a constant template does not match the number
    of arguments supplied.
  - **NILOG003** — the template is built with string concatenation (`"a" + b`) or
    `string.Format(...)`, which defeats the template cache and loses named properties.
  - **NILOG004** — the same named `{Placeholder}` appears more than once (silently collides on
    one structured-property key); numeric/positional `{0} {0}` reuse is intentionally allowed.
  - **NILOG005** (Info) — positional `{0}` placeholders instead of named `{Name}` ones.
  - **NILOG006** — an `Exception` passed as a template value instead of the exception parameter,
    which loses its type/message/stack as structured data.
  - **NILOG007** — a malformed template: an unclosed `{` or an empty `{}` placeholder.
  - **NILOG008** (Info) — a placeholder name that is not PascalCase (`{userId}` → `{UserId}`).

- **Code fix for NILOG001** — a one-click "Convert to a literal template with arguments"
  refactoring that rewrites `logger.WriteInformation($"User {id}")` into
  `logger.WriteInformation("User {id}", id)`, preserving `:format`/`,alignment` clauses and
  appending the extracted expressions at the correct trailing position for every call shape.

- **`Nilog.Analyzers` now ships as a standalone NuGet package** (`analyzers/dotnet/cs`),
  development-dependency only, so it never adds a runtime dependency to consumers.

- **Real `FlushAsync`** via `Nilogger.RegisterFlush(Func<CancellationToken, Task>)` /
  `UnregisterFlush(...)`. Buffering/batching sinks register how to drain themselves and
  `FlushAsync` awaits them all (every callback is attempted; failures surface together as an
  `AggregateException`). With nothing registered it stays a zero-allocation no-op, so existing
  callers are unaffected — turning the long-standing "FlushAsync is a no-op" limitation into a
  working flush.

- **`LoggingEngineInteropTests`** — end-to-end tests that run Nilog through the real
  `Microsoft.Extensions.Logging` `LoggerFactory` + `ILoggerProvider` pipeline (the exact
  contract every third-party engine integrates through), asserting the rendered message,
  `{OriginalFormat}`, named properties, exceptions, and level-filtering all survive intact.

- **Allocation regression gate** — a consolidated `AllocationGateTests` suite (1–8 typed args
  and the static `Log` path, all asserting `0L` on the disabled path) plus a GitHub Actions
  workflow (`.github/workflows/ci.yml`) that builds Release and runs the tests on every push/PR.

### Changed

- **`TemplateFormatter.Render` now covers up to 8 arguments**, so the generated 6–8 arg
  overloads render through the same stack-allocated `Span<char>` path as 1–5 args instead of
  building an `object?[]` in `ToString()`. Enabled-path effect vs Microsoft: 6-arg
  264 B → **192 B** (~27% less, ~44% faster) and 8-arg 336 B → **248 B** (~26% less, ~50%
  faster). `Format(params object?[])` remains only as the format-specifier/overflow fallback.

- **`Nilogger` is now a `partial` class** so the generated overloads compile into the same type.

- **Native AOT / trimming is now compiler-enforced** via `<IsAotCompatible>true</IsAotCompatible>`
  on `Nilog.Core`: the trim, single-file, and AOT analyzers run on every build and, with
  `TreatWarningsAsErrors` in Release, fail the build on any unsafe construct. The Native AOT
  compiler emits native code from `Nilog.dll` with zero warnings.

- **`Nilog.Demo`, `Nilog.Function`, and `Nilog.Benchmark` updated** to exercise 6–8 typed args,
  the 9-argument `params` escape hatch, real `FlushAsync`/`RegisterFlush`, and the four analyzer
  rules. Package/product version bumped to **1.0.3**.

### Fixed

- **`Nilogger.Log` with 5–8 args silently allocated.** The typed `Log<T0..Tn>` overloads carried
  `[OverloadResolutionPriority(-1)]`, which let the `params object[]` overload win for a normal
  (no-exception) call — so the static `Log` API allocated an array, contradicting the documented
  zero-array guarantee. Fixed by **promoting** the exception overload
  (`Log(ILogger, LogLevel, string, Exception, params object[])` → priority 1) instead of demoting
  the typed ones: a trailing `Exception` still binds correctly (regression-tested), while a plain
  typed call now binds to the zero-array overload. Discovered by the new allocation gate.

- **Trimming/AOT hazard removed.** Enabling the AOT analyzers surfaced two uses of
  `Exception.TargetSite` (`[RequiresUnreferencedCode]`) in the exception formatter. The
  `Target Site` line — redundant with the stack trace — was dropped, making the library
  genuinely trim/AOT-clean. (The only behavioural change is one fewer line in the exception
  report text.)

### Performance

Measured with BenchmarkDotNet (ShortRun: 3 warmup + 3 measurement, Server GC), .NET 10.0,
Intel Core i7-13850HX:

| Path | v1.0.2 | v1.0.3 | Δ |
|------|--------|--------|---|
| **8-arg disabled** — `WriteInformation("{A}…{H}", …)` | params, 221 ns / 336 B (≈ Microsoft) | **0.82 ns / 0 B** | **~268× faster, zero alloc** |
| **6-arg enabled** — `WriteInformation("{A}…{F}", …)` | params, 180 ns / 264 B (≈ Microsoft) | **100.6 ns / 192 B** | **~44% faster, ~27% less alloc** |
| **8-arg enabled** — `WriteInformation("{A}…{H}", …)` | params, 233 ns / 336 B (≈ Microsoft) | **117.0 ns / 248 B** | **~50% faster, ~26% less alloc** |
| **Static `Nilogger.Log`, 5–8 typed args, disabled** | params array (allocated) | **0 B** | **bug fix — now zero-array** |

The 0–5-arg paths are unchanged from v1.0.2 (already correct).

## [1.0.2] - 2026-06-16

### Added

- **Typed five-argument overloads** (`WriteTrace`, `WriteDebug`, `WriteInformation`,
  `WriteWarning`, `WriteError`, `WriteCritical`, and `Nilogger.Log` — all now accept a
  `<T0, T1, T2, T3, T4>` form). The zero-array, zero-boxing disabled path now covers
  **0–5 typed arguments**, not just 0–4. A disabled 5-arg call now allocates **0 bytes**
  (previously 184 B for the `params object[]` the compiler built before `IsEnabled` ever
  ran); an enabled 5-arg call is **39% faster and 29% less allocation** than the equivalent
  Microsoft call.

  Internally this required:
  - A new `LogState<T0, T1, T2, T3, T4>` readonly struct (indices 0–4 = arguments, index 5
    = `{OriginalFormat}`), mirroring the existing 0–4-arg structs.
  - A new `TemplateFormatter.Format`/`Render` overload accepting a fifth argument.
  - A private `Emit<T0,T1,T2,T3,T4>` helper and a `Log<T0,T1,T2,T3,T4>` static overload.
  - `[OverloadResolutionPriority(-1)]` on the new no-exception `WriteError`/`WriteCritical`
    5-arg overloads and on `Log<T0,T1,T2,T3,T4>`, so a call with a leading `Exception`
    argument still binds to the dedicated exception overload instead of the generic one
    (same guard pattern already used for the 3- and 4-arg no-exception overloads).
  - 6+ arguments still fall back to `params object[]`, unchanged.

- **`Nilog.Analyzers`** — a new, separate Roslyn analyzer package. Ships diagnostic
  **NILOG001**: warns when an interpolated string (`$"..."`) is passed as a Nilog message
  template, since each call then produces a different literal string, missing the template
  cache and growing it unboundedly, and the interpolated values never become named
  structured properties. The analyzer is opt-in — it is **not** referenced by `Nilog.Core`,
  so existing consumers are unaffected unless they explicitly add it. Covered by 6 new tests
  in `Nilog.Analyzers.Tests`.

### Changed

- **UTC timestamp cache (used by `WriteErrorException`/`WriteCriticalException`) no longer
  runs a background `Timer`.** Previously a `System.Threading.Timer` fired every millisecond
  for the entire process lifetime just to keep the cached timestamp fresh — paid even in
  processes that never log an exception. It now refreshes lazily on read: a reader compares
  `Environment.TickCount64` against the last refresh and only reformats if ≥ 1 ms has
  elapsed. Same effective freshness, zero idle-time cost. `ShutdownUtcTimer()` keeps its
  exact signature and idempotent behaviour for compatibility; it now simply forces a final
  refresh rather than disposing a timer.

- **Per-thread single-slot template cache.** `GetFormatter` now checks, via
  `[ThreadStatic]` fields, whether the template string is reference-equal to the last one
  that thread resolved (true for the common case of the same call site firing repeatedly in
  a loop, since message templates are almost always interned string literals) before
  falling through to the `ConcurrentDictionary` lookup. A miss costs nothing extra; a hit
  skips the dictionary probe entirely.

- **Plain-placeholder message rendering now uses a stack-allocated `Span<char>` instead of
  `string.Format`.** Templates with no `:format`/`,align` suffix render through a new
  `TemplateFormatter.Render` path that writes literal segments and `ISpanFormattable` values
  directly into a 256-char stack buffer, copying out exactly one final `string` — no
  `StringBuilder`, no pool, no array. Measured **~12–31% faster** with identical allocation
  on affected templates. Any template with a format specifier, an alignment suffix, an
  argument-count mismatch, or output that overflows the stack buffer transparently falls
  back to the original, byte-identical `string.Format` path, so no existing template's
  rendered output changes.

- **Sustained/repeated-template throughput improved** as the combined effect of the two
  changes above: a 100,000-call sequential loop reusing the same template dropped from
  5.55 ms to **4.74 ms** (now **33% faster than Microsoft**, up from 23%), with identical
  allocation (11.41 MB).

- **Test suite expanded to 144 tests** (was 132) across net8.0, net9.0, and net10.0, plus a
  new `Nilog.Analyzers.Tests` project (6 tests) for the analyzer package.

- **Benchmark suite updated (15 classes, was 14)** — new `FiveArgBenchmarks` class added for the
  5-arg typed path; `ParamsPathBenchmarks` (the true open-ended `params` escape hatch) bumped
  from 5 to 6 arguments now that 5 is typed; `DisabledAllArgsBenchmarks`'s "5 args (params)"
  category relabelled "5 args (typed)" with a new "6 args (params)" category added;
  `AllocationStressBenchmarks` extended with 5-arg disabled/enabled rows for both Nilog and
  Microsoft, and the stale "new in v1.1" labels removed.

- **`Nilog.Demo` and `Nilog.Function` updated** to reflect the new 5-arg boundary: stale comments
  describing 5 arguments as "the params path" were fixed, a 5-arg typed example was added to the
  demo, and `Nilog.Function` now references `Nilog.Analyzers` as a build-time analyzer
  (`OutputItemType="Analyzer"`) as a worked example of wiring it into a real project.

### Performance

Benchmarks run on .NET 10.0.8, Intel Core i7-13850HX @ 2.10 GHz, BenchmarkDotNet v0.15.8
(ShortRun: 3 warmup + 3 measurement iterations, Server GC):

| Path | v1.0.1 | v1.0.2 | Δ |
|------|--------|--------|---|
| **5-arg disabled** — `WriteInformation("{A}{B}{C}{D}{E}", …)` | params, 28.40 ns / 184 B | **0.25 ns / 0 B** | **~113× faster, zero alloc** |
| **5-arg enabled** — `WriteInformation("{A}{B}{C}{D}{E}", …)` | params, ~125 ns / 224 B (≈ Microsoft) | **78.15 ns / 160 B** | **39% faster, 29% less alloc than Microsoft** |
| Plain `{Id}` template render (warm cache) | 37.12 ns / 80 B | **32.78 ns / 80 B** | **~12% faster, same alloc** |
| Escaped braces + placeholder render | 46.83 ns / 96 B | **32.49 ns / 96 B** | **~31% faster, same alloc** |
| 100,000-call sequential loop (same template) | 5.55 ms / 11.41 MB | **4.74 ms / 11.41 MB** | **~15% faster, same alloc** |
| 10,000-call 4-arg enabled loop | 1,216 μs | **807 μs** | **~34% faster** |
| Exception formatting (basic/full reports) | unchanged | unchanged | no regression from removing the background timer |

Format-specifier and alignment templates, and the 0–4-arg disabled path, are unaffected —
they were already correct and use the same code as v1.0.1.

## [1.0.1] - 2026-06-15

### Added

- **Typed four-argument overloads** (`WriteTrace`, `WriteDebug`, `WriteInformation`,
  `WriteWarning`, `WriteError`, `WriteCritical`, and `Nilogger.Log` — all now accept a
  `<T0, T1, T2, T3>` form). The zero-array, zero-boxing disabled path now covers **0–4 typed
  arguments**, not just 0–3. A disabled call with four args is **479× faster** than the
  equivalent Microsoft call and allocates **0 bytes**; an enabled call is **41% faster** with
  **29% less allocation**.

  Internally this required:
  - A new `LogState<T0, T1, T2, T3>` readonly struct implementing
    `IReadOnlyList<KeyValuePair<string, object?>>` (indices 0–3 = arguments, index 4 =
    `{OriginalFormat}`), mirroring the existing 1–3-arg structs.
  - A new `TemplateFormatter.Format(object, object, object, object)` overload.
  - A private `Emit<T0,T1,T2,T3>` helper that boxes arguments only on the enabled path.

- **Typed no-exception overloads for `WriteError` and `WriteCritical`** (1–4 arguments).
  `logger.WriteError("Validation failed for {UserId}", userId)` now resolves to a
  zero-allocation strongly-typed overload instead of falling back to `params object[]`.
  Same applies to `WriteCritical`.

  Overload resolution is controlled with C# 13's `[OverloadResolutionPriority]` attribute:

  | Overload | Priority |
  |---|---|
  | `WriteError(message, Exception)` and with-exception typed variants | 0 (default) |
  | `WriteError<T0>(message, T0)` — typed no-exception | -1 |
  | `WriteError(message, params object[])` — params fallback | -2 |

  A polyfill for `OverloadResolutionPriorityAttribute` is included for .NET 8 targets
  (the C# 13 compiler recognises it by full name regardless of assembly).

- **`Nilogger.MaxTemplateCacheEntries`** — a configurable ceiling on the number of parsed
  templates held in the `ConcurrentDictionary` cache (default: 10,000). Once the limit is
  reached, new templates are still parsed correctly on every call but are **not stored**, so
  callers who accidentally log interpolated strings (e.g. `$"User {id} logged in"`) can no
  longer grow the cache without bound. A one-time `Debug.WriteLine` warning fires on the
  first overflow. The overflow check happens before `GetOrAdd`, so no dictionary entry is
  created and immediately evicted.

  ```csharp
  // Tighten the limit for memory-constrained environments (optional):
  Nilogger.MaxTemplateCacheEntries = 1_000;
  ```

- **`FourArgTests.cs`** — 12 new xUnit tests covering: render correctness for all six levels
  at four arguments, disabled-path zero-allocation CI regression guard
  (`GC.GetAllocatedBytesForCurrentThread` over 10,000 calls), structured property names and
  `{OriginalFormat}` for the 4-arg path, exception attachment for `WriteError` /
  `WriteCritical` with and without an exception, and `MaxTemplateCacheEntries` overflow
  behaviour (still renders correctly beyond the limit).

### Fixed

- **No-arg enabled path was 4.8× slower than Microsoft** (`WriteInformation("text")` and all
  zero-argument overloads). Root cause: all no-arg log calls were routed through
  `LoggerMessage.Define<string>(level, id, "{Message}")` which internally called
  `string.Format("{Message}", message)` on every enabled call — copying the string for no
  reason and allocating 56 B each time. Replaced with the identity formatter
  `static (s, _) => s` — the same approach Microsoft uses internally. No intermediate string
  copy, no allocation.

- **Exception formatter called `GetType()` twice per exception line.** Both
  `FormatExceptionMessageInternal` and `AppendInnerExceptionDetails` evaluated
  `ex.GetType().FullName ?? ex.GetType().Name` in a single expression, firing two virtual
  dispatches when `FullName` was non-null. Cached to a local `Type` variable in both methods.

- **`ScopeWrapper.ToString()` and `SmallScopeWrapper.ToString()` allocated a fresh
  `StringBuilder` on every call.** Text sinks invoke `ToString()` on each scope per log entry;
  both classes were doing `new StringBuilder(…)` each time. Both now borrow from the
  process-wide `_sbPool` (`ObjectPool<StringBuilder>`) in a try/finally block.

### Changed

- **`FlushAsync` simplified to a true no-op.** The method now returns `Task.CompletedTask`
  directly — no `async` keyword, no state machine, no `Task.Yield`, no cancellation check.
  Since there is no buffered sink today there is nothing to flush, and the overhead of
  `Task.Yield` was misleading. Measured cost: **~0.01 ns / 0 B** (below BDN noise floor).
  `FlushAsyncCore` was removed. Any code that awaited `FlushAsync()` at shutdown continues
  to work identically; it simply returns synchronously.

- **Typed no-exception `WriteError` / `WriteCritical` params fallbacks** annotated
  `[OverloadResolutionPriority(-2)]` so they are only selected when no typed overload applies.
  Existing callers with 5+ arguments are unaffected.

- **Benchmark suite updated** — class 5 renamed to `FourArgBenchmarks` (4-arg typed path),
  new class 5b `ParamsPathBenchmarks` (5-arg true params fallback), class 6
  `DisabledAllArgsBenchmarks` extended with 4-arg typed and 5-arg params rows, class 10
  `RuntimeLevelBenchmarks` extended with `Log<T0,T1,T2,T3>` enabled/disabled cases, class 11
  `FlushBenchmarks` simplified to non-async `Task`-returning methods with a `Baseline`,
  class 14 `AllocationStressBenchmarks` extended with 4-arg disabled (0 B) and enabled rows.

- **README** updated with expert-reviewed improvements: `## ⚠️ Limitations` section (honest
  accounting of where allocation-freedom stops), `## 🏭 Production readiness` table, ASCII bar
  chart updated with the 4-arg row, stress-test table extended, conservative benchmark wording
  ("in this benchmark, X was Y× faster"), FlushAsync description updated to "returns
  `Task.CompletedTask` directly", Best Practices table updated to ≤ 4 named holes, API reference
  extended with 4-arg overloads and `MaxTemplateCacheEntries`, Roadmap updated.

- **NuGet README** (`README.nuget.md`) rewritten with all new benchmark figures, `Limitations`
  section, `Production readiness` table, and 4-arg overload documentation.

- **Test suite expanded to 132 tests** (was 120) across net8.0, net9.0, and net10.0.

### Performance

Benchmarks run on .NET 10.0.8, Intel Core i7-13850HX @ 2.10 GHz, BenchmarkDotNet v0.15.8
(ShortRun: 3 warmup + 3 measurement iterations, Server GC):

| Path | v1.0.0 | v1.0.1 | Δ |
|------|--------|--------|---|
| No-arg enabled — `WriteInformation("text")` | 29 ns / 56 B | **4.14 ns / 0 B** | **7× faster, zero alloc** |
| Feature C — `WriteError("msg", ex)` no args | 36 ns / 72 B | **3.95 ns / 0 B** | **9× faster, zero alloc** |
| `Nilogger.Log(…)` 0-arg enabled | 27 ns / 40 B | **4.46 ns / 0 B** | **6× faster, zero alloc** |
| `FlushAsync()` | 1,280 ns / 328 B | **~0.01 ns / 0 B** | **>100,000× faster, zero alloc** |
| `WriteErrorException(ex)` basic report | 182 ns / 992 B | **99.5 ns / 496 B** | **1.8× faster, 50% less alloc** |
| **4-arg disabled — `WriteInformation("{A}{B}{C}{D}", …)`** | n/a (params, 113 ns / 192 B) | **0.24 ns / 0 B** | **479× faster vs Microsoft** |
| **4-arg enabled — `WriteInformation("{A}{B}{C}{D}", …)`** | n/a (params, 122 ns / 192 B) | **71.8 ns / 136 B** | **41% faster, 29% less alloc** |

The 1–3-arg disabled and enabled paths are unchanged from v1.0.0 (they were already correct).

## [1.0.0] - 2026-06-13

Initial public release. Zero-allocation, high-performance logging extensions for
`Microsoft.Extensions.Logging`.

### Added

- **Strongly-typed log overloads** for one to three arguments (`WriteTrace`, `WriteDebug`,
  `WriteInformation`, `WriteWarning`, `WriteError`, `WriteCritical`) that avoid the
  `params object[]` allocation and allocate nothing when the level is disabled.
- **`params` fallback overloads** for four or more arguments, preserving familiar usage.
- **Static `Nilogger.Log(...)` API** for logging when the level is decided at runtime.
- **Exception reporting** via `WriteErrorException` and `WriteCriticalException`, with an
  optional verbose mode that includes the stack trace and a bounded walk of inner and
  `AggregateException` branches.
- **Pluggable `ExceptionFormatter`** for customising how exceptions are rendered; assigning
  `null` restores the built-in formatter.
- **Logging scopes** through `WriteScope(key, value)` and `WriteScope(dictionary)`, with an
  allocation-light path for small contexts (the scope object itself is allocation-free).
- **Structured logging support**: named template properties and the `{OriginalFormat}` entry
  flow through to structured sinks via a stack-only `readonly struct` state.
- **Runtime template parser** with caching, supporting named placeholders, escaped braces
  (`{{`/`}}`), and alignment/format suffixes (`{Value,5}`, `{Value:000}`).
- **Allocation-free UTC timestamp cache** refreshed by a background timer, with
  `ShutdownUtcTimer()` for deterministic teardown (also wired to process exit automatically).
- **Forward-looking async hooks**: `AsyncSinkFilter`, `UseAsyncSinkProvider(filter)`, and
  `FlushAsync(cancellationToken)`.
- **`Write*` method naming** chosen deliberately to avoid ambiguous-call conflicts with the
  framework's own `Log*` extensions, so both can be imported side by side.
- **Thread-safe and Native AOT / trimming friendly**: immutable or concurrency-safe shared
  state and no reflection.
- Ships as a single `Nilog.dll` exposing the `Nilog` namespace (`using Nilog;`), multi-targeting
  **.NET 8.0, 9.0, and 10.0**, with SourceLink, embedded symbols, and XML documentation.

### Performance

Highlights from the included BenchmarkDotNet suites (.NET 8.0.27, AMD Ryzen AI 9 365;
see the README for full tables and methodology):

- **Disabled log call:** `0.48 ns` / **0 B** vs the framework's `142 ns` / `208 B` — ~297× faster,
  zero allocation.
- **Enabled, 1 argument:** `56 ns` / `80 B` vs `72 ns` / `112 B` (~22% faster, ~29% less memory).
- **Enabled, 3 arguments:** `72 ns` / `104 B` vs `121 ns` / `152 B` (~40% faster, ~32% less memory).
- **100,000-entry loop:** `7.0 ms` / `11.4 MB` vs `9.7 ms` / `15.2 MB` (~1.38× faster, ~25% less RAM).
- **50,000 logs across all cores:** ~29% less allocated memory.

### Documentation & tooling

- Comprehensive README with benchmark figures/graphs, a feature comparison, recipes
  (ASP.NET Core, worker services, Serilog sink), a migration guide, a full API reference, and a FAQ.
- Runnable, fully commented demo (`Nilog.Demo`) that tours every feature.
- xUnit test suite (`Nilog.Tests`) — 81 tests passing on net8.0 / net9.0 / net10.0.
- BenchmarkDotNet project (`Nilog.Benchmark`) covering enabled, disabled, exception, scope,
  parallel, and stress scenarios.
- XML documentation on every public member, consistent file headers, and centralised analyzer
  suppressions for the deliberate hot-path trade-offs.

[Unreleased]: https://github.com/gcfernando/Nilog/compare/v1.0.4...HEAD
[1.0.4]: https://github.com/gcfernando/Nilog/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/gcfernando/Nilog/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/gcfernando/Nilog/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/gcfernando/Nilog/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/gcfernando/Nilog/releases/tag/v1.0.0
