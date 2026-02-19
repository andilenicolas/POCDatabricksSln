# Databricks + EF Core — Implementation Notes

## What Was Built

### Folder Structure

The project follows `ARCHITECTURE.md`'s layered layout exactly, placed under `src/` inside the existing solution:

```
src/
├── Domain/
│   ├── Entities/       Product.cs · Related.cs
│   ├── Models/         ProductModels.cs
│   └── Repositories/   IProductRepository.cs
├── Application/
│   ├── Repositories/   ProductRepository.cs
│   ├── Search/         ProductSpecifications.cs · ProductSortExtensions.cs
│   └── Projections/    ProductProjections.cs
├── Infrastructure/
│   ├── Context/        DatabricksDbContext.cs
│   └── Providers/      DatabricksOptions.cs
└── Api/
    └── Controllers/    ProductsController.cs
```

The root `Program.cs` and `appsettings.json` were updated in-place; no new top-level files were created.

---

## Layer-by-Layer Summary

### Domain

Pure C# POCOs with zero EF or framework attributes. `Product` carries navigation properties (`Category`, `Tags`) so EF Core can build expression trees that cross-join without any explicit join syntax in application code. `IProductRepository` defines the async contract the rest of the app depends on — the controller and `Program.cs` reference only this interface.

### Application

- **`ProductRepository`** — the only place LINQ runs. Every method chains `.Where()` → `.ApplySort()` → `.Select()` **before** any terminal operator, so the entire expression tree is handed to EF Core in one shot. `.ToListAsync()` is always the last call — never mid-query.
- **`ProductSpecifications`** — each condition is a tiny `Expression<Func<Product, bool>>`. `ProductSpecificationBuilder.Build()` combines them via `AndAlso` into a single tree. EF Core sees one `WHERE` clause, not chained calls.
- **`ProductSortExtensions`** — a switch-based `.ApplySort()` extension on `IQueryable<Product>`. All branches return `IQueryable`, keeping the tree open.
- **`ProductProjections`** — static `Expression<Func<Product, ProductSummaryDto>>` and `Expression<Func<Product, ProductDetailDto>>` fields. Because they are `Expression<>` (not `Func<>`), EF Core includes them in the SQL `SELECT` column list. If they were `Func<>`, EF would fetch `SELECT *` then filter in memory.

### Infrastructure

- **`DatabricksDbContext`** — sets `NoTracking` globally in `OnConfiguring`. `OnModelCreating` maps every entity to Unity Catalog `schema: "commerce"` tables with snake_case column names. No migrations are ever intended to run against this context.
- **`DatabricksOptions`** — typed config POCO with `BuildConnectionString()` that assembles the Simba ODBC DSN-less connection string.

### Api

`ProductsController` depends only on `IProductRepository`. Five endpoints covering get-by-id, get-by-SKU, get-by-category, paginated search, and stock aggregation.

---

## Changes vs. Initial `files/` Plan

| Area                   | `files/` plan                               | What changed                                                                                                                                                |
| ---------------------- | ------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **EF Core provider**   | `EntityFrameworkCore.Jet` (via `UseOdbc()`) | No `UseOdbc()` exists on NuGet. Used `EntityFrameworkCore.Jet.Odbc` which exposes `UseJetOdbc()` — the correct ODBC-specific extension from the same family |
| **EF Core version**    | 9.0.0                                       | Bumped to **9.0.4** — `EntityFrameworkCore.Jet.Odbc 9.0.0` requires `>= 9.0.4` transitively; using 9.0.2 caused a `NU1605` downgrade error                  |
| **`System.Data.Odbc`** | Not in `files/` csproj                      | Added explicitly — `EntityFrameworkCore.Jet.Odbc` needs it at runtime to load `OdbcFactory`                                                                 |
| **Target framework**   | `net9.0` in `files/` csproj                 | Kept as **`net10.0`** — the existing solution was already net10                                                                                             |
| **`files/` folder**    | Source files meant as reference             | Excluded from compilation via `<Compile Remove="files\**\*" />` to avoid duplicate-type errors                                                              |
| **`CA1416` warning**   | Not addressed                               | Suppressed via `<NoWarn>CA1416</NoWarn>` — the Simba driver is Windows-only by design                                                                       |
| **Swagger**            | `Swashbuckle.AspNetCore`                    | Added; replaced the placeholder `AddOpenApi()` / `MapOpenApi()` with `AddSwaggerGen()` / `UseSwaggerUI()` to match the `files/` `Program.cs`                |

---

## Packages Used

