// -----------------------------------------------------------------------------
//  Nilog.Function — a stand-in inventory service. It logs through an injected
//  ILogger<T>, so every line automatically inherits the per-request scope opened
//  by RequestLoggingMiddleware (correlation id, order id, customer id, ...).
//
//  File        : InventoryService.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;
using Nilog;
using Nilog.Function.Models;

namespace Nilog.Function.Services;

public sealed class InventoryService(ILogger<InventoryService> logger) : IInventoryService
{
    private readonly ILogger<InventoryService> _logger = logger;

    public void Reserve(Guid orderId, IReadOnlyList<OrderLine> items)
    {
        foreach (OrderLine line in items)
        {
            // Convention for the demo: any SKU ending in "-OOS" is permanently out of stock.
            if (line.Sku.EndsWith("-OOS", StringComparison.OrdinalIgnoreCase))
            {
                throw new OutOfStockException(line.Sku, line.Quantity);
            }
        }

        // Typed, zero-allocation structured log (2 args).
        _logger.WriteInformation("Reserved {LineCount} line(s) for order {OrderId}", items.Count, orderId);
    }

    public void Release(Guid orderId) =>
        _logger.WriteWarning("Released inventory reservation for order {OrderId}", orderId);
}
