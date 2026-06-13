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
- **Logging scopes** through `WriteScope(key, value)` and `WriteScope(dictionary)`, with
  an allocation-light path for small contexts (a single-pair scope allocates zero bytes).
- **Structured logging support**: named template properties and the `{OriginalFormat}` entry
  flow through to structured sinks via a stack-only `readonly struct` state.
- **Runtime template parser** with caching, supporting named placeholders, escaped braces
  (`{{`/`}}`), and alignment/format suffixes (`{Value,5}`, `{Value:000}`).
- **Allocation-free UTC timestamp cache** refreshed by a background timer, with
  `ShutdownUtcTimer()` for deterministic teardown (also wired to process exit automatically).
- **Forward-looking async hooks**: `AsyncSinkFilter`, `UseAsyncSinkProvider(filter)`, and
  `FlushAsync(cancellationToken)`.
- Ships as a single `Nilog.dll` exposing the `Nilog` namespace (`using Nilog;`), multi-targeting
  **.NET 8.0, 9.0, and 10.0**, with SourceLink, embedded symbols, and XML documentation.

[Unreleased]: https://github.com/gcfernando/Nilog/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/gcfernando/Nilog/releases/tag/v1.0.0
