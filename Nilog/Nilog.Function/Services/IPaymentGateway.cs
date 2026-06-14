// -----------------------------------------------------------------------------
//  Nilog.Function — payment abstraction used by the checkout flow.
//
//  File        : IPaymentGateway.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
namespace Nilog.Function.Services;

public interface IPaymentGateway
{
    /// <summary>Charges the card and returns an authorization code, or throws on decline.</summary>
    string Charge(Guid orderId, decimal amount, string currency, string cardLast4);
}
