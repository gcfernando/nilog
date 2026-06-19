// -----------------------------------------------------------------------------
//  Nilog.Function — a real-world Azure Functions (isolated worker) host showing
//  how to use Nilog end to end: process-wide setup at startup, a correlation
//  scope per invocation via middleware, structured logging in functions and
//  injected services, and a graceful flush on shutdown.
//
//  File        : Program.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nilog;
using Nilog.Function.Logging;
using Nilog.Function.Services;
using OpenTelemetry;
using OpenTelemetry.Trace;

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Minimum log level by build configuration: verbose (Debug) in development, lean
// (Information) in production. DEBUG is defined for Debug builds in Directory.Build.props.
#if DEBUG
builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

// 1. Configure Nilog's process-wide hooks exactly once, before the host starts.
NilogStartup.Configure();

// 2. Open a Nilog correlation scope around every invocation. Registering the
//    middleware here means functions and services never repeat correlation fields.
builder.UseMiddleware<RequestLoggingMiddleware>();

// 3. Domain services. They log through ILogger<T>, so they inherit the request scope.
builder.Services.AddSingleton<IInventoryService, InventoryService>();
builder.Services.AddSingleton<IPaymentGateway, PaymentGateway>();

// 4. Telemetry: ILogger (and therefore every Nilog call) flows into OpenTelemetry.
//    The Azure Monitor exporter is wired only when a connection string is present,
//    so the sample still runs locally with no Application Insights configured.
IOpenTelemetryBuilder otel = builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

string? appInsights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsights))
{
    _ = otel.UseAzureMonitorExporter();
}

IHost app = builder.Build();

// 5. Register how to drain telemetry so Nilogger.FlushAsync() actually flushes it (v1.0.3).
//    A buffering/batching sink registers a callback; Nilog awaits it on flush. Here we force
//    the OpenTelemetry trace pipeline to flush. With nothing registered FlushAsync is a no-op.
TracerProvider? tracerProvider = app.Services.GetService<TracerProvider>();
if (tracerProvider is not null)
{
    Nilogger.RegisterFlush(ct =>
    {
        _ = tracerProvider.ForceFlush();
        return Task.CompletedTask;
    });
}

// 6. Drain Nilog (which now awaits the registered telemetry flush) and stop its UTC
//    timestamp refresh on graceful shutdown, so buffered work is flushed and teardown
//    is deterministic.
IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(static () =>
{
    Nilogger.FlushAsync().GetAwaiter().GetResult();
    Nilogger.ShutdownUtcTimer();
});

await app.RunAsync();
