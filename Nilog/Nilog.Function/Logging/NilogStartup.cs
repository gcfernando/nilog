// -----------------------------------------------------------------------------
//  Nilog.Function — process-wide Nilog configuration. These hooks are global, so
//  best practice is to set them exactly once, at startup, from Program.cs.
//
//  File        : NilogStartup.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nilog;

namespace Nilog.Function.Logging;

internal static class NilogStartup
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Configures Nilog's global hooks. Call once before the host starts.</summary>
    public static void Configure()
    {
        // 1. Exception formatter — render exceptions as single-line JSON so a structured
        //    sink (Application Insights, Seq, ELK) gets queryable fields instead of an
        //    opaque multi-line blob. WriteErrorException / WriteCriticalException use this.
        Nilogger.ExceptionFormatter = static (ex, title, verbose) => JsonSerializer.Serialize(
            new
            {
                title,
                type = ex.GetType().FullName ?? ex.GetType().Name,
                ex.Message,
                hresult = ex.HResult,
                inner = ex.InnerException?.Message,
                stack = verbose ? ex.StackTrace : null,
            },
            _json);

        // 2. Async-sink filter — a forward-looking hook telling a custom async/alerting
        //    pipeline which entries are worth shipping off-box. Here: Warning and above.
        //    The primary ILogger path is unaffected and still receives everything.
        Nilogger.UseAsyncSinkProvider(static (level, _, _) => level >= LogLevel.Warning);
    }
}
