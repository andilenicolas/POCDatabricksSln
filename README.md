# Databricks .NET POC

ASP.NET Core Web API demonstrating read-only access to **Databricks SQL Warehouse** using [linq2db](https://linq2db.github.io/) over the Simba Spark ODBC Driver.

## Architecture

```
HTTP Request
  → ProductsController
    → IProductRepository
      → DatabricksDataConnection (linq2db DataConnection)
        → SAP HANA ODBC provider (built-in linq2db)
          → Simba Spark ODBC Driver
            → Databricks SQL Warehouse (HTTPS/443)
```

```
src/
├── Domain/         Entities · Models · IProductRepository
├── Application/    ProductRepository · Specifications · Projections · Sort
├── Infrastructure/ DatabricksDataConnection · DatabricksOdbcDataProvider
└── Api/            ProductsController
```

### Packages

| Package | Purpose |
|---|---|
| `linq2db 5.4.1` | ORM / query translation |
| `linq2db.AspNet 5.4.1` | DI integration |
| `System.Data.Odbc 9.0.4` | ODBC transport |
| `Swashbuckle.AspNetCore 10.1.4` | Swagger UI |

### Key design decisions

- **Expression trees, not `Func<>`** — projections are `Expression<Func<Product, TDto>>` so linq2db generates a narrow `SELECT` column list, not `SELECT *`
- **Specification pattern** — each filter condition is a small `Expression<Func<Product, bool>>`; `ProductSpecificationBuilder` combines them with `AndAlso` into one `WHERE` clause
- **`IQueryable` stays open** — every `.Where()` / `.Select()` / `.OrderBy()` appends to the expression tree; `.ToListAsync()` is always the last call
- **No tracking** — `DataConnection` does not track by design; appropriate for a read-only API
- **No migrations** — Databricks manages its own schema via Unity Catalog

---

## Prerequisites

### Databricks
1. A Databricks workspace (Community Edition works for testing)
2. A **SQL Warehouse** — note the `Host` and `HTTPPath` from its **Connection Details** tab
3. A **Personal Access Token** — User Settings → Developer → Access Tokens → Generate
4. Tables in Unity Catalog:
   - `commerce.products`, `commerce.categories`, `commerce.product_tags`

---

## Setup — Windows (local development)

### 1. Install the Simba Spark ODBC Driver

1. Download the **64-bit Windows** installer from https://www.databricks.com/spark/odbc-drivers-download
2. Run with default options
3. Verify registration:
   ```powershell
   Get-OdbcDriver | Where-Object { $_.Name -match "Simba|Spark" } | Select-Object Name, Platform
   ```
   Expected: `Simba Spark ODBC Driver  64-bit`

### 2. Store credentials as user secrets

Never commit a real token — use .NET user secrets:

```powershell
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Databricks" "Driver={Simba Spark ODBC Driver};Host=<host>;Port=443;SSL=1;ThriftTransport=2;HTTPPath=<http-path>;AuthMech=3;UID=token;PWD=<dapi-token>;"
```

ASP.NET Core merges user secrets automatically when `ASPNETCORE_ENVIRONMENT=Development`.

### 3. Run

```powershell
dotnet run
```

Swagger UI opens at `https://localhost:<port>/`.

---

## Setup — Linux / Docker

`appsettings.json` is pre-configured for the Linux driver path. Set the full connection string via environment variable:

```bash
export ConnectionStrings__Databricks="Driver=/opt/simba/spark/lib/64/libsparkodbc_sb64.so;Host=<host>;Port=443;SSL=1;ThriftTransport=2;HTTPPath=<http-path>;AuthMech=3;UID=token;PWD=<dapi-token>;"
```

### Service principal / OAuth2 (production)

Switch `AuthMech=11` and supply client credentials instead of a PAT:

```
AuthMech=11;Auth_Flow=1;Auth_Client_ID=<id>;Auth_Client_Secret=<secret>;
```

---

## Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/products/{id}` | Single product with tags |
| `GET` | `/api/products/category/{slug}` | Products by category |
| `GET` | `/api/products/search` | Paginated search with filters |
| `GET` | `/api/products/stock/by-category` | Stock aggregated by category |
