// -----------------------------------------------------------------------------
//  Nilog.Function — two probes in one file: a liveness check (GET /api/health,
//  "is the process up") and a readiness check (GET /api/health/ready, "can the
//  process serve traffic right now"). The readiness check is the realistic
//  pattern: measure each dependency, log the structured result either way, and
//  let the response status (200 vs 503) drive your orchestrator's behaviour -
//  never bury degraded-but-not-down state in an unstructured text message.
//
//      GET /api/health
//      GET /api/health/ready
//
//  File        : HealthFunction.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Nilog.Function.Functions;

public sealed class HealthFunction(ILogger<HealthFunction> logger)
{
    // A liveness probe should never depend on anything that can fail - if this handler
    // can't run, the process can't run. Keep it cheap and dependency-free.
    private static readonly TimeSpan DegradedThreshold = TimeSpan.FromMilliseconds(200);

    private readonly ILogger<HealthFunction> _logger = logger;

    [Function("Health")]
    public IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        // The single key/value scope overload (the lightest of the three scope shapes).
        using (_logger.WriteScope("Probe", "liveness"))
        {
            // Literal braces: "{{status}}" renders as a literal "{status}", which is handy
            // when a log line needs to contain JSON or code snippets verbatim.
            _logger.WriteInformation("Health template {{status}} resolves to {Status}", "healthy");

            // The async-sink filter is a readable property; surface the current policy.
            bool shipsWarnings = Nilogger.AsyncSinkFilter(LogLevel.Warning, string.Empty, null!);
            _logger.WriteDebug("Async sink ships Warning and above? {Enabled}", shipsWarnings);
        }

        return new OkObjectResult(new { status = "healthy", utc = DateTime.UtcNow });
    }

    [Function("HealthReady")]
    public IActionResult Ready(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/ready")] HttpRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);

        using (_logger.WriteScope("Probe", "readiness"))
        {
            (bool healthy, TimeSpan elapsed) = PingWarehouseDatabase();

            if (!healthy)
            {
                // Warning, not Error: a slow dependency is a degradation an alert should
                // surface, not an incident a human needs to be paged for at 3 a.m. The
                // exact latency is a structured field, not folded into the message text,
                // so it's queryable across every instance without parsing strings.
                _logger.WriteWarning(
                    "Readiness check degraded: warehouse-db responded in {ElapsedMs} ms (threshold {ThresholdMs} ms)",
                    elapsed.TotalMilliseconds, DegradedThreshold.TotalMilliseconds);

                return new ObjectResult(new { status = "degraded", elapsedMs = elapsed.TotalMilliseconds })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable,
                };
            }

            _logger.WriteInformation("Readiness check passed: warehouse-db responded in {ElapsedMs} ms",
                elapsed.TotalMilliseconds);
            return new OkObjectResult(new { status = "ready", elapsedMs = elapsed.TotalMilliseconds });
        }
    }

    // Stands in for a real dependency ping (a lightweight SELECT 1-style query, measured
    // with Stopwatch in production). Reports a latency above the threshold every third
    // call, so the degraded path above is exercised without needing a real flaky dependency.
    private static (bool Healthy, TimeSpan Elapsed) PingWarehouseDatabase()
    {
        TimeSpan elapsed = Random.Shared.Next(3) == 0 ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(15);
        return (elapsed < DegradedThreshold, elapsed);
    }
}
