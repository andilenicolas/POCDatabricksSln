# Databricks .NET POC

ASP.NET Core Web API demonstrating read-only access to **Databricks SQL Warehouse** using [linq2db](https://linq2db.github.io/) over the Simba Spark ODBC Driver.

## Architecture

```
HTTP Request
  → ASP.NET Core Controller
    → IProductRepository (Repository pattern)
      → DatabricksDataConnection (linq2db DataConnection)
        → SAP HANA ODBC provider (built-in linq2db)
          → Simba Spark ODBC Driver
            → Databricks SQL Warehouse (HTTPS/443)
```

**Key packages**
| Package | Purpose |
|---|---|
| `linq2db 5.4.1` | ORM / query translation |
| `linq2db.AspNet 5.4.1` | DI integration |
| `System.Data.Odbc 9.0.4` | ODBC transport |
| `Swashbuckle.AspNetCore 10.1.4` | Swagger UI |

**Patterns used:** Repository, Specification, Expression projection (no LinqKit — linq2db expands expressions natively).

---

## Prerequisites

### Databricks side
1. A Databricks workspace (Community Edition works for testing)
2. A **SQL Warehouse** — note the `HTTPPath` from its connection details
3. A **Personal Access Token** — User Settings → Developer → Access Tokens → Generate
4. Tables in Unity Catalog matching the mapped schema:
   - `commerce.products`
   - `commerce.categories`
   - `commerce.product_tags`

---

## Setup — Linux / Docker

The base `appsettings.json` is pre-configured for the Linux Simba driver path:

```
Driver=/opt/simba/spark/lib/64/libsparkodbc_sb64.so
```

Install the driver inside your container/host, then set your token via environment variable or Docker secret — do **not** commit the real token:

```bash
export ConnectionStrings__Databricks="Driver=/opt/simba/spark/lib/64/libsparkodbc_sb64.so;Host=<your-host>;Port=443;SSL=1;ThriftTransport=2;HTTPPath=<your-http-path>;AuthMech=3;UID=token;PWD=<your-token>;"
```

---

## Setup — Windows (local development)

> The `appsettings.Development.json` override kicks in automatically when
> `ASPNETCORE_ENVIRONMENT=Development` (the default when running from Visual Studio or `dotnet run`).

### 1. Install the Simba Spark ODBC Driver

1. Go to https://www.databricks.com/spark/odbc-drivers-download
2. Create/sign in to a Databricks account if prompted
3. Download **Simba Spark ODBC Driver** — choose the **64-bit Windows** installer
4. Run the installer (default options are fine)
5. Verify it registered correctly:
   ```powershell
   Get-OdbcDriver | Where-Object { $_.Name -match "Simba|Spark" } | Select-Object Name, Platform
   ```
   You should see `Simba Spark ODBC Driver` with Platform `64-bit`.

   Alternatively: **Control Panel → Administrative Tools → ODBC Data Sources (64-bit) → Drivers tab**

### 2. Store your connection string as a user secret

Never put a real token in `appsettings.Development.json` — use .NET user secrets instead:

```powershell
cd "C:\path\to\DatabricksSln"
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Databricks" "Driver={Simba Spark ODBC Driver};Host=<your-host>;Port=443;SSL=1;ThriftTransport=2;HTTPPath=<your-http-path>;AuthMech=3;UID=token;PWD=<your-token>;"
```

Replace:
- `<your-host>` — e.g. `dbc-ef82b5c7-b72a.cloud.databricks.com`
- `<your-http-path>` — e.g. `/sql/1.0/warehouses/60a677ebb3f702e6`
- `<your-token>` — your Databricks PAT starting with `dapi`

ASP.NET Core merges user secrets automatically in Development — no code changes needed.

### 3. Run

```powershell
dotnet run
```

Swagger UI opens at `https://localhost:<port>/`.

---

## Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/products/{id}` | Single product with tags |
| `GET` | `/api/products/category/{slug}` | Products by category |
| `GET` | `/api/products/search` | Paginated search with filters |
| `GET` | `/api/products/stock/by-category` | Stock aggregated by category |
