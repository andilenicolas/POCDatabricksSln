using System.Net;
using System.Net.Http.Json;
using DatabricksPoc.Domain.Models;
using DatabricksPoc.Domain.Repositories;
using DatabricksPoc.Tests.Fixtures;
using DatabricksPoc.Tests.Helpers;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DatabricksPoc.Tests.Integration;

/// <summary>
/// Integration tests for ProductsController via the real ASP.NET Core HTTP pipeline.
///
/// What these add over the unit tests:
///   - Route matching  — /api/products/sku/{sku} vs /api/products/{id:long}
///     (the long constraint means "SKU-001" must not match the id route)
///   - Model binding   — [FromQuery] ProductSearchRequest binding from query string
///   - JSON serialisation — response body shape, property naming, null handling
///   - Status codes    — the full HTTP response including headers
///   - Middleware       — exception handling, routing middleware
///
/// ApiFactory starts the real ASP.NET Core host once per test class ([OneTimeSetUp]).
/// The IProductRepository is replaced in DI with an NSubstitute mock.
/// No Databricks connection needed.
/// </summary>
[TestFixture]
public class ProductsControllerIntegrationTests
{
  private ApiFactory _factory = null!;
  private HttpClient _client = null!;
  private IProductRepository _repo => _factory.Repository;

  [OneTimeSetUp]
  public void OneTimeSetUp()
  {
    _factory = new ApiFactory();
    _client = _factory.CreateClient();
  }

  [OneTimeTearDown]
  public void OneTimeTearDown()
  {
    _client.Dispose();
    _factory.Dispose();
  }

  // Reset mock state between tests — NSubstitute records all calls,
  // so clearing between tests prevents call counts from bleeding across them
  [SetUp]
  public void SetUp() => _repo.ClearReceivedCalls();

  // ── GET /api/products/{id} ────────────────────────────────────────────────

  [Test]
  public async Task GetById_ProductExists_Returns200WithJsonBody()
  {
    var expected = TestData.MakeProductDetail(id: 1);
    _repo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(expected);

    var response = await _client.GetAsync("/api/products/1");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<ProductDetailDto>();
    body.Should().BeEquivalentTo(expected);
  }

  [Test]
  public async Task GetById_ProductNotFound_Returns404()
  {
    _repo.GetByIdAsync(999, Arg.Any<CancellationToken>()).ReturnsNull();

    var response = await _client.GetAsync("/api/products/999");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Test]
  public async Task GetById_NonNumericId_Returns404_RouteConstraintRejects()
  {
    // The route is {id:long} — a non-numeric segment should not match
    // this route at all. Verifies the route constraint is working.
    var response = await _client.GetAsync("/api/products/not-a-number");

    // Either 404 (no route matched) or 400 (matched but binding failed)
    // Both are correct — the important thing is it doesn't reach the action
    ((int)response.StatusCode).Should().BeOneOf(400, 404);
    await _repo.DidNotReceive().GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
  }

  // ── GET /api/products/sku/{sku} ───────────────────────────────────────────

  [Test]
  public async Task GetBySku_ProductExists_Returns200WithJsonBody()
  {
    var expected = TestData.MakeProductDetail(sku: "SKU-001");
    _repo.GetBySkuAsync("SKU-001", Arg.Any<CancellationToken>()).Returns(expected);

    var response = await _client.GetAsync("/api/products/sku/SKU-001");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<ProductDetailDto>();
    body!.Sku.Should().Be("SKU-001");
  }

  [Test]
  public async Task GetBySku_ProductNotFound_Returns404()
  {
    _repo.GetBySkuAsync("INVALID-SKU", Arg.Any<CancellationToken>()).ReturnsNull();

    var response = await _client.GetAsync("/api/products/sku/INVALID-SKU");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Test]
  public async Task GetBySku_SkuRoute_DoesNotConflictWithIdRoute()
  {
    // "sku" as a segment must match /api/products/sku/{sku} not /api/products/{id:long}
    // This is the critical routing ambiguity test — "sku" is not a long, so the
    // id route should not match, and GetBySkuAsync should be called, not GetByIdAsync
    var expected = TestData.MakeProductDetail(sku: "SKU-TEST");
    _repo.GetBySkuAsync("SKU-TEST", Arg.Any<CancellationToken>()).Returns(expected);

    await _client.GetAsync("/api/products/sku/SKU-TEST");

    await _repo.Received(1).GetBySkuAsync("SKU-TEST", Arg.Any<CancellationToken>());
    await _repo.DidNotReceive().GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
  }

  // ── GET /api/products/category/{slug} ─────────────────────────────────────

