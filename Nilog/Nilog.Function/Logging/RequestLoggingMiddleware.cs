// -----------------------------------------------------------------------------
//  Nilog.Function — worker middleware that wraps every invocation in a single
//  Nilog scope. Because the scope is opened here, EVERY log line produced during
//  the request (in functions and in injected services) automatically carries the
//  correlation id, function name, and invocation id — without repeating them.
//
//  File        : RequestLoggingMiddleware.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Nilog.Function.Logging;

public sealed class RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger) : IFunctionsWorkerMiddleware
{
    private const string CorrelationHeader = "X-Correlation-ID";
    private readonly ILogger<RequestLoggingMiddleware> _logger = logger;

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        HttpContext? http = context.GetHttpContext();

        // Honour an inbound correlation id (for distributed tracing across services),
        // otherwise fall back to the host-assigned invocation id.
        string correlationId =
            http?.Request.Headers[CorrelationHeader].FirstOrDefault() is { Length: > 0 } inbound
                ? inbound
                : context.InvocationId;

        // Echo it back so the caller can correlate their request with our logs.
        _ = http?.Response.Headers[CorrelationHeader] = correlationId;

        // A multi-key scope (<= 4 entries takes Nilog's allocation-light array path).
        Dictionary<string, object> scope = new()
        {
            ["CorrelationId"] = correlationId,
            ["Function"] = context.FunctionDefinition.Name,
            ["InvocationId"] = context.InvocationId,
        };

        using (_logger.WriteScope(scope))
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                await next(context);

                int status = http?.Response.StatusCode ?? 0;
                double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                // Severity decided at runtime from the HTTP outcome — Nilogger.Log's
                // typed (3-arg) overload keeps this allocation-free on every request.
                LogLevel level;

                if (status >= 500)
                {
                    level = LogLevel.Error;
                }
                else
                {
                    level = status >= 400 ? LogLevel.Warning : LogLevel.Information;
                }

                Nilogger.Log(_logger, level, "{Function} -> {Status} in {Elapsed:N1} ms",
                    context.FunctionDefinition.Name, status, elapsedMs);
            }
            catch (Exception ex)
            {
                // An exception escaped the function. Emit a full, verbose incident report
                // at Critical, then rethrow so the host still records the failure.
                _logger.WriteCriticalException(ex, title: "unhandled.function.exception", moreDetailsEnabled: true);
                throw;
            }
        }
    }
}
