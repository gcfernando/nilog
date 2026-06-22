// -----------------------------------------------------------------------------
//  Nilog benchmarks — BenchmarkDotNet host entry point that runs the suites
//  comparing Nilog with the framework's logging extensions.
//
//  File        : Program.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
// Nilog benchmark host.
//
//   Run everything:        dotnet run -c Release --project Nilog.Benchmark -f net10.0
//   Run one suite:         dotnet run -c Release --project Nilog.Benchmark -f net10.0 -- --filter *Disabled*
//   Faster (noisier) runs: append  --job short
//
// BenchmarkDotNet requires a Release build; a Debug run only prints a warning.

using BenchmarkDotNet.Running;

// Benchmarks must not run with an attached debugger — measurements are unreliable.
if (System.Diagnostics.Debugger.IsAttached)
    throw new InvalidOperationException(
        "Benchmarks must not run with an attached debugger. " +
        "Run with: dotnet run -c Release --project Nilog.Benchmark -- --filter \"*\" --join");

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
