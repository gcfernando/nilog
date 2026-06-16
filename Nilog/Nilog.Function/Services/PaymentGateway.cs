// -----------------------------------------------------------------------------
//  Nilog.Function — a stand-in payment gateway. Demonstrates the Trace level, a
//  realistic nested exception chain (timeout -> declined) that the verbose
//  exception report can expand for the on-call engineer, and a retry-with-backoff
//  pattern for the transient failures a real payment processor call will hit.
//
//  File        : PaymentGateway.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Globalization;
using Microsoft.Extensions.Logging;
using Nilog;
using Nilog.Function.Models;

namespace Nilog.Function.Services;

public sealed class PaymentGateway(ILogger<PaymentGateway> logger) : IPaymentGateway
{
    private const int MaxAttempts = 3;
    private readonly ILogger<PaymentGateway> _logger = logger;

    public string Charge(Guid orderId, decimal amount, string currency, string cardLast4)
    {
        // Trace: the chattiest level, normally off in production. Costs nothing here
        // because the IsEnabled guard returns before any work is done.
        _logger.WriteTrace("Contacting payment processor for order {OrderId}", orderId);

        // Best practice: a transient failure (timeout, 503, connection reset) is worth
        // retrying with backoff; a business decline is not. Every retry attempt logs its
        // own structured Attempt/DelayMs - never just a bare "retrying..." string - so an
        // on-call engineer can see the whole attempt history in one query.
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return AttemptCharge(orderId, amount, currency, cardLast4, attempt);
            }
            catch (TimeoutException ex) when (attempt < MaxAttempts)
            {
                int delayMs = 200 * attempt;
                _logger.WriteWarning(
                    "Payment processor call for order {OrderId} timed out on attempt {Attempt}/{MaxAttempts}, retrying in {DelayMs} ms: {Reason}",
                    orderId, attempt, MaxAttempts, delayMs, ex.Message);
            }
        }

        // Final attempt: let the timeout propagate as-is rather than swallowing it, so the
        // caller's catch block (and the verbose exception report) still sees the real cause.
        return AttemptCharge(orderId, amount, currency, cardLast4, MaxAttempts);
    }

    private string AttemptCharge(Guid orderId, decimal amount, string currency, string cardLast4, int attempt)
    {
        // Convention for the demo: card ending 9000 simulates a transient gateway timeout
        // that resolves on the final attempt - exercises the retry path above without
        // looping forever. Card ending 0341 always declines outright - a business
        // decision, not a transient failure, so it is never retried.
        if (cardLast4 is "9000" && attempt < MaxAttempts)
        {
            throw new TimeoutException("processor gateway timed out after 8000 ms");
        }

        if (cardLast4 is "0341")
        {
            try
            {
                throw new TimeoutException("processor gateway timed out after 8000 ms");
            }
            catch (Exception transport)
            {
                throw new PaymentDeclinedException("insufficient_funds", transport);
            }
        }

        string auth = "AUTH-" + orderId.ToString("N", CultureInfo.InvariantCulture)[..8].ToUpperInvariant();
        _logger.WriteInformation("Charged {Amount:N2} {Currency} (auth {Auth})", amount, currency, auth);
        return auth;
    }
}
