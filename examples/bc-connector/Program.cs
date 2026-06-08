using System.Reflection;
using BcConnector;
using VestedAI.ConnectorSdk;

// Build the BC OData client from BC_* environment variables before connecting
// to the hub. Fails fast with a clear message if any are missing.
BcClient.Configure();

return await ConnectorHost
    .CreateBuilder()
    .ScanAssembly(Assembly.GetExecutingAssembly())
    .Build()
    .RunFromEnvironmentAsync();
