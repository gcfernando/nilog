// -----------------------------------------------------------------------------
//  Nilog.Function — a stand-in payment gateway. Demonstrates the Trace level and
//  a realistic nested exception chain (timeout -> declined) that the verbose
//  exception report can expand for the on-call engineer.
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
    private readonly ILogger<PaymentGateway> _logger = logger;

    public string Charge(Guid orderId, decimal amount, string currency, string cardLast4)
    {
        // Trace: the chattiest level, normally off in production. Costs nothing here
        // because the IsEnabled guard returns before any work is done.
        _logger.WriteTrace("Contacting payment processor for order {OrderId}", orderId);

        // Convention for the demo: the card ending 0341 always declines, wrapping the
        // underlying transport failure so the inner-exception walk has something to show.
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
