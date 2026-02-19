using System.Linq.Expressions;
using DatabricksPoc.Domain.Entities;
using DatabricksPoc.Domain.Models;

namespace DatabricksPoc.Application.Projections;

/// <summary>
/// Reusable LINQ projection expressions for Linq2DB.
///
/// All members are Expression<> fields (not Func<>) so Linq2DB generates
/// a narrow SQL SELECT instead of SELECT *.
///
/// Navigation property access (p.Category.Name) is translated to a LEFT JOIN
/// by Linq2DB via the Association mappings in DatabricksDataConnection.
/// No .Invoke() or LINQKit needed — Linq2DB has its own expression expansion.
///
/// ── Tags in ToDetail ─────────────────────────────────────────────────────────
/// p.Tags.Select(t => t.Tag).ToArray() inside a projection Expression is a
/// collection sub-select that does not translate to Spark SQL. Tags are loaded
/// via a separate targeted query in the repository and merged in-memory via
/// WithTags(). Everything else in ToDetail runs server-side.
/// </summary>
public static class ProductProjections
{
    // ── Summary projection (list / search) ───────────────────────────────────
    // No collection navigation. Translates cleanly to SQL.

    public static readonly Expression<Func<Product, ProductSummaryDto>> ToSummary =
        p => new ProductSummaryDto
        {
            ProductId = p.ProductId,
            Sku = p.Sku,
            Name = p.Name,
            Price = p.Price,
            Stock = p.Stock,
            CategoryName = p.Category.Name   // → LEFT JOIN categories
        };

    // ── Detail projection (server-side scalar fields + category join) ─────────
    // p.Category.Name / .Slug → Linq2DB generates LEFT JOIN via Association.
    // Tags omitted — filled in-memory by WithTags() after a second query.

    public static readonly Expression<Func<Product, ProductDetailDto>> ToDetail =
        p => new ProductDetailDto
        {
            ProductId = p.ProductId,
            Sku = p.Sku,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            Stock = p.Stock,
            IsActive = p.IsActive,
            CategoryName = p.Category.Name,   // → LEFT JOIN
            CategorySlug = p.Category.Slug,   // → LEFT JOIN
            Tags = Array.Empty<string>(),      // filled in-memory after second query
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };

    /// <summary>
    /// Merges Tags (loaded via AsSplitQuery) into an already-projected ProductDetailDto.
    /// Called in-memory in the repository after the two-part query completes.
    /// Everything else in the DTO was projected server-side.
    /// </summary>
    public static ProductDetailDto WithTags(ProductDetailDto dto, string[] tags)
        => new ProductDetailDto
        {
            ProductId = dto.ProductId,
            Sku = dto.Sku,
            Name = dto.Name,
            Description = dto.Description,
            Price = dto.Price,
            Stock = dto.Stock,
            IsActive = dto.IsActive,
            CategoryName = dto.CategoryName,
            CategorySlug = dto.CategorySlug,
            Tags = tags,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt
        };
}

// =============================================================================
// COMPLEX MULTI-ENTITY AGGREGATION EXAMPLE
//
// Scenario: "Order line sales report" — joins Order → OrderLine → Product →
//   Category → Supplier, applies GROUP BY with SUM/COUNT/AVG aggregations,
//   and projects into a flat POCO.
//
// Everything stays IQueryable until .ToListAsync() after pagination.
// No premature materialisation. Sub-projections composed via .Invoke()
// are inlined by ExpressionExpander into one flat expression tree before
// the provider translates to SQL.
// =============================================================================

// ── Supporting DTOs ───────────────────────────────────────────────────────────

public record CategoryDto
{
    public long CategoryId { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
}

public record SupplierSummaryDto
{
    public long SupplierId { get; init; }
    public string CompanyName { get; init; } = default!;
    public string Country { get; init; } = default!;
}

/// <summary>
/// The complex aggregated POCO — built from 4 joined entities, fully server-side.
/// </summary>
public record ProductSalesAggregateDto
{
    public long ProductId { get; init; }
    public string Sku { get; init; } = default!;
    public string ProductName { get; init; } = default!;
    public decimal UnitPrice { get; init; }

    // From Category join
    public string CategoryName { get; init; } = default!;
    public string CategorySlug { get; init; } = default!;

    // From Supplier join
    public string SupplierName { get; init; } = default!;
    public string SupplierCountry { get; init; } = default!;

    // Aggregated from OrderLines
    public int TotalUnitsSold { get; init; }
    public decimal TotalRevenue { get; init; }
    public int OrderCount { get; init; }
    public decimal AverageOrderSize { get; init; }
}

public static class ProductSalesProjections
{
    // ── Reusable leaf fragment ────────────────────────────────────────────────

