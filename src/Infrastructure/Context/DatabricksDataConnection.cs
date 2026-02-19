using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using DatabricksPoc.Domain.Entities;

namespace DatabricksPoc.Infrastructure.Context;

public class DatabricksDataConnection : DataConnection
{
  private static readonly MappingSchema _mappingSchema = BuildMappingSchema();

  public DatabricksDataConnection(DataOptions<DatabricksDataConnection> options)
      : base(options.Options)
  {
    AddMappingSchema(_mappingSchema);
  }

  public ITable<Product> Products => this.GetTable<Product>();
  public ITable<Category> Categories => this.GetTable<Category>();
  public ITable<ProductTag> ProductTags => this.GetTable<ProductTag>();

  private static MappingSchema BuildMappingSchema()
  {
    var schema = new MappingSchema();
    var builder = new FluentMappingBuilder(schema);

    // ── Product ───────────────────────────────────────────────────────────
    builder.Entity<Product>()
        .HasTableName("products")
        .HasSchemaName("commerce")   // → commerce.products in SQL
        .HasPrimaryKey(p => p.ProductId)
        .Property(p => p.ProductId).HasColumnName("product_id")
        .Property(p => p.Sku).HasColumnName("sku")
        .Property(p => p.Name).HasColumnName("name")
        .Property(p => p.Description).HasColumnName("description")
        .Property(p => p.Price).HasColumnName("price")
        .Property(p => p.Stock).HasColumnName("stock")
        .Property(p => p.IsActive).HasColumnName("is_active")
        .Property(p => p.CategoryId).HasColumnName("category_id")
        .Property(p => p.CreatedAt).HasColumnName("created_at")
        .Property(p => p.UpdatedAt).HasColumnName("updated_at")
        // Navigation properties are not columns — tell Linq2DB to ignore them
        .Property(p => p.Category).IsNotColumn()
        .Property(p => p.Tags).IsNotColumn()
        // Declare the association so Linq2DB can generate JOINs in LINQ queries
        .Association(p => p.Category, p => p.CategoryId, c => c.CategoryId)
        .Association(p => p.Tags, p => p.ProductId, t => t.ProductId);

    // ── Category ──────────────────────────────────────────────────────────
    builder.Entity<Category>()
        .HasTableName("categories")
        .HasSchemaName("commerce")
        .HasPrimaryKey(c => c.CategoryId)
        .Property(c => c.CategoryId).HasColumnName("category_id")
        .Property(c => c.Name).HasColumnName("name")
        .Property(c => c.Slug).HasColumnName("slug")
        .Property(c => c.Products).IsNotColumn();

    // ── ProductTag ────────────────────────────────────────────────────────
    builder.Entity<ProductTag>()
        .HasTableName("product_tags")
        .HasSchemaName("commerce")
        .HasPrimaryKey(t => t.ProductTagId)
        .Property(t => t.ProductTagId).HasColumnName("product_tag_id")
        .Property(t => t.ProductId).HasColumnName("product_id")
        .Property(t => t.Tag).HasColumnName("tag")
        .Property(t => t.Product).IsNotColumn();

    builder.Build();
    return schema;
  }
}
