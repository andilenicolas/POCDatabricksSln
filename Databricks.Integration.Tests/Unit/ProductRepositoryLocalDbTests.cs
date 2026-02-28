using DatabricksPoc.Application.Repositories;
using DatabricksPoc.Domain.Entities;
using DatabricksPoc.Infrastructure.Context;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SqlServer;
using Microsoft.Data.SqlClient;

namespace DatabricksPoc.Tests.Unit;

// ProductRepositoryLocalDbTests
//
// Same coverage as ProductRepositoryTests but backed by (localdb)\MSSQLLocalDB
// instead of SQLite.
//
// Databricks 3-part naming:  catalog_a  .  commerce  .  products
//                             ----------    ---------    -----------
//                             Not modelled  Created as   Mapped in
//                             (conn string  SQL Server   DatabricksDataConnection
//                             concept).     schema.      via HasTableName().
//
// Setup:
//   [OneTimeSetUp]  Create a GUID-named database on LocalDB + commerce schema.
//   [SetUp]         Truncate all tables so each test starts clean.
//   [OneTimeTearDown] DROP the database.
//
// The real DatabricksDataConnection is used without any modification.
// DatabricksInterceptor is a no-op for SQL Server:
//   - FixQuoting    targets "identifier" quotes; SQL Server emits [identifier] -> no match.
//   - InlineParams  replaces ? placeholders; SQL Server uses @p0 named params -> no match.
//   - Parameters.Clear() gated on Contains('?') -> never fires -> named params survive.
[TestFixture]
public class ProductRepositoryLocalDbTests
{
  private static readonly string _dbName = $"ProductRepoTest_{Guid.NewGuid():N}";
  private const string MasterConnStr =
      @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";

  private SqlConnection _conn = null!;
  private DatabricksDataConnection _db = null!;
  private ProductRepository _sut = null!;

  [OneTimeSetUp]
  public void OneTimeSetUp()
  {
    // Create the GUID-named database on LocalDB.
    using var master = new SqlConnection(MasterConnStr);
    master.Open();
    using var create = master.CreateCommand();
    create.CommandText = $"CREATE DATABASE [{_dbName}]";
    create.ExecuteNonQuery();

    // Open a persistent connection to the new database (keeps it alive).
    _conn = new SqlConnection(
        $@"Server=(localdb)\MSSQLLocalDB;Database={_dbName};Integrated Security=true;TrustServerCertificate=true");
    _conn.Open();

    // Build DatabricksDataConnection over the SQL Server connection.
    // Pass the open connection so GetDataProvider() can auto-detect the SQL Server version.
    var provider = SqlServerTools.GetDataProvider(connection: _conn);
    var options = new DataOptions()
        .UseConnectionString(ProviderName.SqlServer2025, $@"Server=(localdb)\MSSQLLocalDB;Database={_dbName};Integrated Security=true;TrustServerCertificate=true");
    _db = new DatabricksDataConnection(new DataOptions<DatabricksDataConnection>(options));

    // Create the "commerce" schema then each table.
    // DatabricksDataConnection maps every entity with HasSchemaName("commerce"),
    // so CreateTable<> issues: CREATE TABLE commerce.<table> (...)
    _db.Execute("CREATE SCHEMA commerce");
    _db.CreateTable<Category>();
    _db.CreateTable<Product>();
    _db.CreateTable<ProductTag>();

    _sut = new ProductRepository(_db);
    }

  [SetUp]
  public void SetUp()
  {
    // Truncate in FK-safe order so each test starts with empty tables.
    _db.Execute("DELETE FROM commerce.product_tags");
    _db.Execute("DELETE FROM commerce.products");
    _db.Execute("DELETE FROM commerce.categories");
  }

  [OneTimeTearDown]
  public void OneTimeTearDown()
  {
    _db.Dispose();
    _conn.Dispose();

    // Force single-user mode to evict any lingering connections, then drop.
    using var master = new SqlConnection(MasterConnStr);
    master.Open();
    using var drop = master.CreateCommand();
    drop.CommandText =
        $"ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
        $"DROP DATABASE [{_dbName}]";
    drop.ExecuteNonQuery();
  }

  // Seed helpers

