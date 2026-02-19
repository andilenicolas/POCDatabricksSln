using DatabricksPoc.Domain.Entities;
using DatabricksPoc.Domain.Models;

namespace DatabricksPoc.Domain.Repositories;

public interface IProductRepository
{
  // ── Simple reads ────────────────────────────────────────────────────────
  Task<ProductDetailDto?> GetByIdAsync(long productId, CancellationToken ct = default);
  Task<ProductDetailDto?> GetBySkuAsync(string sku, CancellationToken ct = default);
  Task<IReadOnlyList<ProductDetailDto>> GetByCategoryAsync(string categorySlug, CancellationToken ct = default);

  // ── Search / filtering with dynamic projections ──────────────────────
  Task<PagedResult<ProductSummaryDto>> SearchAsync(ProductSearchRequest request, CancellationToken ct = default);

  // ── Aggregations that stay LINQ-based ───────────────────────────────
  Task<Dictionary<string, int>> GetStockByCategoryAsync(CancellationToken ct = default);
  Task<bool> ExistsAsync(string sku, CancellationToken ct = default);
}
