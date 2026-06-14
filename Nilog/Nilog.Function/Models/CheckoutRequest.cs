// -----------------------------------------------------------------------------
//  Nilog.Function — request/response contracts for the checkout API.
//
//  File        : CheckoutRequest.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
namespace Nilog.Function.Models;

/// <summary>
/// The JSON body posted to <c>POST /api/orders</c>. Kept deliberately small so the
/// sample stays focused on how it is <em>logged</em>, not on domain modelling.
/// </summary>
public sealed class CheckoutRequest
{
    public int CustomerId { get; set; }
    public string Currency { get; set; } = "EUR";
    public string CardLast4 { get; set; } = string.Empty;
    public string Carrier { get; set; } = "DHL";
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public List<OrderLine> Items { get; set; } = [];

    /// <summary>Cheap, allocation-free validation; returns the first problem found.</summary>
    public bool IsValid(out string? error)
    {
        if (CustomerId <= 0)
        {
            error = "customerId must be a positive integer.";
            return false;
        }

        if (Items.Count == 0)
        {
            error = "at least one line item is required.";
            return false;
        }

        if (CardLast4.Length != 4)
        {
            error = "cardLast4 must be exactly 4 digits.";
            return false;
        }

        error = null;
        return true;
    }
}

/// <summary>A single line on the cart.</summary>
public sealed class OrderLine
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
