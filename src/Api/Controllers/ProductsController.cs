using System.Diagnostics;
using DatabricksPoc.Domain.Models;
using DatabricksPoc.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace DatabricksPoc.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(IProductRepository repo) : ControllerBase
{
    // GET /api/products/{id}
    [HttpGet("{id:long}")]
    public async Task<ActionResult<ProductDetailDto>> GetById(
        long id, CancellationToken ct)
    {
        var sw = new Stopwatch();
        sw.Start();
        var product = await repo.GetByIdAsync(id, ct);
        sw.Stop();
        Console.WriteLine($"GetByIdAsync took {sw.ElapsedMilliseconds} ms");
        return product is null ? NotFound() : Ok(product);
    }

    // GET /api/products/singleQuery/{id}
    [HttpGet("singleQuery/{id:long}")]
    public async Task<ActionResult<ProductDetailDto>> GetByIdSingleQuery(
        long id, CancellationToken ct)
    {
        var sw = new Stopwatch();
        sw.Start();
        var product = await repo.GetByIdSingleQueryAsync(id, ct);
        sw.Stop();
        Console.WriteLine($"GetByIdSingleQueryAsync took {sw.ElapsedMilliseconds} ms");
        return product is null ? NotFound() : Ok(product);
    }

    // GET /api/products/sku/{sku}
    [HttpGet("sku/{sku}")]
    public async Task<ActionResult<ProductDetailDto>> GetBySku(
        string sku, CancellationToken ct)
    {
        var product = await repo.GetBySkuAsync(sku, ct);
        return product is null ? NotFound() : Ok(product);
    }

    // GET /api/products/category/{slug}
    [HttpGet("category/{slug}")]
    public async Task<ActionResult<IReadOnlyList<ProductDetailDto>>> GetByCategory(
        string slug, CancellationToken ct)
        => Ok(await repo.GetByCategoryAsync(slug, ct));

    // GET /api/products/search?nameContains=widget&minPrice=10&tags=sale,new&page=1&pageSize=20&sortBy=price_asc
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<ProductSummaryDto>>> Search(
        [FromQuery] ProductSearchRequest request, CancellationToken ct)
        => Ok(await repo.SearchAsync(request, ct));

    // GET /api/products/stock-by-category
    [HttpGet("stock-by-category")]
    public async Task<ActionResult<Dictionary<string, int>>> StockByCategory(CancellationToken ct)
        => Ok(await repo.GetStockByCategoryAsync(ct));
}
