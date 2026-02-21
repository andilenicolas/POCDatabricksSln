using LinqToDB;
using LinqToDB.Extensions.Logging;
using DatabricksPoc.Domain.Repositories;
using DatabricksPoc.Infrastructure.Context;
using DatabricksPoc.Application.Repositories;
using LinqToDB.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Databricks")
    ?? throw new InvalidOperationException("ConnectionStrings:Databricks is missing.");

builder.Services.AddLinqToDBContext<DatabricksDataConnection>((provider, options) =>
    options
        .UseProvider(ProviderName.SapHanaOdbc)
        .UseConnectionString(connectionString)
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

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
