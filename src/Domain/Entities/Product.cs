namespace DatabricksPoc.Domain.Entities;

/// <summary>
/// Maps to: catalog.commerce.products (Unity Catalog 3-part naming)
/// Delta table — no EF migrations, schema managed in Databricks.
/// </summary>
public class Product
{
  public long ProductId { get; set; }
  public string Sku { get; set; } = default!;
  public string Name { get; set; } = default!;
  public string Description { get; set; } = default!;
  public decimal Price { get; set; }
  public int Stock { get; set; }
  public bool IsActive { get; set; }
  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }

  // Navigation properties (EF Core will LEFT JOIN these)
  public long CategoryId { get; set; }
  public Category Category { get; set; } = default!;

  public ICollection<ProductTag> Tags { get; set; } = [];
}