  [Test]
  public async Task GetByCategory_Returns200WithList()
  {
    var products = new List<ProductDetailDto>
        {
            TestData.MakeProductDetail(id: 1),
            TestData.MakeProductDetail(id: 2, name: "Keyboard")
        };
    _repo.GetByCategoryAsync("electronics", Arg.Any<CancellationToken>()).Returns(products);

    var response = await _client.GetAsync("/api/products/category/electronics");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<List<ProductDetailDto>>();
    body.Should().HaveCount(2);
  }

  [Test]
  public async Task GetByCategory_EmptyCategory_Returns200WithEmptyList()
  {
    _repo.GetByCategoryAsync("empty", Arg.Any<CancellationToken>())
         .Returns(new List<ProductDetailDto>());

    var response = await _client.GetAsync("/api/products/category/empty");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<List<ProductDetailDto>>();
    body.Should().BeEmpty();
  }

  // ── GET /api/products/search ──────────────────────────────────────────────

  [Test]
  public async Task Search_NoQueryParams_UsesDefaults_Returns200()
  {
    _repo.SearchAsync(Arg.Any<ProductSearchRequest>(), Arg.Any<CancellationToken>())
         .Returns(TestData.MakePagedResult());

    var response = await _client.GetAsync("/api/products/search");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    // Verify defaults were applied by model binding
    await _repo.Received(1).SearchAsync(
        Arg.Is<ProductSearchRequest>(r => r.Page == 1 && r.PageSize == 20),
        Arg.Any<CancellationToken>());
  }

  [Test]
  public async Task Search_AllQueryParams_BindsCorrectly()
  {
    _repo.SearchAsync(Arg.Any<ProductSearchRequest>(), Arg.Any<CancellationToken>())
         .Returns(TestData.MakePagedResult());

    var url = "/api/products/search" +
              "?nameContains=headphones" +
              "&minPrice=100" +
              "&maxPrice=500" +
              "&inStockOnly=true" +
              "&categorySlug=electronics" +
              "&tags=sale&tags=featured" +
              "&page=2&pageSize=5&sortBy=price_asc";

    var response = await _client.GetAsync(url);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    await _repo.Received(1).SearchAsync(
        Arg.Is<ProductSearchRequest>(r =>
            r.NameContains == "headphones" &&
            r.MinPrice == 100m &&
            r.MaxPrice == 500m &&
            r.InStockOnly == true &&
            r.CategorySlug == "electronics" &&
            r.Tags!.Contains("sale") &&
            r.Tags!.Contains("featured") &&
            r.Page == 2 &&
            r.PageSize == 5 &&
            r.SortBy == "price_asc"),
        Arg.Any<CancellationToken>());
  }

  [Test]
  public async Task Search_Returns200WithCorrectPaginationMetadata()
  {
    var paged = TestData.MakePagedResult(
        items: [TestData.MakeProductSummary()],
        page: 2,
        pageSize: 5,
        totalCount: 23);
    _repo.SearchAsync(Arg.Any<ProductSearchRequest>(), Arg.Any<CancellationToken>())
         .Returns(paged);

    var response = await _client.GetAsync("/api/products/search?page=2&pageSize=5");
    var body = await response.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>();

    body!.Page.Should().Be(2);
    body.PageSize.Should().Be(5);
    body.TotalCount.Should().Be(23);
    body.TotalPages.Should().Be(5);      // ceil(23/5)
    body.HasNextPage.Should().BeTrue();  // page 2 of 5
  }

  // ── GET /api/products/stock-by-category ───────────────────────────────────

  [Test]
  public async Task StockByCategory_Returns200WithDictionary()
  {
    var expected = new Dictionary<string, int>
    {
      ["Electronics"] = 170,
      ["Clothing"] = 200
    };
    _repo.GetStockByCategoryAsync(Arg.Any<CancellationToken>()).Returns(expected);

    var response = await _client.GetAsync("/api/products/stock-by-category");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();
    body.Should().BeEquivalentTo(expected);
  }

  // ── Content-Type ──────────────────────────────────────────────────────────

  [Test]
  public async Task AllEndpoints_Return_ApplicationJson_ContentType()
  {
    _repo.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
         .Returns(TestData.MakeProductDetail());
    _repo.GetByCategoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
         .Returns(new List<ProductDetailDto> { TestData.MakeProductDetail() });
    _repo.SearchAsync(Arg.Any<ProductSearchRequest>(), Arg.Any<CancellationToken>())
         .Returns(TestData.MakePagedResult());
    _repo.GetStockByCategoryAsync(Arg.Any<CancellationToken>())
         .Returns(new Dictionary<string, int> { ["Test"] = 1 });

    var endpoints = new[]
    {
            "/api/products/1",
            "/api/products/category/electronics",
            "/api/products/search",
            "/api/products/stock-by-category"
        };

    foreach (var endpoint in endpoints)
    {
      var response = await _client.GetAsync(endpoint);
      response.Content.Headers.ContentType?.MediaType
          .Should().Be("application/json", because: $"endpoint {endpoint} should return JSON");
    }
  }
}
