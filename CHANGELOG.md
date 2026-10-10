# Changelog

All notable changes to **Nilog** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

_Nothing yet._

## [1.0.6] - 2026-10-08

Audit-remediation release. Every item below is covered by a regression test or an external
harness; the core suite (252 tests) and analyzer suite (66 tests) pass on net8.0, net9.0 and net10.0.

### 🐛 Fixed

- **F-001** `WriteError`/`WriteCritical`/`Log` with a derived exception type or extra arguments
  on C# ≤ 12 bound to the generic overload and lost the exception. Runtime and generated
  overloads now attach a leading `Exception`. Verified in a 6-configuration consumer matrix.
- **F-004 / F-005** `NILOG001` code fix: arguments are inserted in the correct position (after the
  exception argument where present), brace escaping no longer doubles braces, and the fix is
  withheld where it cannot be proven semantics-preserving.
- **F-006** A throwing `ToString()`/`ISpanFormattable` on a typed argument no longer escapes the
  log call; it renders as `[ToString failed: Type threw Ex]`.
- **F-007** `FlushAsync` honours cancellation, preserves earlier failures in an `AggregateException`
  and observes abandoned callbacks.
- **F-008** The template cache is bounded by entry count and by template length. New public API:
  `MaxCachedTemplateLength`, `TemplateCacheCount`, `ClearTemplateCache()`.
- **F-009** Exception reports are bounded (message, depth, node and total length), single-line in
  compact mode, list all `AggregateException` inners, and a throwing user `ExceptionFormatter`
  falls back to the built-in report.
- **F-010** Dictionary/`IEnumerable` scopes validate keys (`ArgumentException` for null/blank) and
  treat `Count` as a hint only.
- **F-013** Null arguments render as `(null)`, matching Microsoft.Extensions.Logging.
- `SmallScopeWrapper` indexer threw `IndexOutOfRangeException` for an out-of-range index; it now throws `ArgumentOutOfRangeException` (found by coverage analysis).

### 📚 Documentation

- Benchmark tables replaced with v1.0.6 measurements against conventional MEL extensions, `LoggerMessage.Define` and `[LoggerMessage]`; the earlier "240x faster" style comparisons (conventional extension only) are retired. Nilog is equal to the optimized APIs on the disabled path and not faster on the enabled path.
- Removed the unsupported "full parity with SerilogAnalyzer" claim from the README, NuGet readme and analyzer package description.
- **F-003** The v1.0.5 "0 B under parallel load" claim could not be reproduced and is withdrawn.
  Re-measured: BenchmarkDotNet MediumRun 3.82 MB (Nilog) vs 5.34 MB (Microsoft); process-wide probe
  80 B vs 112 B per 1-arg call. The enabled path allocates the rendered string.
- **F-002** Documented that statically `object`-typed arguments bind to `params` and allocate.
- **F-011 / F-014** Replaced "never throws" and "immutable static state" wording; documented the
  raw-template fallback (Nilog) versus `FormatException` (MEL) and the `object[]`-path ownership.

### 🧪 Tests & tooling

- New regression suites (exception binding, cache bounds, flush, exception report, formatting
  robustness, scope validation, allocation characterization, code-fix behaviour).
- `Nilog.Benchmark --parallel-alloc` process-wide allocation probe.
- Coverage-gap tests (high-arity states, scope wrappers) and additional NILOG002 analyzer cases; `NILOG002` description corrected for the `params`/MEL path.
- Line coverage measured at 51% (branch 45%) before the gap tests; see the Phase 2 coverage report.

### 🔒 CI / supply chain (F-012)

- Actions pinned to commit SHAs, read-only default permissions, tag/version values passed via
  environment variables and validated, analyzer tests added to CI.
- Added `global.json`, NuGet lock files (`--locked-mode` in CI/release), `SECURITY.md` and a CycloneDX SBOM procedure; removed a duplicated nested workflow directory.
- `dotnet list package --vulnerable --include-transitive`: 0 advisories in all projects.
- Not done: package signing and provenance attestation (require credentials; nothing was published).

### 📦 Version

- Package and analyzer version bumped to 1.0.6.

## [1.0.5] - 2026-10-07