    public static readonly Expression<Func<Supplier, SupplierSummaryDto>> SupplierToSummary =
        s => new SupplierSummaryDto
        {
            SupplierId = s.SupplierId,
            CompanyName = s.CompanyName,
            Country = s.Country
        };

    // ── Composite aggregation projection ─────────────────────────────────────
    //
    // Source: IGrouping<long, OrderLine> from a GroupBy(ol => ol.ProductId)
    // on a query that has already joined OrderLine → Product → Category → Supplier.
    //
    // ExpressionExpander inlines:
    //   ProductProjections.CategoryToDto.Invoke(...)
    //   ProductSalesProjections.SupplierToSummary.Invoke(...)
    //
    // The provider receives a single flat SELECT with no Invoke nodes:
    //
    //   SELECT
    //     p.product_id, p.sku, p.name, p.price,
    //     c.name AS category_name, c.slug AS category_slug,
    //     s.company_name AS supplier_name, s.country AS supplier_country,
    //     SUM(ol.quantity)              AS total_units_sold,
    //     SUM(ol.quantity * ol.unit_price) AS total_revenue,
    //     COUNT(DISTINCT ol.order_id)   AS order_count,
    //     AVG(CAST(ol.quantity AS DECIMAL)) AS average_order_size
    //   FROM order_lines ol
    //   JOIN products    p ON ol.product_id = p.product_id
    //   JOIN categories  c ON p.category_id = c.category_id
    //   JOIN suppliers   s ON p.supplier_id = s.supplier_id
    //   GROUP BY ol.product_id, p.product_id, p.sku, p.name, p.price,
    //            c.name, c.slug, s.company_name, s.country

    public static readonly Expression<Func<IGrouping<long, OrderLine>, ProductSalesAggregateDto>> ToAggregate =
        g => new ProductSalesAggregateDto
        {
            ProductId = g.Key,
            Sku = g.First().Product.Sku,
            ProductName = g.First().Product.Name,
            UnitPrice = g.First().Product.Price,

            // Sub-expressions inlined by ExpressionExpander
            CategoryName = g.First().Product.Category.Name,
            CategorySlug = g.First().Product.Category.Slug,
            SupplierName = g.First().Supplier.CompanyName,
            SupplierCountry = g.First().Supplier.Country,

            // Aggregations — translated to SUM(), COUNT(), AVG() in SQL
            TotalUnitsSold = g.Sum(ol => ol.Quantity),
            TotalRevenue = g.Sum(ol => ol.Quantity * ol.UnitPrice),
            OrderCount = g.Select(ol => ol.OrderId).Distinct().Count(),
            AverageOrderSize = g.Average(ol => (decimal)ol.Quantity)
        };
}

// ── Repository usage example ──────────────────────────────────────────────────
//
//   public async Task<PagedResult<ProductSalesAggregateDto>> GetSalesAggregateAsync(
//       DateRange range, string? categorySlug, int page, int pageSize, CancellationToken ct)
//   {
//       // Build the fully server-side IQueryable — no ToList() until after pagination
//       IQueryable<ProductSalesAggregateDto> query = db.OrderLines
//           .Where(ol => ol.Order.PlacedAt >= range.From && ol.Order.PlacedAt <= range.To)
//           .Where(ol => categorySlug == null || ol.Product.Category.Slug == categorySlug)
//           .GroupBy(ol => ol.ProductId)
//           .Select(ProductSalesProjections.ToAggregate)  // ← expander inlines sub-expressions
//           .OrderByDescending(r => r.TotalRevenue);
//
//       int total = await query.CountAsync(ct);
//
//       var items = await query
//           .Skip((page - 1) * pageSize)
//           .Take(pageSize)
//           .ToListAsync(ct);    // ← first and only materialisation
//
//       return new PagedResult<ProductSalesAggregateDto> { ... };
//   }

// ── Stub entities (move to Domain/Entities/ in a real project) ────────────────

public class Supplier
{
    public long SupplierId { get; set; }
    public string CompanyName { get; set; } = default!;
    public string Country { get; set; } = default!;
}

public class Order
{
    public long OrderId { get; set; }
    public DateTime PlacedAt { get; set; }
}

public class OrderLine
{
    public long OrderLineId { get; set; }
    public long OrderId { get; set; }
    public long ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public Order Order { get; set; } = default!;
    public Product Product { get; set; } = default!;
    public Supplier Supplier { get; set; } = default!;
}
