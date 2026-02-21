using DatabricksPoc.Application.Projections;
using DatabricksPoc.Application.Search;
using DatabricksPoc.Domain.Models;
using DatabricksPoc.Domain.Repositories;
using DatabricksPoc.Infrastructure.Context;
using LinqToDB.Async;

namespace DatabricksPoc.Application.Repositories;

/// <summary>
/// ProductRepository backed by Linq2DB.
///
/// ITable&lt;T&gt; is IQueryable&lt;T&gt; — the query patterns are identical to EF Core:
///   .Where().Select().OrderBy().Skip().Take().ToListAsync()
///
/// Key differences from EF Core:
///   - Expression&lt;&gt; sub-projections work natively. Linq2DB's own query
///     translator expands expression variables inline. LINQKit not needed.
///   - Tags: same two-query pattern. Collection sub-selects inside projections
///     are a SQL limitation, not a Linq2DB limitation.
///   - No .Include() — Linq2DB generates JOINs from Association mappings
///     when projections reference navigation properties directly.
/// </summary>
public class ProductRepository(DatabricksDataConnection db) : IProductRepository
{
    // ─────────────────────────────────────────────────────────────────────────
    // Detail reads
    //
    // Phase 1: project scalar fields + category join server-side
    // Phase 2: fetch tags with second targeted query
    // Phase 3: merge in-memory via WithTags()
    //
    // Linq2DB generates the Category LEFT JOIN from the Association mapping
    // when the projection references p.Category.Name directly.
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ProductDetailDto?> GetByIdAsync(long productId, CancellationToken ct = default)
    {
        var dto = await db.Products
            .Where(p => p.ProductId == productId)
            .Select(ProductProjections.ToDetail)
            .FirstOrDefaultAsync(ct);

        if (dto is null) return null;

        var tags = await db.ProductTags
            .Where(t => t.ProductId == productId)
            .Select(t => t.Tag)
            .ToArrayAsync(ct);

        return ProductProjections.WithTags(dto, tags);
    }

    // Single SQL:
    // SELECT p.*, c.name, c.slug, pt.tag
    // FROM products p
    // LEFT JOIN categories c ON p.category_id = c.category_id
    // LEFT JOIN product_tags pt ON p.product_id = pt.product_id
    // WHERE p.product_id = @productId
    public async Task<ProductDetailDto?> GetByIdSingleQueryAsync(long productId, CancellationToken ct = default)
    {
        var rows = await db.Products
            .Where(p => p.ProductId == productId)
            .SelectMany(
                p => db.ProductTags.Where(t => t.ProductId == p.ProductId).DefaultIfEmpty(),
                (p, t) => new
                {
                    p.ProductId,
                    p.Sku,
                    p.Name,
                    p.Description,
                    p.Price,
                    p.Stock,
                    p.IsActive,
                    p.CreatedAt,
                    p.UpdatedAt,
                    CategoryName = p.Category.Name,
                    CategorySlug = p.Category.Slug,
                    Tag = (string?)t.Tag
                })
            .ToListAsync(ct);

        if (rows.Count == 0) return null;

        var first = rows[0];
        return new ProductDetailDto
        {
            ProductId = first.ProductId,
            Sku = first.Sku,
            Name = first.Name,
            Description = first.Description,
            Price = first.Price,
            Stock = first.Stock,
            IsActive = first.IsActive,
            CreatedAt = first.CreatedAt,
            UpdatedAt = first.UpdatedAt,
            CategoryName = first.CategoryName,
            CategorySlug = first.CategorySlug,
            Tags = rows.Where(r => r.Tag is not null).Select(r => r.Tag!).ToArray()
        };
    }

    public async Task<ProductDetailDto?> GetBySkuAsync(string sku, CancellationToken ct = default)
    {
        var dto = await db.Products
            .Where(p => p.Sku == sku)
            .Select(ProductProjections.ToDetail)
            .FirstOrDefaultAsync(ct);

        if (dto is null) return null;

        var tags = await db.ProductTags
            .Where(t => t.ProductId == dto.ProductId)
            .Select(t => t.Tag)
            .ToArrayAsync(ct);

        return ProductProjections.WithTags(dto, tags);
    }

    public async Task<IReadOnlyList<ProductDetailDto>> GetByCategoryAsync(
        string categorySlug, CancellationToken ct = default)
    {
        var dtos = await db.Products
            .Where(p => p.Category.Slug == categorySlug && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(ProductProjections.ToDetail)
            .ToListAsync(ct);

        if (dtos.Count == 0) return dtos;

        var productIds = dtos.Select(d => d.ProductId).ToArray();
        var tagsByProduct = await db.ProductTags
            .Where(t => productIds.Contains(t.ProductId))
            .GroupBy(t => t.ProductId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(t => t.Tag).ToArray(), ct);

        return dtos
            .Select(d => ProductProjections.WithTags(
                d, tagsByProduct.GetValueOrDefault(d.ProductId, [])))
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Search — uses ToSummary expression projection (no Tags, translates cleanly)
    //
    // Round trips: SearchAsync issues 2 SQL statements per call:
    //   1. COUNT(*) — total matching rows for pagination metadata
    //   2. SELECT ... LIMIT/OFFSET — the actual page of results
    //
    // Both share the exact same IQueryable<ProductSummaryDto> so the WHERE,
    // JOIN, and ORDER BY are identical — only the terminal operator differs.
    //
    // If double round-trip latency becomes a concern at scale, the only
    // LINQ-safe alternative is to drop the TotalCount from the response and
    // use cursor-based pagination instead (HasNextPage via Take(pageSize+1)).
    // Window function approaches (COUNT(*) OVER()) require raw SQL.
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PagedResult<ProductSummaryDto>> SearchAsync(
        ProductSearchRequest request, CancellationToken ct = default)
    {
        // Build the full query once — nothing hits the DB here.
        // ToSummary is an Expression<> so EF Core generates a narrow SELECT
        // (no SELECT *, no Tags join).
        IQueryable<ProductSummaryDto> query = db.Products
            .Where(ProductSpecificationBuilder.Build(request)!)
            .ApplySort(request.SortBy)
            .Select(ProductProjections.ToSummary);

        // Round trip 1: COUNT — same WHERE clause, no ORDER BY, no LIMIT
        int totalCount = await query.CountAsync(ct);

        // Round trip 2: paged data
        List<ProductSummaryDto> items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return new PagedResult<ProductSummaryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Aggregations
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Dictionary<string, int>> GetStockByCategoryAsync(CancellationToken ct = default)
    {
        // Generated SQL:
        //   SELECT c.name, SUM(p.stock)
        //   FROM commerce.products p
        //   LEFT JOIN commerce.categories c ON p.category_id = c.category_id
        //   WHERE p.is_active = true
        //   GROUP BY c.name
        return await db.Products
            .Where(p => p.IsActive)
            .GroupBy(p => p.Category.Name)
            .Select(g => new { CategoryName = g.Key, TotalStock = g.Sum(p => p.Stock) })
            .ToDictionaryAsync(x => x.CategoryName, x => x.TotalStock, ct);
    }

    public async Task<bool> ExistsAsync(string sku, CancellationToken ct = default)
        => await db.Products.AnyAsync(p => p.Sku == sku, ct);
}

