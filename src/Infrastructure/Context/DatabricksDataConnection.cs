using LinqToDB;
using System.Text;
using LinqToDB.Data;
using LinqToDB.Mapping;
using System.Data.Common;
using LinqToDB.Interceptors;
using DatabricksPoc.Domain.Entities;
using System.Text.RegularExpressions;

namespace DatabricksPoc.Infrastructure.Context;

public class DatabricksDataConnection : DataConnection
{
    private static readonly MappingSchema _mappingSchema = BuildMappingSchema();

    public DatabricksDataConnection(DataOptions<DatabricksDataConnection> options)
        : base(options.Options)
    {
        this.AddMappingSchema(_mappingSchema);
        this.AddInterceptor(new DatabricksInterceptor());
    }

    public ITable<Product> Products => this.GetTable<Product>();
    public ITable<Category> Categories => this.GetTable<Category>();
    public ITable<ProductTag> ProductTags => this.GetTable<ProductTag>();

    private static MappingSchema BuildMappingSchema()
    {
        var schema = new MappingSchema();
        var builder = new FluentMappingBuilder(schema);

        // â”€â”€ Product â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
            .Property(p => p.Items).HasColumnName("items").HasConversion(
                v => DatabricksConverters.ToItemModelsString(v),
                s => DatabricksConverters.ParseItemModels(s))
            .Property(p => p.Category).IsNotColumn()
            .Property(p => p.Tags).IsNotColumn()
            .Association(p => p.Category, p => p.CategoryId, c => c.CategoryId)
            .Association(p => p.Tags, p => p.ProductId, t => t.ProductId);

        // â”€â”€ Category â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        builder.Entity<Category>()
            .HasTableName("categories")
            .HasSchemaName("commerce")
            .HasPrimaryKey(c => c.CategoryId)
            .Property(c => c.CategoryId).HasColumnName("category_id")
            .Property(c => c.Name).HasColumnName("name")
            .Property(c => c.Slug).HasColumnName("slug")
            .Property(c => c.Products).IsNotColumn();

        // â”€â”€ ProductTag â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        builder.Entity<ProductTag>()
            .HasTableName("product_tags")
            .HasSchemaName("commerce")
            .HasPrimaryKey(t => t.ProductTagId)
            .Property(t => t.ProductTagId).HasColumnName("product_tag_id")
            .Property(t => t.ProductId).HasColumnName("product_id")
            .Property(t => t.Tag).HasColumnName("tag")
            .Property(t => t.Product).IsNotColumn();
        AddDataBricksConverters(builder.MappingSchema);
        builder.Build();
        return schema;
    }

    private static void AddDataBricksConverters(MappingSchema schema)
    {
        // Our interest is here entity <-> Linq2DB <-> Data source
        schema.SetDataType(typeof(List<ItemModel>), DataType.NVarChar);

        // ItemModel Converter
        schema.SetConverter<string, List<ItemModel>>(DatabricksConverters.ParseItemModels);
        schema.SetConverter<List<ItemModel>, string>(DatabricksConverters.ToItemModelsString);
    }
}

public static class DatabricksConverters
{
    public static List<ItemModel> ParseItemModels(string v)
    {
        if (string.IsNullOrWhiteSpace(v))
            return [];

        try
        {
            return [.. v.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(ParseItemModelSegment)];
        }
        catch
        {
            return [];
        }
    }

    public static string ToItemModelsString(List<ItemModel> v)
    {
        if (v == null || v.Count == 0)
            return string.Empty;

        return string.Join(";", v.Select(item =>
            $"{(item.Name ?? string.Empty).Trim()} @ID{(item.Id ?? string.Empty).Trim()}"));
    }

    public static ItemModel ParseItemModelSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return new ItemModel { Name = string.Empty, Id = string.Empty };

        var trimmed = segment.Trim();
        var marker = trimmed.IndexOf(" @ID", StringComparison.Ordinal);

        if (marker < 0)
            return new ItemModel { Name = trimmed, Id = string.Empty };

        var name = trimmed[..marker].Trim();
        var idStart = marker + 4;
        var id = idStart < trimmed.Length ? trimmed[idStart..].Trim() : string.Empty;

        return new ItemModel { Name = name, Id = id };
    }
}

public sealed partial class DatabricksInterceptor : CommandInterceptor
{
    private const int RegexTimeoutMs = 500;
    [GeneratedRegex("\"([\\.\\w\\s\\-]+)\"", RegexOptions.None, RegexTimeoutMs)]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"FROM\s+`?DUMMY`?", RegexOptions.IgnoreCase, RegexTimeoutMs)]
    private static partial Regex DummyFromRegex();

    public override DbCommand CommandInitialized(CommandEventData eventData, DbCommand command)
    {
        var strValue = "";
        foreach (DbParameter p in command.Parameters)
        {
            //p.Value = "NULL";
            strValue += $"  {p.SourceColumn}: {p.Value?.GetType().FullName ?? "NULL"} = {p.Value}\n\n";
        }
        Console.WriteLine($"Original SQL:\n{command.CommandText}\nParameters:\n{strValue}");
        var hasPositionalParams = command.CommandText.Contains('?');
        command.CommandText = FixSql(command.CommandText, command.Parameters);
        if (hasPositionalParams)
            command.Parameters.Clear();
        return command;
    }

    private static string FixSql(string sql, DbParameterCollection parameters)
    {
        var newSql = sql;
        newSql = FixQuoting(newSql);
        newSql = ReplaceDummyTable(newSql);
        newSql = InlineParameters(newSql, parameters.Cast<DbParameter>().Select(p => p.Value));
        return newSql;
    }

    public static string InlineParameters(string sql, IReadOnlyList<DataParameter> parameters)
        => InlineParameters(sql, parameters.Select(p => p.Value));

    private static string InlineParameters(string sql, IEnumerable<object?> values)
    {
        var valueList = values.ToList();
        if (valueList.Count == 0)
            return sql;

        var sb = new StringBuilder();
        var paramIndex = 0;

        foreach (var character in sql)
        {
            if (character == '?' && paramIndex < valueList.Count)
            {
                sb.Append(RenderLiteral(valueList[paramIndex]));
                paramIndex++;
            }
            else
            {
                sb.Append(character);
            }
        }

        return sb.ToString();
    }

    private static string FixQuoting(string sql)
    {
        sql = sql.Replace("\"\".", string.Empty);
        return QuoteRegex().Replace(sql, m => $"`{m.Groups[1].Value}`");
    }

    private static string ReplaceDummyTable(string sql)
        => DummyFromRegex().Replace(sql, "FROM (VALUES (1)) AS `dual`(`x`)");

    private static string RenderLiteral(object? value) => value switch
    {
        null => "NULL",
        bool b => b ? "TRUE" : "FALSE",
        string s => $"'{s.Replace("'", "\\'")}'",
        DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"
    };
}