  private static Category Cat(long id, string name = "Electronics", string slug = "electronics")
      => new() { CategoryId = id, Name = name, Slug = slug };

  private static Product Prod(long id, long catId, string sku, bool active = true,
      int stock = 10, decimal price = 9.99m)
      => new()
      {
        ProductId = id,
        Sku = sku,
        Name = $"Product {id}",
        Description = "desc",
        Price = price,
        Stock = stock,
        IsActive = active,
        CategoryId = catId,
        CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Items = new() { new() { Id = "1", Name = "name"} }
      };

  // ExistsAsync

  [Test]
  public async Task ExistsAsync_SkuPresent_ReturnsTrue()
  {
    _db.Insert(Cat(1));
    var product = Prod(1, 1, "SKU-001");
    _db.Insert(product);

    var result = await _sut.ExistsAsync("SKU-001");

    result.Should().BeTrue();
  }

  [Test]
  public async Task ExistsAsync_SkuAbsent_ReturnsFalse()
  {
    var result = await _sut.ExistsAsync("SKU-MISSING");

    result.Should().BeFalse();
  }

  // GetByIdAsync

  [Test]
  public async Task GetByIdAsync_ProductExists_ReturnsMappedDtoWithCategoryAndTags()
  {
    _db.Insert(Cat(1, "Electronics", "electronics"));
    _db.Insert(Prod(42, 1, "SKU-042"));
    _db.Insert(new ProductTag { ProductTagId = 1, ProductId = 42, Tag = "featured" });
    _db.Insert(new ProductTag { ProductTagId = 2, ProductId = 42, Tag = "sale" });

    var dto = await _sut.GetByIdAsync(42);

    dto.Should().NotBeNull();
    dto!.ProductId.Should().Be(42);
    dto.Sku.Should().Be("SKU-042");
    dto.CategoryName.Should().Be("Electronics");
    dto.CategorySlug.Should().Be("electronics");
    dto.Tags.Should().BeEquivalentTo(new[] { "featured", "sale" });
  }

  [Test]
  public async Task GetByIdAsync_ProductMissing_ReturnsNull()
  {
    var dto = await _sut.GetByIdAsync(999);

    dto.Should().BeNull();
  }

  [Test]
  public async Task GetByIdAsync_ProductHasNoTags_ReturnsEmptyTagsArray()
  {
    _db.Insert(Cat(1));
    _db.Insert(Prod(5, 1, "SKU-005"));

    var dto = await _sut.GetByIdAsync(5);

    dto.Should().NotBeNull();
    dto!.Tags.Should().BeEmpty();
  }

  // GetBySkuAsync

  [Test]
  public async Task GetBySkuAsync_SkuExists_ReturnsMappedDto()
  {
    _db.Insert(Cat(2, "Books", "books"));
    _db.Insert(Prod(10, 2, "BOOK-001"));

    var dto = await _sut.GetBySkuAsync("BOOK-001");

    dto.Should().NotBeNull();
    dto!.Sku.Should().Be("BOOK-001");
    dto.CategoryName.Should().Be("Books");
    dto.CategorySlug.Should().Be("books");
  }

  [Test]
  public async Task GetBySkuAsync_SkuMissing_ReturnsNull()
  {
    var dto = await _sut.GetBySkuAsync("NO-SUCH-SKU");

    dto.Should().BeNull();
  }

  // GetStockByCategoryAsync

  [Test]
  public async Task GetStockByCategoryAsync_SumsOnlyActiveProductsPerCategory()
  {
    _db.Insert(Cat(1, "Gadgets", "gadgets"));
    _db.Insert(Cat(2, "Clothing", "clothing"));

    _db.Insert(Prod(1, 1, "G-001", active: true, stock: 5));
    _db.Insert(Prod(2, 1, "G-002", active: true, stock: 3));
    _db.Insert(Prod(3, 1, "G-003", active: false, stock: 99)); // excluded

    _db.Insert(Prod(4, 2, "C-001", active: true, stock: 20));

    var result = await _sut.GetStockByCategoryAsync();

    result.Should().HaveCount(2);
    result["Gadgets"].Should().Be(8);
    result["Clothing"].Should().Be(20);
  }
}
