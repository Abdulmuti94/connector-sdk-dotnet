using System.Reflection;
using BcConnector;
using VestedAI.ConnectorSdk;

// Build the BC OData client from BC_* environment variables before connecting
// to the hub. Fails fast with a clear message if any are missing.
BcClient.Configure();

// Optional: build the read-only SQL client from BC_SQL_* variables. No-op (the
// run_sql tool stays disabled) when those variables are not set.
BcSqlClient.Configure();

return await ConnectorHost
    .CreateBuilder()
    .ScanAssembly(Assembly.GetExecutingAssembly())
    .Build()
    .RunFromEnvironmentAsync();