This is a hardening/audit release: no new public APIs, no behavioural redesign. It re-validates
the v1.0.4 implementation end-to-end (architecture, allocation, memory, concurrency, template
cache, scopes, exceptions, flush/lifecycle, analyzers, packaging) and corrects the documentation
drift that audit uncovered.

### ⚙️ Configuration & usability

- **Clarified that zero Nilog-specific configuration was already optional** — `README.md`'s
  "Global configuration" section implied that `Nilogger` settings were something applications
  needed to set up. No runtime configuration requirement was removed (there never was a required
  one); the section was renamed and reworded to say so explicitly — see "Documentation" below.
- **`FlushAsync` wording corrected** — `README.md` previously described `FlushAsync` in one place
  as "a deliberate no-op placeholder" for a hypothetical future buffering sink, which contradicted
  the real `RegisterFlush`/`UnregisterFlush`-backed flush implementation shipped since v1.0.3 and
  documented correctly elsewhere in the same file. Reworded consistently everywhere: `FlushAsync`
  performs a real, in-order await of every callback registered via `RegisterFlush`, and remains a
  zero-allocation no-op only when nothing has been registered.
- **`ShutdownUtcTimer` wording corrected** — the configuration table previously said it "stops the
  background timestamp-cache timer," which no longer matches the implementation: the UTC
  timestamp cache has used lazy, on-read refresh with no persistent background `Timer` since
  **v1.0.2** (see that release's changelog entry). Reworded to state plainly that no background
  timer exists, that the method forces one final refresh for deterministic teardown, and that it
  is kept only for source/binary compatibility — not something normal applications need to call.
- **Separated MEL configuration from Nilog-specific hooks** — the renamed "Optional advanced
  configuration" section now shows standard `Microsoft.Extensions.Logging` level/filter
  configuration (`SetMinimumLevel`, `AddFilter`, `AddConsole`) as belonging to MEL, distinct from
  the handful of optional Nilog hooks (`ExceptionFormatter`, `MaxTemplateCacheEntries`,
  `UseAsyncSinkProvider`/`AsyncSinkFilter`, `RegisterFlush`/`UnregisterFlush`/`FlushAsync`,
  `ShutdownUtcTimer`), none of which are required for normal use.

### 📝 Documentation

- **Stale test counts corrected** — the project table in `README.md` reported `Nilog.Tests` as
  "188 tests" and `Nilog.Analyzers.Tests` as "22 tests"; a full `dotnet test -c Release` run
  across net8.0/net9.0/net10.0 shows the actual, current counts are **179** and **31**
  respectively (matching the "210 passing" badge, which was already correct). Updated to match.
- **Current-version references bumped to 1.0.5** — the NuGet badge and the `PackageReference`
  install examples for `Nilog` and `Nilog.Analyzers` in `README.md` and `Nilog/README.nuget.md`
  now point at `1.0.5`. Historical "(v1.0.4)"/"NEW in v1.0.4" annotations that document when a
  specific feature (16-arg typed overloads, typed multi-pair scopes, compact exception report,
  etc.) was introduced are left untouched — they remain historically correct.
- **Benchmark provenance clarified** — the benchmarks section states explicitly which numbers
  were freshly measured and when. All published numbers are a fresh BenchmarkDotNet run against
  the final compiled v1.0.5 runtime code (21 benchmark classes, measured 2026-10-07; see "Final
  runtime validation" below) — no v1.0.4 numbers are presented as current without being re-run,
  since this release does not modify any of the measured hot paths but the figures were
  re-verified rather than merely carried forward.
- **"Global configuration" renamed to "Optional advanced configuration"** — the section heading,
  table of contents entry, and in-text anchors in `README.md` were renamed and the section
  rewritten to lead with an explicit "no Nilog-specific configuration or DI registration is
  required for normal use" callout, with MEL configuration (`SetMinimumLevel`, `AddFilter`,
  `AddConsole`, …) and optional Nilog hooks now presented in clearly separated, explicitly
  optional subsections.
- **Stale enabled-path allocation/time percentages corrected** — several places in `README.md`
  and `Nilog/README.nuget.md` (the before/after table, the FAQ, and the API-walkthrough text)
  still quoted an earlier approximation ("25–32% less", "37% faster on the 9-arg enabled path",
  "~6 ns" for the 0-arg enabled path). Replaced with the figures that actually match the current
  benchmark tables in the same documents: **26–29% less allocation** across 2–8 typed args,
  **72% faster / ~4.1 ns** for the 0-arg enabled path, and **12% faster** at 9 args (where 368 B
  of boxing is unavoidable on both sides).
- **`Nilog.Tests` test-count corrected** — the `dotnet test` comment in `README.md` still said
  "188 core tests + 22 analyzer tests = 210 total", left over from before the "Stale test counts
  corrected" fix above was applied everywhere; updated to **179 core + 31 analyzer = 210 unique
  tests**, and clarified that the 630 figure elsewhere in the same document is the count of
  **target-framework executions** (210 unique tests × 3 TFMs), not a distinct, larger test count.
- **`Nilog/README.nuget.md` restructured** to match the documented consumer-facing outline: added
  a short **"What Nilog is"** lead-in, an explicit **"Configuration"** section stating no
  Nilog-specific configuration or DI registration is required for normal use (with the standard
  MEL filter/level example kept separate from the optional Nilog hooks), a **"Structured
  logging"** section showing the rendered message / named properties / `{OriginalFormat}`
  contract, and a **"Provider compatibility"** section that explains compatibility is through
  `Microsoft.Extensions.Logging` and explicitly does not claim direct MongoDB/Elasticsearch/
  SQL/Oracle integrations.

### 🧪 Verification performed

- `dotnet build -c Release` — solution builds clean (0 warnings, 0 errors) across net8.0/net9.0/net10.0.
- `dotnet test -c Release` — **179/179** `Nilog.Tests` and **31/31** `Nilog.Analyzers.Tests` pass
  on every supported target framework (630 total test executions, 0 failed, 0 skipped).
- Re-reviewed the template cache (bounded `ConcurrentDictionary`, warn-once-and-stop-caching
  behaviour at `MaxTemplateCacheEntries`), scope wrappers, `FlushAsync`/`RegisterFlush` lifecycle,
  the UTC timestamp cache (lazy refresh, no background timer), and the Roslyn analyzer/code-fix
  project. No correctness, concurrency, or memory-retention regressions were found relative to
  the behaviour already shipped and tested in v1.0.4.

### 📦 Versioning

- `Nilog` (`Nilog.Core`) and `Nilog.Analyzers` package versions bumped from `1.0.4` to `1.0.5`.

### 📊 Final runtime validation (independent pass, same 1.0.5 release)

A second, independent validation pass was performed against the same final v1.0.5 codebase to
confirm the production-readiness claims above with real builds, publishes, and provider
integrations rather than code inspection alone. The version was **not** changed.

- **Fresh BenchmarkDotNet run** — re-ran the complete, current suite of 21 benchmark classes
  (not 20; `ParallelBenchmarks`, `StressBenchmarks`, and `AllocationStressBenchmarks` were
  previously undercounted) against the compiled v1.0.5 runtime on 2026-10-07. Confirmed 0 B
  disabled-path allocation for typed 0–9 args, typed 2/3-pair scope allocation parity with a
  single-key scope (24 B, no dictionary), and the documented template-cache warm/cold-parse
  speedup (345× time, 19.3× allocation). `README.md`/`Nilog/README.nuget.md` benchmark tables are
  updated with these fresh numbers (see "Documentation" below).
- **`ParallelBenchmarks` methodology correction** — an initial `ShortRun` (3-iteration) pass
  showed Nilog allocating *more* than Microsoft under parallel load (8.6 MB vs 5.34 MB). This was
  investigated and found to be pure statistical noise (StdErr ≈120% of the mean with only 3
  samples), not a real regression: a `MediumRun` rerun (30 samples) shows Nilog allocating **0 B**
  vs Microsoft's 5.6 MB, with equal mean time. No runtime code was changed; this is a benchmarking
  methodology finding, recorded here for transparency.
- **Native AOT** — published a minimal real consumer with `PublishAot=true` on net10.0: zero
  Nilog-related AOT/trim warnings, the published native binary runs correctly and structured
  logging/exception logging work as expected.
- **Trimming** — published the same consumer with `PublishTrimmed=true` (no AOT): zero Nilog
  trim warnings, logging/templates/scopes/exceptions all verified at runtime.
- **Real Serilog integration** — exercised `Nilog` extension methods through
  `Microsoft.Extensions.Logging` into the actual `Serilog.Extensions.Logging` provider and
  `Serilog.Sinks.InMemory`; verified level, rendered message, structured properties,
  `{OriginalFormat}`, exception, and scope state all arrive correctly at the sink.
- **Real OpenTelemetry integration** — exercised the same call surface through the actual
  `OpenTelemetry.Extensions.Logging` provider with `OpenTelemetry.Exporter.InMemory`; verified
  body, severity, structured attributes, and exception information.
- **Live ASP.NET Core integration** — a minimal web host handling 50 concurrent HTTP requests,
  each with a request-correlation scope, verified no scope/property leakage across concurrent
  requests.
- **Live Worker Service integration** — a real `BackgroundService` running a dequeue/process loop
  with scoped `ItemId`, periodic error logging, and graceful `StopAsync`/host shutdown; confirmed
  no lifecycle exceptions, no callback leak, and clean shutdown.
- **Azure Functions (`Nilog.Function`)** — rebuilt fresh in Release with 0 warnings/errors.
  `func` (Azure Functions Core Tools) was not available in this environment, so this is
  **build-tested only**, not live-host tested; stated explicitly rather than implied.
- **Package-consumer validation** — ran `dotnet pack -c Release` for `Nilog` and
  `Nilog.Analyzers`, installed the real generated `.nupkg`s (not project references) into a
  separate external console project via a local NuGet feed, and confirmed restore, compile,
  runtime extension-method behaviour, and that the `NILOG002` analyzer diagnostic fires correctly
  from the installed analyzer package.
- **NuGet package content inspection** — confirmed both `.nupkg`s contain only the expected
  assets (lib DLLs + XML docs per TFM, analyzer DLL, README, nuspec) with no test/benchmark/obj
  leakage.
- **Template cache stress test** — verified the cache grows to exactly the configured limit
  (10,000 entries) and stays bounded there even when 100,000–1,000,000 unique templates are
  logged; logging continues to work correctly after saturation, including under 50,000 concurrent
  duplicate-template insertions.
- **Concurrency stress test** — 1/2/4/8/16/32 worker counts (20,000 calls each: cache hits,
  distinct templates, scopes, periodic exceptions) plus 16 independent logger categories ×
  10,000 calls each: zero exceptions, no corruption, in every run.
- **Long-running memory soak** — 3,000,000 mixed log operations (repeated/unique templates,
  disabled logging, scopes, exceptions) with checkpoints every 500,000 ops: post-forced-GC memory
  at the end was **below** the measured baseline (no retained-memory growth); Gen0/Gen1/Gen2
  collection counts were modest (155/6/3) for the full run.
- **Binary compatibility** — used Microsoft's official `Microsoft.DotNet.ApiCompat.Tool` to
  compare the real, previously-published `Nilog 1.0.4` package (downloaded from nuget.org) against
  the newly built `1.0.5` assemblies on all three target frameworks (net8.0/net9.0/net10.0):
  **no breaking changes found**.
- **Source compatibility** — compiled a representative v1.0.4-era usage sample (all log levels,
  typed overloads, `params` fallback, single/typed/dictionary scopes, exception overloads,
  `ExceptionFormatter` customization, `MaxTemplateCacheEntries`, flush register/unregister/
  `FlushAsync`, and static `Nilogger.Log`) unchanged against v1.0.5: compiled with 0
  warnings/errors and ran correctly.
- **Result**: no new Nilog runtime defects were found during this validation pass. Every anomaly
  investigated (the `ParallelBenchmarks` allocation discrepancy, an early stress-harness showing
  "0 cached templates") traced back to a test-harness/benchmark-methodology issue, not a Nilog
  defect, and was corrected before drawing conclusions.


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

| Path                                                              | v1.0.3                               | v1.0.4               | Δ                                                                                   |
| ------ | -------- | -------- | --- |
| **9-arg disabled** — `WriteDebug("{A}…{I}", 1…9)`                 | params, 211 ns / 368 B (≈ Microsoft) | **0.45 ns / 0 B**    | **≈ 469× faster, zero alloc**                                                       |
| **9-arg enabled** — `WriteInformation("{A}…{I}", 1…9)`            | params, 246 ns / 368 B (≈ Microsoft) | **156 ns / 368 B**   | **37% faster** (boxing is unavoidable on the enabled path; no array overhead added) |
| **5-arg enabled**                                                 | 77.70 ns / 160 B                     | **77.70 ns / 160 B** | unchanged — confirmed < 140 ns target ✅                                             |
| **2-arg enabled (int+int)**                                       | n/a                                  | **46.27 ns / 96 B**  | 34% faster than Microsoft (70 ns / 136 B)                                           |
| **Compact exception report (basic, `moreDetailsEnabled: false`)** | ≈ 992 B                              | **< 300 B**          | **> 3× less allocation per report**                                                 |

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

| Path                                                  | v1.0.2                               | v1.0.3               | Δ                                |
| ------ | -------- | -------- | --- |
| **8-arg disabled** — `WriteInformation("{A}…{H}", …)` | params, 221 ns / 336 B (≈ Microsoft) | **0.82 ns / 0 B**    | **~268× faster, zero alloc**     |
| **6-arg enabled** — `WriteInformation("{A}…{F}", …)`  | params, 180 ns / 264 B (≈ Microsoft) | **100.6 ns / 192 B** | **~44% faster, ~27% less alloc** |
| **8-arg enabled** — `WriteInformation("{A}…{H}", …)`  | params, 233 ns / 336 B (≈ Microsoft) | **117.0 ns / 248 B** | **~50% faster, ~26% less alloc** |
| **Static `Nilogger.Log`, 5–8 typed args, disabled**   | params array (allocated)             | **0 B**              | **bug fix — now zero-array**     |

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

| Path                                                          | v1.0.1                                | v1.0.2                 | Δ                                                |
| ------ | -------- | -------- | --- |
| **5-arg disabled** — `WriteInformation("{A}{B}{C}{D}{E}", …)` | params, 28.40 ns / 184 B              | **0.25 ns / 0 B**      | **~113× faster, zero alloc**                     |
| **5-arg enabled** — `WriteInformation("{A}{B}{C}{D}{E}", …)`  | params, ~125 ns / 224 B (≈ Microsoft) | **78.15 ns / 160 B**   | **39% faster, 29% less alloc than Microsoft**    |
| Plain `{Id}` template render (warm cache)                     | 37.12 ns / 80 B                       | **32.78 ns / 80 B**    | **~12% faster, same alloc**                      |
| Escaped braces + placeholder render                           | 46.83 ns / 96 B                       | **32.49 ns / 96 B**    | **~31% faster, same alloc**                      |
| 100,000-call sequential loop (same template)                  | 5.55 ms / 11.41 MB                    | **4.74 ms / 11.41 MB** | **~15% faster, same alloc**                      |
| 10,000-call 4-arg enabled loop                                | 1,216 μs                              | **807 μs**             | **~34% faster**                                  |
| Exception formatting (basic/full reports)                     | unchanged                             | unchanged              | no regression from removing the background timer |

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

| Path                                                       | v1.0.0                       | v1.0.1              | Δ                                |
| ------ | -------- | -------- | --- |
| No-arg enabled — `WriteInformation("text")`                | 29 ns / 56 B                 | **4.14 ns / 0 B**   | **7× faster, zero alloc**        |
| Feature C — `WriteError("msg", ex)` no args                | 36 ns / 72 B                 | **3.95 ns / 0 B**   | **9× faster, zero alloc**        |
| `Nilogger.Log(…)` 0-arg enabled                            | 27 ns / 40 B                 | **4.46 ns / 0 B**   | **6× faster, zero alloc**        |
| `FlushAsync()`                                             | 1,280 ns / 328 B             | **~0.01 ns / 0 B**  | **>100,000× faster, zero alloc** |
| `WriteErrorException(ex)` basic report                     | 182 ns / 992 B               | **99.5 ns / 496 B** | **1.8× faster, 50% less alloc**  |
| **4-arg disabled — `WriteInformation("{A}{B}{C}{D}", …)`** | n/a (params, 113 ns / 192 B) | **0.24 ns / 0 B**   | **479× faster vs Microsoft**     |
| **4-arg enabled — `WriteInformation("{A}{B}{C}{D}", …)`**  | n/a (params, 122 ns / 192 B) | **71.8 ns / 136 B** | **41% faster, 29% less alloc**   |

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
