

//using DatabricksPoc.Application.Repositories;
//using DatabricksPoc.Domain.Entities;
//using DatabricksPoc.Infrastructure.Context;
//using DuckDB.NET.Data;
//using FluentAssertions;
//using LinqToDB;
//using LinqToDB.Data;
//using LinqToDB.DataProvider.DuckDB;

//namespace DatabricksPoc.Tests.Integration;

//[TestFixture]
//public class GetByIdAsyncTests
//{
//  private DuckDBConnection _conn = null!;
//  private DatabricksDataConnection _db = null!;

//  [SetUp]
//  public void SetUp()
//  {
//    _conn = new DuckDBConnection("Data Source=:memory:");
//    _conn.Open();

//    _db = new DatabricksDataConnection(
//        new DataOptions<DatabricksDataConnection>(
//            new DataOptions().UseConnection(DuckDBTools.GetDataProvider(), _conn, disposeConnection: false)));

//    _db.Execute("CREATE SCHEMA commerce");
//    _db.CreateTable<Category>();
//    _db.CreateTable<Product>();
//    _db.CreateTable<ProductTag>();

//    _db.Insert(new Category { CategoryId = 1, Name = "Electronics", Slug = "electronics" });
//    _db.Insert(new Product
//    {
//      ProductId = 1,
//      Sku = "SKU-001",
//      Name = "Wireless Headphones",
//      Description = "Test",
//      Price = 299.99m,
//      Stock = 50,
//      IsActive = true,
//      CategoryId = 1,
//      CreatedAt = DateTime.UtcNow,
//      UpdatedAt = DateTime.UtcNow
//    });
//    _db.Insert(new ProductTag { ProductTagId = 1, ProductId = 1, Tag = "sale" });
//  }

//  [TearDown]
//  public void TearDown()
//  {
//    _db.Dispose();
//    _conn.Dispose();
//  }

//  [Test]
//  public async Task GetByIdAsync_ExistingProduct_ReturnsMappedDto()
//  {
//    var result = await new ProductRepository(_db).GetByIdAsync(1);

//    result.Should().NotBeNull();
//    result!.ProductId.Should().Be(1);
//    result.Name.Should().Be("Wireless Headphones");
//    result.CategoryName.Should().Be("Electronics");
//    result.Tags.Should().Contain("sale");
//  }
//}
