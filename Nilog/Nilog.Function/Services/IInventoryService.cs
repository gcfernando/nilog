// -----------------------------------------------------------------------------
//  Nilog.Function — inventory abstraction used by the checkout flow.
//
//  File        : IInventoryService.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Nilog.Function.Models;

namespace Nilog.Function.Services;

public interface IInventoryService
{
    /// <summary>Reserves stock for an order, or throws <see cref="OutOfStockException"/>.</summary>
    void Reserve(Guid orderId, IReadOnlyList<OrderLine> items);

    /// <summary>Releases a previous reservation (e.g. after a failed payment).</summary>
    void Release(Guid orderId);
}
