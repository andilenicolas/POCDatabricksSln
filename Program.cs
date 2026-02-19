using DatabricksPoc.Application.Repositories;
using DatabricksPoc.Domain.Repositories;
using DatabricksPoc.Infrastructure.Context;
using DatabricksPoc.Infrastructure.Provider;
using LinqToDB;
using LinqToDB.AspNet;
using LinqToDB.AspNet.Logging;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Register custom Databricks provider ───────────────────────────────────
// Must happen before any DataConnection is created.
// Registers DatabricksOdbcDataProvider under the "Databricks.Odbc" provider name.
DatabricksOdbcDataProvider.Register();

// ── 2. Read connection string ─────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Databricks")
    ?? throw new InvalidOperationException("ConnectionStrings:Databricks is missing.");

// ── 3. Register Linq2DB DataConnection ───────────────────────────────────────
// Scoped lifetime: one DataConnection per HTTP request (same as EF Core DbContext).
// UseDefaultLogging wires Linq2DB's SQL tracing into ASP.NET Core ILogger.
builder.Services.AddLinqToDBContext<DatabricksDataConnection>((provider, options) =>
    options
        .UseConnectionString(DatabricksOdbcDataProvider.ProviderName, connectionString)
        .UseDefaultLogging(provider));

// ── 4. Register repository ────────────────────────────────────────────────────
builder.Services.AddScoped<IProductRepository, ProductRepository>();

// ── 5. API infrastructure ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger UI — serves at https://localhost:<port>/ immediately on startup
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "DatabricksPoc API v1");
    c.RoutePrefix = string.Empty;
});

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
