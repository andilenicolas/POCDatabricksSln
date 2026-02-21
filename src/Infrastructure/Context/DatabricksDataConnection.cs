using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using System.Data.Common;
using LinqToDB.Interceptors;
using DatabricksPoc.Domain.Entities;

namespace DatabricksPoc.Infrastructure.Context;

public class DatabricksDataConnection : DataConnection
{
    private static readonly MappingSchema _mappingSchema = BuildMappingSchema();

    public DatabricksDataConnection(DataOptions<DatabricksDataConnection> options)
        : base(options.Options)
    {
        this.AddMappingSchema(_mappingSchema);
        this.AddInterceptor(new DatabricksIdentifierInterceptor());
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
            .HasSchemaName("commerce")
            .HasTableName("products")
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
            .Property(p => p.Category).IsNotColumn()
            .Property(p => p.Tags).IsNotColumn()
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

public sealed class DatabricksIdentifierInterceptor : CommandInterceptor
{
    private static string FixQuoting(string sql)
        => System.Text.RegularExpressions.Regex.Replace(
            sql,
            "\"([^\"]+)\"",
            m => $"`{m.Groups[1].Value}`");

    public override DbCommand CommandInitialized(
        CommandEventData eventData, DbCommand command)
    {
        command.CommandText = FixQuoting(command.CommandText);
        return command;
    }
}

