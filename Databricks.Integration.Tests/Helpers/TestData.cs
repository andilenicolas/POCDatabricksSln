using DatabricksPoc.Domain.Models;

namespace DatabricksPoc.Tests.Helpers;

/// <summary>
/// Centralised test data factory.
/// All tests use these builders so a DTO shape change only needs fixing here.
/// </summary>
public static class TestData
{
  public static ProductDetailDto MakeProductDetail(
      long id = 1,
      string sku = "SKU-001",
      string name = "Wireless Headphones",
      decimal price = 299.99m,
      int stock = 50,
      bool isActive = true,
      string category = "Electronics",
      string categorySlug = "electronics",
      string[]? tags = null) => new()
      {
        ProductId = id,
        Sku = sku,
        Name = name,
        Description = "Test description",
        Price = price,
        Stock = stock,
        IsActive = isActive,
        CategoryName = category,
        CategorySlug = categorySlug,
        Tags = tags ?? ["featured", "sale"],
        CreatedAt = new DateTime(2024, 1, 1),
        UpdatedAt = new DateTime(2024, 6, 1)
      };

  public static ProductSummaryDto MakeProductSummary(
      long id = 1,
      string sku = "SKU-001",
      string name = "Wireless Headphones",
      decimal price = 299.99m,
      int stock = 50,
      string category = "Electronics") => new()
      {
        ProductId = id,
        Sku = sku,
        Name = name,
        Price = price,
        Stock = stock,
        CategoryName = category
      };

  public static PagedResult<ProductSummaryDto> MakePagedResult(
      IEnumerable<ProductSummaryDto>? items = null,
      int page = 1,
      int pageSize = 20,
      int totalCount = 1) => new()
      {
        Items = (items ?? [MakeProductSummary()]).ToList(),
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount
      };
}
