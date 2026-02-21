using LinqToDB;
using LinqToDB.Extensions.Logging;
using Microsoft.Extensions.Logging;
using DatabricksPoc.Infrastructure.Context;
using DatabricksPoc.Application.Repositories;

namespace Databricks.Integration.Tests.Integration;

public interface IDatabricksDbFactory
{
    DatabricksDataConnection Create();
}

public sealed class DatabricksDbFactory : IDatabricksDbFactory
{
    private readonly string _connectionString;

    public DatabricksDbFactory(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public DatabricksDataConnection Create()
    {
        var options = new DataOptions()
            .UseConnectionString(ProviderName.SapHanaOdbc, _connectionString)
            .UseLoggerFactory(new LoggerFactory());

        var db = new DatabricksDataConnection(new DataOptions<DatabricksDataConnection>(options));
        db.OnTraceConnection = ti =>
        {
            if (ti.TraceInfoStep == LinqToDB.Data.TraceInfoStep.BeforeExecute)
            {
                TestContext.WriteLine("SQL:\n" + ti.SqlText);
            }
        };

        return db;
    }
}


[TestFixture]
[Category("Integration")]
[NonParallelizable] // sequential: avoid ODBC/Databricks flakiness
public sealed class ProductRepository_ReadOnly_IntegrationTests
{
    // LOCAL DEV ONLY. DO NOT PUT REAL TOKENS HERE.
    private static readonly string _connStrFallback = "Driver={Simba Spark ODBC Driver};Host=dbc-5d1.cloud.databricks.com;Port=443;SSL=1;ThriftTransport=2;HTTPPath=/sql/1.0/warehouses/479c;Catalog=catalog_a;AuthMech=3;UID=token;PWD=dapi_your_token;";
    private DatabricksDataConnection _db = default!;
    private ProductRepository _repo = default!;


    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var connStr =
                Environment.GetEnvironmentVariable("DATABRICKS_ODBC_CS")
                ?? (string.IsNullOrWhiteSpace(_connStrFallback) ? null : _connStrFallback);

        if (string.IsNullOrWhiteSpace(connStr))
        {
            throw new InvalidOperationException(
                "Missing Databricks connection string.\n" +
                "Set env var 'DATABRICKS_ODBC_CS' (CI secret).\n" +
                "If running locally, temporarily set fallbackCs in the test fixture (DO NOT COMMIT TOKENS)."
            );
        }

        var factory = new DatabricksDbFactory(connStr);

        _db = factory.Create();

        _repo = new ProductRepository(_db);
    }


    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_db is not null)
            await _db.DisposeAsync();
    }

    [Test]
    public async Task GetByIdAsync_WhenMissing_ReturnsNull()
    {
        var dto = await _repo.GetByIdAsync(long.MaxValue, CancellationToken.None);
        Assert.That(dto, Is.Null);
    }

    [Test]
    public async Task GetByIdAsync_WhenExisting_ReturnsCategory_And_DoesNotThrow()
    {
        var dto = await _repo.GetByIdAsync(1, CancellationToken.None);

        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.ProductId, Is.EqualTo(1));
        Assert.That(dto.CategoryName, Is.Not.Null.And.Not.Empty);
        Assert.That(dto.Tags, Is.Not.Null);
    }
}


