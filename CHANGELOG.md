# Changelog

All notable changes to **Nilog** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

_Nothing yet._

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

[Unreleased]: https://github.com/gcfernando/Nilog/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/gcfernando/Nilog/releases/tag/v1.0.0
