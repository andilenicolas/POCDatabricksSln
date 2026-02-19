using DatabricksPoc.Domain.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace DatabricksPoc.Tests.Fixtures;

/// <summary>
/// Spins up the real ASP.NET Core pipeline in-process using WebApplicationFactory.
///
/// What this tests that pure unit tests cannot:
///   - Route matching  (/api/products/{id} vs /api/products/sku/{sku})
///   - Model binding   (long id, string sku, [FromQuery] ProductSearchRequest)
///   - Action result serialisation (Ok(dto) → 200 + JSON body)
///   - NotFound() → 404
///   - Middleware ordering
///
/// The real IProductRepository is replaced with an NSubstitute mock in DI,
/// so no Databricks connection or database is needed.
///
/// Lifetime: [OneTimeSetUp] / [OneTimeTearDown] — one factory per test class
/// reuse is significantly faster than creating a new factory per test.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
  public IProductRepository Repository { get; } = Substitute.For<IProductRepository>();

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    // Provide a dummy connection string so Program.cs doesn't throw on startup.
    // The real DataConnection is registered in DI but never used — the repository
    // is replaced below with an NSubstitute mock before any request is served.
    builder.ConfigureAppConfiguration(config =>
    {
      config.AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["ConnectionStrings:Databricks"] = "testing-dummy"
      });
    });

    builder.ConfigureServices(services =>
    {
      // Remove the real repository registration
      var descriptor = services.SingleOrDefault(
              d => d.ServiceType == typeof(IProductRepository));
      if (descriptor is not null)
        services.Remove(descriptor);

      // Replace with the NSubstitute mock — same instance reused across tests
      // so setup and verification work correctly
      services.AddSingleton(Repository);
    });

    // Use a minimal test environment
    builder.UseEnvironment("Testing");
  }
}
