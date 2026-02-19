namespace DatabricksPoc.Infrastructure.Provider;

/// <summary>
/// Provider configuration for Databricks SQL Warehouse via Simba ODBC.
///
/// linq2db has no built-in Databricks provider. The SAP HANA ODBC provider
/// ("SapHana.Odbc") is the correct built-in choice: it drives a plain
/// System.Data.Odbc.OdbcConnection — no file-path expansion, no JET concepts —
/// and targets ANSI SQL 2003, which Databricks SQL Warehouse also exposes.
/// LIMIT/OFFSET pagination, standard JOINs, and GROUP BY all work without
/// any additional overrides.
/// </summary>
public static class DatabricksOdbcDataProvider
{
  /// <summary>
  /// The linq2db provider name to use in AddLinqToDBContext / UseConnectionString.
  /// Maps to the built-in SAP HANA ODBC data provider (LinqToDB.ProviderName.SapHanaOdbc).
  /// </summary>
  public const string ProviderName = "SapHana.Odbc";

  /// <summary>
  /// No registration needed — "SapHana.Odbc" is a built-in linq2db provider.
  /// This method is kept for symmetry; call it at startup if preferred.
  /// </summary>
  public static void Register() { }
}
