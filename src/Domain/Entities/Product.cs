using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatabricksPoc.Domain.Entities;

/// <summary>
/// Maps to: catalog.commerce.products (Unity Catalog 3-part naming)
/// Delta table — no EF migrations, schema managed in Databricks.
/// Annotations are read by both EF Core (MSSQL) and LinqToDB (Databricks).
/// </summary>
[Table("products", Schema = "commerce")]
public class Product
{
  [Key]
  [Column("product_id")]
  public long ProductId { get; set; }

  [Column("sku")]
  public string Sku { get; set; } = default!;

  [Column("name")]
  public string Name { get; set; } = default!;

  [Column("description")]
  public string Description { get; set; } = default!;

  [Column("price")]
  public decimal Price { get; set; }

  [Column("stock")]
  public int Stock { get; set; }

  [Column("is_active")]
  public bool IsActive { get; set; }

  [Column("created_at")]
  public DateTime CreatedAt { get; set; }

  [Column("updated_at")]
  public DateTime UpdatedAt { get; set; }

  [Column("category_id")]
  public long CategoryId { get; set; }

  // Navigation properties — excluded from column mapping in both EF Core and LinqToDB
  [NotMapped]
  public Category Category { get; set; } = default!;

  [NotMapped]
  public ICollection<ProductTag> Tags { get; set; } = [];
}
