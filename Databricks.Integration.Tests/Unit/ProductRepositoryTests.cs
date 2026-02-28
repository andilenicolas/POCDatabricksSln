using System.Runtime.CompilerServices;
using DatabricksPoc.Application.Repositories;
using DatabricksPoc.Domain.Entities;
using DatabricksPoc.Infrastructure.Context;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using Microsoft.Data.Sqlite;

namespace DatabricksPoc.Tests.Unit;

// Bootstrap: initialise SQLite native library exactly once per process.
// [ModuleInitializer] runs before any test framework code touches the assembly.
// internal static class SqliteBootstrap
// {
//   [ModuleInitializer]
//   internal static void Init() => SQLitePCL.Batteries.Init();
// }

// ProductRepositoryTests
//
// Databricks 3-part naming:  catalog_a  .  commerce  .  products
//                             ----------    ---------    -----------
//                             Set in the    Schema       Table name
//                             connection    layer:       mapped in
//                             string.       modelled     DatabricksDataConnection
//                             Ignored       here with    via HasTableName().
//                             in tests.     ATTACH.
//
// How the SQLite setup mirrors production:
//
//   Main SqliteConnection(:memory:)
//     Represents the catalog layer (catalog_a).
//     Ignored in tests -- the catalog is baked into the connection string
//     in production and has no effect on the LINQ queries under test.
//
//   ATTACH ':memory:' AS commerce
//     Models the "commerce" schema layer.
//     DatabricksDataConnection maps every entity with HasSchemaName("commerce"),
//     so LinqToDB emits SQL like:
//       SELECT ... FROM `commerce`.`products` ...
//     SQLite routes that reference to the attached database, matching
//     the production 3-part path exactly (minus the catalog prefix).
//
//   DatabricksDataConnection constructed over the SQLite connection.
//     The production fluent mapping schema and the backtick identifier
//     interceptor are both active -- no overrides, no stub types.
//     ProductRepository receives the exact same type it gets from DI.
//     Only the underlying provider differs (SQLite instead of Spark ODBC).
//
// No mocking. No fake ITable<T>. Real LINQ to SQL translation on every test.
// SQLite accepts backtick-quoted identifiers via MySQL compatibility mode.
[TestFixture]
public class ProductRepositoryTests
{
  private SqliteConnection _sqliteConn = null!;
  private DatabricksDataConnection _db = null!;
  private ProductRepository _sut = null!;

  [SetUp]
  public void SetUp()
  {
    // Main connection = catalog layer (catalog_a) -- ignored in tests.
    _sqliteConn = new SqliteConnection("Data Source=:memory:");
    _sqliteConn.Open();

    // ATTACH ':memory:' AS commerce = the "commerce" schema layer.
    // LinqToDB generates commerce.<table> references; SQLite resolves
    // them against this attached database.
    using (var cmd = _sqliteConn.CreateCommand())
    {
      cmd.CommandText = "ATTACH DATABASE ':memory:' AS commerce";
      cmd.ExecuteNonQuery();
    }

    var options = new DataOptions()
        .UseConnection(SQLiteTools.GetDataProvider(), _sqliteConn, disposeConnection: false);

    _db = new DatabricksDataConnection(new DataOptions<DatabricksDataConnection>(options));

    // CreateTable uses the mapping schema (HasSchemaName("commerce") +
    // HasTableName("products" etc.)), so each table is created inside the
    // attached "commerce" database -- mirroring catalog_a.commerce.<table>.
    _db.CreateTable<Category>();
    _db.CreateTable<Product>();
    _db.CreateTable<ProductTag>();

    _sut = new ProductRepository(_db);
  }

  [TearDown]
  public void TearDown()
  {
    _db.Dispose();
    _sqliteConn.Dispose();
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
        UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
      };

  // ExistsAsync

  [Test]
  public async Task ExistsAsync_SkuPresent_ReturnsTrue()
  {
    _db.Insert(Cat(1));
    _db.Insert(Prod(1, 1, "SKU-001"));

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
