using Microsoft.Data.SqlClient;
using VestedAI.ConnectorSdk.Errors;
using VestedAI.ConnectorSdk.Tool;

namespace BcConnector;

// ---------------------------------------------------------------------------
// Direct read-only SQL access to the Business Central on-prem database, as a
// companion to the AL gateway (BcClient). It runs a single validated SELECT
// (see BcSqlGuard) against a least-privilege login and returns the rows.
//
// SECURITY MODEL (the SELECT-only guarantee lives in the database, not here):
//   1. A dedicated SQL login granted SELECT and denied INSERT/UPDATE/DELETE/EXEC.
//   2. BcSqlGuard parses the statement and rejects anything but one read-only SELECT.
//   3. (DEFERRED) Per-caller ERP permission scoping — see the TODO in RunSelectAsync.
//
// The feature is OPTIONAL: if no BC_SQL_* variables are set, Configure() leaves the
// client disabled and the tool reports a clear "not configured" error rather than
// failing process startup. Connections are opened per call and returned to the
// ADO.NET pool — no long-lived connection is held.
// ---------------------------------------------------------------------------
internal static class BcSqlClient
{
    private static string? _connectionString;
    private static int _timeoutSeconds = 60;
    private static string[] _allowedSchemas = Array.Empty<string>();

    /// <summary>True once a connection string has been configured from BC_SQL_* variables.</summary>
    public static bool IsConfigured => _connectionString is not null;

    /// <summary>
    /// Build the read-only SQL connection from BC_SQL_* environment variables. Supports either a full
    /// <c>BC_SQL_CONNECTION</c> string, or discrete <c>BC_SQL_SERVER/DATABASE/USERNAME/PASSWORD</c>.
    /// Does nothing (feature stays disabled) when neither is present. Call once at startup.
    /// </summary>
    public static void Configure()
    {
        var full   = Environment.GetEnvironmentVariable("BC_SQL_CONNECTION");
        var server = Environment.GetEnvironmentVariable("BC_SQL_SERVER");

        if (string.IsNullOrWhiteSpace(full) && string.IsNullOrWhiteSpace(server))
        {
            Console.WriteLine("[bc-sql] BC_SQL_* not set — direct SQL query tool is disabled.");
            return;
        }

        _timeoutSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("BC_SQL_TIMEOUT_SECONDS"), out var t) && t > 0 ? t : 60;

        var allowed = Environment.GetEnvironmentVariable("BC_SQL_ALLOWED_SCHEMAS");
        _allowedSchemas = string.IsNullOrWhiteSpace(allowed)
            ? Array.Empty<string>()
            : allowed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!string.IsNullOrWhiteSpace(full))
        {
            _connectionString = full;
        }
        else
        {
            // On-prem BC SQL Servers commonly use a self-signed certificate; default to trusting it
            // (override with BC_SQL_TRUST_CERT=false where a CA-signed cert is installed).
            var trustCert = !string.Equals(
                Environment.GetEnvironmentVariable("BC_SQL_TRUST_CERT"), "false",
                StringComparison.OrdinalIgnoreCase);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource             = server,
                InitialCatalog         = Require("BC_SQL_DATABASE"),
                UserID                 = Require("BC_SQL_USERNAME"),
                Password               = Require("BC_SQL_PASSWORD"),
                ApplicationName        = "VestedAI-BcConnector-SqlReader",
                ApplicationIntent      = ApplicationIntent.ReadOnly,
                Encrypt                = true,
                TrustServerCertificate = trustCert,
                ConnectTimeout         = _timeoutSeconds,
            };
            _connectionString = builder.ConnectionString;
        }

        var schemaNote = _allowedSchemas.Length > 0
            ? $"schemas: {string.Join(", ", _allowedSchemas)}"
            : "all readable schemas (no allow-list)";
        Console.WriteLine($"[bc-sql] Direct SQL query tool enabled ({schemaNote}).");
    }

    /// <summary>
    /// Validate and run a single read-only SELECT, returning every row it produces. Throws
    /// <see cref="ToolValidationException"/> when the feature is not configured, the statement is not
    /// a lone SELECT, or the query/connection fails. Row count is bounded only by the query itself
    /// (use <c>TOP</c>) and the command timeout — there is no server-side row cap.
    /// </summary>
    public static async Task<SqlQueryOutcome> RunSelectAsync(
        string sql, ToolContext ctx, string toolKey)
    {
        if (_connectionString is null)
            throw new ToolValidationException(
                toolKey,
                "Direct SQL querying is not configured. Set BC_SQL_CONNECTION (or " +
                "BC_SQL_SERVER / BC_SQL_DATABASE / BC_SQL_USERNAME / BC_SQL_PASSWORD).");

        BcSqlGuard.EnsureSingleReadOnlySelect(sql, _allowedSchemas, toolKey);

        await using var conn = new SqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync();
        }
        catch (SqlException ex)
        {
            throw new ToolValidationException(
                toolKey, $"Could not connect to the Business Central SQL Server: {ex.Message}", ex);
        }

        // Read-only analytics path: never take locks that could block live BC users.
        await ExecuteNonQueryAsync(conn, "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;");

        // TODO (DEFERRED — per-caller ERP permission scoping):
        // Identity scoping is intentionally not yet applied here, so this tool currently returns
        // UNSCOPED data — keep it pointed at non-sensitive views/tables until this is in place.
        // When implementing, push the hub-authenticated caller identity onto the session so that
        // ai.* views (or an RLS SECURITY POLICY) can filter rows — the caller cannot spoof these,
        // they come from ToolContext, not from the model's SQL:
        //   await ExecuteNonQueryAsync(conn,
        //       "EXEC sp_set_session_context @key=N'erp_id',      @value=@erpId;" +
        //       "EXEC sp_set_session_context @key=N'departments', @value=@depts;",
        //       ("@erpId", ctx.ErpIdentifier ?? ""),
        //       ("@depts", string.Join(",", ctx.ErpDepartmentIdentifiers)));
        _ = ctx; // referenced today only by the deferred scoping above

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = _timeoutSeconds;

        var columns = new List<string>();
        var rows = new List<Dictionary<string, object?>>();

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();

            for (var i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                for (var i = 0; i < reader.FieldCount; i++)
                    row[columns[i]] = reader.IsDBNull(i) ? null : Normalize(reader.GetValue(i));
                rows.Add(row);
            }
        }
        catch (SqlException ex)
        {
            throw new ToolValidationException(toolKey, $"SQL query failed: {ex.Message}", ex);
        }

        return new SqlQueryOutcome(columns, rows, rows.Count);
    }

    // Keep result values JSON-friendly: BLOB/media columns are summarized instead of dumped (a BC
    // picture column would otherwise flood the result), and a couple of awkward types are stringified.
    private static object? Normalize(object value) => value switch
    {
        byte[] bytes => $"0x[{bytes.Length} bytes]",
        TimeSpan ts  => ts.ToString(),
        char c       => c.ToString(),
        _            => value, // string, bool, numeric, decimal, DateTime(Offset), Guid — all STJ-friendly
    };

    private static async Task ExecuteNonQueryAsync(
        SqlConnection conn, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? "");
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Require(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ConnectorException(
                $"Missing required environment variable '{name}'. " +
                "It is needed when BC_SQL_SERVER is set (discrete SQL connection). " +
                "Alternatively set a full BC_SQL_CONNECTION string.");
        return value;
    }
}

/// <summary>Result of a read-only SQL query: ordered columns and the row maps.</summary>
internal sealed record SqlQueryOutcome(
    List<string> Columns,
    List<Dictionary<string, object?>> Rows,
    int RowCount);