| Package                                    | Version | Why                                                                                                                                                                      |
| ------------------------------------------ | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Microsoft.EntityFrameworkCore`            | 9.0.4   | Core EF runtime — expression tree compilation, IQueryable pipeline                                                                                                       |
| `Microsoft.EntityFrameworkCore.Relational` | 9.0.4   | Relational base: `ToTable()`, `HasColumnName()`, schema mapping, `NoTracking`                                                                                            |
| `EntityFrameworkCore.Jet.Odbc`             | 9.0.0   | Provides `UseJetOdbc(connectionString)` — the only open-source EF Core provider that accepts a raw ODBC connection string. Routes all queries through `System.Data.Odbc` |
| `System.Data.Odbc`                         | 9.0.4   | The .NET ODBC bridge. `EntityFrameworkCore.Jet.Odbc` reflects into `OdbcFactory` at runtime to open physical connections                                                 |
| `Swashbuckle.AspNetCore`                   | 6.9.0   | Swagger UI at `/swagger` — makes the API explorable during development                                                                                                   |
| `Microsoft.AspNetCore.OpenApi`             | 10.0.2  | Already present; kept for OpenAPI metadata                                                                                                                               |

> **Production note**: CData publishes `CData.Databricks.EntityFrameworkCore8` — a purpose-built Databricks EF Core provider. If query translation fidelity becomes a concern with Jet, that is the drop-in commercial replacement. Swap `UseJetOdbc()` for its equivalent extension and remove the Jet packages.

---

## Required `appsettings.json` Configuration

```json
{
  "Databricks": {
    "Host": "adb-<workspace-id>.<region>.azuredatabricks.net",
    "HttpPath": "/sql/1.0/warehouses/<warehouse-id>",
    "AccessToken": "<personal-access-token-or-sp-secret>",
    "Catalog": "main",
    "Port": 443
  }
}
```

### Where to Find Each Value in Databricks

| Key           | Location in Databricks UI                                          |
| ------------- | ------------------------------------------------------------------ |
| `Host`        | SQL Warehouse → **Connection Details** tab → **Server hostname**   |
| `HttpPath`    | SQL Warehouse → **Connection Details** tab → **HTTP path**         |
| `AccessToken` | User Settings → **Developer** → **Access tokens** → Generate token |
| `Catalog`     | Unity Catalog name you want to query (e.g. `main`, `prod_catalog`) |
| `Port`        | Always `443` for Databricks HTTPS endpoints                        |

### Assembled ODBC Connection String

`DatabricksOptions.BuildConnectionString()` produces:

```
Driver={Simba Spark ODBC Driver};
Host=adb-xxx.azuredatabricks.net;
Port=443;
HTTPPath=/sql/1.0/warehouses/abc123;
SSL=1;
ThriftTransport=2;
AuthMech=3;
UID=token;
PWD=<your-token>;
Catalog=main;
```

**`AuthMech=3`** = username/password mode. The literal string `UID=token` is required by Databricks — the actual secret goes in `PWD`.

---

## Host Prerequisites

- **Simba Spark ODBC Driver 2.x** must be installed on the host machine.
  - Windows: the driver name `{Simba Spark ODBC Driver}` must appear in **ODBC Data Source Administrator (64-bit)**.
  - Linux: path to `.so` must be configured in `odbcinst.ini`.
- **.NET 10 SDK** (`net10.0` target framework).

---

## Secret Management

Never put a real token in `appsettings.json`. Two safe alternatives:

**Development — .NET User Secrets:**

```bash
dotnet user-secrets set "Databricks:AccessToken" "dapi..."
```

**Any environment — environment variable** (double-underscore as section separator):

```
Databricks__AccessToken=dapi...
Databricks__Host=adb-xxx.azuredatabricks.net
Databricks__HttpPath=/sql/1.0/warehouses/abc123
```

---

## Service Principal / OAuth2 Auth

For non-interactive / production auth, replace the PAT with a service principal secret and switch Simba to OAuth2 (`AuthMech=11`). Update `BuildConnectionString()` accordingly:

```csharp
$"Driver={{Simba Spark ODBC Driver}};" +
$"Host={Host};Port={Port};HTTPPath={HttpPath};SSL=1;ThriftTransport=2;" +
$"AuthMech=11;" +
$"Auth_Flow=1;" +
$"Auth_Client_ID={ClientId};" +
$"Auth_Client_Secret={ClientSecret};" +
$"Auth_Scope=2523..." +   // Databricks OAuth scope
$"Catalog={Catalog};";
```

Add `ClientId` and `ClientSecret` properties to `DatabricksOptions` and bind them from configuration/secrets.

---

## Key Design Decisions (from ARCHITECTURE.md)

### 1. Expression Trees, not Func delegates

```csharp
// ✅ CORRECT — EF Core receives the expression, generates SQL SELECT p.name, p.price
query.Select(ProductProjections.ToSummary)

// ❌ WRONG — EF fetches SELECT * then filters in memory
query.ToList().Select(p => new ProductSummaryDto { ... })
```

### 2. Specification Pattern for dynamic WHERE

Each condition is a small `Expression<Func<Product, bool>>`.
The builder combines them with `AndAlso` into one expression tree.
EF Core sees a single combined predicate → one clean SQL `WHERE` clause.

### 3. IQueryable stays IQueryable until the last moment

No `.ToList()` mid-query. Every `.Where()`, `.Select()`, `.OrderBy()` just adds to the expression tree. The DB call only happens at `.ToListAsync()`.

### 4. NoTracking by default

Read-only API → disable change tracking globally in `OnConfiguring`. Saves memory and CPU on large result sets.

### 5. No EF Migrations

Databricks manages its own Delta table schema via Unity Catalog. Never run `dotnet ef migrations` against this context.
