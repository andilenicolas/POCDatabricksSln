using DatabricksPoc.Domain.Entities;

namespace DatabricksPoc.Application.Search;

/// <summary>
/// Extension method for dynamic ORDER BY on IQueryable&lt;Product&gt;.
/// Keeps sorting logic out of the repository and translatable by EF Core.
///
/// Supported sort keys:
///   "price_asc"    → ORDER BY price ASC
///   "price_desc"   → ORDER BY price DESC
///   "name_asc"     → ORDER BY name ASC
///   "name_desc"    → ORDER BY name DESC
///   "created_desc" → ORDER BY created_at DESC
///   "created_asc"  → ORDER BY created_at ASC
///   (default)      → ORDER BY name ASC
/// </summary>
public static class ProductSortExtensions
{
  public static IQueryable<Product> ApplySort(this IQueryable<Product> query, string? sortBy)
      => sortBy switch
      {
        "price_asc" => query.OrderBy(p => p.Price),
        "price_desc" => query.OrderByDescending(p => p.Price),
        "name_asc" => query.OrderBy(p => p.Name),
        "name_desc" => query.OrderByDescending(p => p.Name),
        "created_desc" => query.OrderByDescending(p => p.CreatedAt),
        "created_asc" => query.OrderBy(p => p.CreatedAt),
        _ => query.OrderBy(p => p.Name)   // default sort
      };
}
