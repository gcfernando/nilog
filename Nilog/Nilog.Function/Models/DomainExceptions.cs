// -----------------------------------------------------------------------------
//  Nilog.Function — domain exceptions, the kind a real payments/inventory module
//  would define. They carry structured fields so logs stay queryable.
//
//  File        : DomainExceptions.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
namespace Nilog.Function.Models;

/// <summary>Thrown when a line cannot be reserved because stock is insufficient.</summary>
public sealed class OutOfStockException(string sku, int missingQuantity)
    : Exception($"SKU {sku} is short by {missingQuantity} unit(s).")
{
    public string Sku { get; } = sku;
    public int MissingQuantity { get; } = missingQuantity;
}

/// <summary>Thrown when the payment processor declines (or fails to reach) a charge.</summary>
public sealed class PaymentDeclinedException(string reason, Exception? inner = null)
    : Exception($"Payment declined ({reason}).", inner)
{
    public string Reason { get; } = reason;
}
