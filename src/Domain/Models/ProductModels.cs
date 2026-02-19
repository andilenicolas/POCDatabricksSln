namespace DatabricksPoc.Domain.Models;

// ─────────────────────────────────────────────
// Inbound search request
// ─────────────────────────────────────────────
public class ProductSearchRequest
{
  public string? NameContains { get; init; }
  public string? Sku { get; init; }
  public string? CategorySlug { get; init; }
  public decimal? MinPrice { get; init; }
  public decimal? MaxPrice { get; init; }
  public bool? InStockOnly { get; init; }
  public string[]? Tags { get; init; }

  // Pagination
  public int Page { get; init; } = 1;
  public int PageSize { get; init; } = 20;

  // Sorting — "price_asc", "price_desc", "name_asc", "created_desc"
  public string? SortBy { get; init; }
}

// ─────────────────────────────────────────────
// Projected DTOs — decoupled from entities
// ─────────────────────────────────────────────
public class ProductSummaryDto
{
  public long ProductId { get; init; }
  public string Sku { get; init; } = default!;
  public string Name { get; init; } = default!;
  public decimal Price { get; init; }
  public int Stock { get; init; }
  public string CategoryName { get; init; } = default!;
}

public class ProductDetailDto
{
  public long ProductId { get; init; }
  public string Sku { get; init; } = default!;
  public string Name { get; init; } = default!;
  public string Description { get; init; } = default!;
  public decimal Price { get; init; }
  public int Stock { get; init; }
  public bool IsActive { get; init; }
  public string CategoryName { get; init; } = default!;
  public string CategorySlug { get; init; } = default!;
  public string[] Tags { get; init; } = [];
  public DateTime CreatedAt { get; init; }
  public DateTime UpdatedAt { get; init; }
}

public class PagedResult<T>
{
  public IReadOnlyList<T> Items { get; init; } = [];
  public int TotalCount { get; init; }
  public int Page { get; init; }
  public int PageSize { get; init; }
  public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
  public bool HasNextPage => Page < TotalPages;
}
