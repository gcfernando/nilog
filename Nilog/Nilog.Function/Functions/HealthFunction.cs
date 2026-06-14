// -----------------------------------------------------------------------------
//  Nilog.Function — a liveness probe that doubles as a tour of the few Nilog
//  features the checkout flow doesn't naturally hit: the single-key scope
//  overload, literal braces in a template, and reading the async-sink filter.
//
//      GET /api/health
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
}
