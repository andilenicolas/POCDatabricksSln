using DatabricksPoc.Api.Controllers;
using DatabricksPoc.Domain.Models;
using DatabricksPoc.Domain.Repositories;
using DatabricksPoc.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DatabricksPoc.Tests.Unit;

/// <summary>
/// Unit tests for ProductsController.
///
/// Tests the controller class directly — no HTTP stack, no routing.
/// Fast: no factory startup overhead.
///
/// Covers:
///   - Correct return types (Ok, NotFound)
///   - Repository is called with the right arguments
///   - Response body is the value from the repository (no transformation)
///   - Cancellation token is forwarded
/// </summary>
[TestFixture]
public class ProductsControllerTests
{
  private IProductRepository _repo = null!;
  private ProductsController _sut = null!;

  [SetUp]
  public void SetUp()
  {
    _repo = Substitute.For<IProductRepository>();
    _sut = new ProductsController(_repo);
  }

  // ── GetById ───────────────────────────────────────────────────────────────

  [Test]
  public async Task GetById_ProductExists_Returns200WithProduct()
  {
    var expected = TestData.MakeProductDetail(id: 1);
    _repo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(expected);

    var result = await _sut.GetById(1, CancellationToken.None);

    var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    ok.StatusCode.Should().Be(200);
    ok.Value.Should().BeEquivalentTo(expected);
  }

  [Test]
  public async Task GetById_ProductNotFound_Returns404()
  {
    _repo.GetByIdAsync(99, Arg.Any<CancellationToken>()).ReturnsNull();

    var result = await _sut.GetById(99, CancellationToken.None);

    result.Result.Should().BeOfType<NotFoundResult>()
        .Which.StatusCode.Should().Be(404);
  }

  [Test]
  public async Task GetById_ForwardsCancellationToken()
  {
    var cts = new CancellationTokenSource();
    _repo.GetByIdAsync(Arg.Any<long>(), cts.Token).ReturnsNull();

    await _sut.GetById(1, cts.Token);

    await _repo.Received(1).GetByIdAsync(1, cts.Token);
  }

  // ── GetBySku ──────────────────────────────────────────────────────────────

  [Test]
  public async Task GetBySku_ProductExists_Returns200WithProduct()
  {
    var expected = TestData.MakeProductDetail(sku: "SKU-001");
    _repo.GetBySkuAsync("SKU-001", Arg.Any<CancellationToken>()).Returns(expected);

    var result = await _sut.GetBySku("SKU-001", CancellationToken.None);

    var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    ok.Value.Should().BeEquivalentTo(expected);
  }

  [Test]
  public async Task GetBySku_ProductNotFound_Returns404()
  {
    _repo.GetBySkuAsync("INVALID", Arg.Any<CancellationToken>()).ReturnsNull();

    var result = await _sut.GetBySku("INVALID", CancellationToken.None);

    result.Result.Should().BeOfType<NotFoundResult>();
  }

  [Test]
  public async Task GetBySku_ForwardsCancellationToken()
  {
    var cts = new CancellationTokenSource();
    _repo.GetBySkuAsync(Arg.Any<string>(), cts.Token).ReturnsNull();

    await _sut.GetBySku("SKU-001", cts.Token);

    await _repo.Received(1).GetBySkuAsync("SKU-001", cts.Token);
  }

  // ── GetByCategory ─────────────────────────────────────────────────────────

  [Test]
  public async Task GetByCategory_Returns200WithProducts()
  {
    var expected = new List<ProductDetailDto>
        {
            TestData.MakeProductDetail(id: 1, name: "Headphones"),
            TestData.MakeProductDetail(id: 2, name: "Keyboard")
        };
    _repo.GetByCategoryAsync("electronics", Arg.Any<CancellationToken>())
         .Returns(expected);

    var result = await _sut.GetByCategory("electronics", CancellationToken.None);

    var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    ok.Value.Should().BeEquivalentTo(expected);
  }

  [Test]
  public async Task GetByCategory_EmptyCategory_Returns200WithEmptyList()
  {
    _repo.GetByCategoryAsync("empty-cat", Arg.Any<CancellationToken>())
         .Returns(new List<ProductDetailDto>());

    var result = await _sut.GetByCategory("empty-cat", CancellationToken.None);

    var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    ok.Value.Should().BeEquivalentTo(Array.Empty<ProductDetailDto>());
  }

  // ── Search ────────────────────────────────────────────────────────────────

  [Test]
  public async Task Search_ValidRequest_Returns200WithPagedResult()
  {
    var request = new ProductSearchRequest { NameContains = "head", Page = 1, PageSize = 10 };
    var expected = TestData.MakePagedResult(totalCount: 1);
    _repo.SearchAsync(request, Arg.Any<CancellationToken>()).Returns(expected);

    var result = await _sut.Search(request, CancellationToken.None);

    var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    ok.Value.Should().BeEquivalentTo(expected);
  }

  [Test]
  public async Task Search_NoResults_Returns200WithEmptyPage()
  {
    var request = new ProductSearchRequest { NameContains = "zzznomatch" };
    var expected = TestData.MakePagedResult(items: [], totalCount: 0);
    _repo.SearchAsync(request, Arg.Any<CancellationToken>()).Returns(expected);

    var result = await _sut.Search(request, CancellationToken.None);

    var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    var paged = ok.Value.Should().BeOfType<PagedResult<ProductSummaryDto>>().Subject;
    paged.Items.Should().BeEmpty();
    paged.TotalCount.Should().Be(0);
  }

  [Test]
  public async Task Search_ForwardsRequestToRepository()
  {
    var request = new ProductSearchRequest
    {
      NameContains = "keyboard",
      MinPrice = 100m,
      MaxPrice = 500m,
      InStockOnly = true,
      Page = 2,
      PageSize = 5,
      SortBy = "price_asc"
    };
    _repo.SearchAsync(Arg.Any<ProductSearchRequest>(), Arg.Any<CancellationToken>())
         .Returns(TestData.MakePagedResult());

    await _sut.Search(request, CancellationToken.None);

    await _repo.Received(1).SearchAsync(
        Arg.Is<ProductSearchRequest>(r =>
            r.NameContains == "keyboard" &&
            r.MinPrice == 100m &&
            r.MaxPrice == 500m &&
            r.InStockOnly == true &&
            r.Page == 2 &&
            r.PageSize == 5 &&
            r.SortBy == "price_asc"),
        Arg.Any<CancellationToken>());
  }

  // ── StockByCategory ───────────────────────────────────────────────────────

  [Test]
  public async Task StockByCategory_Returns200WithDictionary()
  {
    var expected = new Dictionary<string, int>
    {
      ["Electronics"] = 170,
      ["Clothing"] = 200
    };
    _repo.GetStockByCategoryAsync(Arg.Any<CancellationToken>()).Returns(expected);

    var result = await _sut.StockByCategory(CancellationToken.None);

    var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    ok.Value.Should().BeEquivalentTo(expected);
  }

  [Test]
  public async Task StockByCategory_EmptyResult_Returns200WithEmptyDictionary()
  {
    _repo.GetStockByCategoryAsync(Arg.Any<CancellationToken>())
         .Returns(new Dictionary<string, int>());

    var result = await _sut.StockByCategory(CancellationToken.None);

    var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    ok.Value.Should().BeEquivalentTo(new Dictionary<string, int>());
  }
}
