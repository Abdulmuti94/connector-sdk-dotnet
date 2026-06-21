using System.ComponentModel;
using VestedAI.ConnectorSdk.Errors;
using VestedAI.ConnectorSdk.Tool;

namespace BcConnector;

// ---------------------------------------------------------------------------
// erp_bc.data.run_sql — constrained free-form SELECT over the BC SQL Server.
// Belongs to the Data & Analytics agent (erp_bc.data.*). The heavy lifting —
// SELECT-only validation, connection, row capping — lives in BcSqlClient /
// BcSqlGuard; this handler is the typed schema + dispatch, matching the shape
// of the gateway tools in Tools.cs.
// ---------------------------------------------------------------------------

/// <summary>
/// Runs a single read-only T-SQL SELECT directly against the Business Central database for
/// ad-hoc analytics the gateway data tools cannot express (joins, GROUP BY, window functions).
/// </summary>
[Tool(
    Key         = "erp_bc.data.run_sql",
    Description = BcToolDescriptions.RunSql,
    Sensitivity = "read")]
public class RunSql : ToolHandler<RunSql.Args, RunSql.Result>
{
    public class Args
    {
        [Description(
            "A single read-only T-SQL SELECT statement. Exactly one SELECT — no " +
            "INSERT/UPDATE/DELETE/MERGE/EXEC/DDL, no ';'-separated or batched statements, " +
            "no SELECT ... INTO. There is no row cap — ALWAYS use TOP (or an aggregate) to keep the " +
            "result small. " + BcToolDescriptions.SqlCatalog)]
        public string Sql { get; set; } = "";
    }

    public class Result
    {
        [Description("Column names, in result order.")]
        public List<string> Columns { get; set; } = new();

        [Description("Number of rows returned.")]
        public int RowCount { get; set; }

        // Value type is `object`, not JsonElement, for the same NJsonSchema reason documented
        // on QueryRecords.Result.Rows in Tools.cs (avoids an invalid draft-07 schema).
        [Description("Result rows as column-name → value maps.")]
        public List<Dictionary<string, object?>> Rows { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Sql))
            throw new ToolValidationException("erp_bc.data.run_sql", "sql is required.");

        var outcome = await BcSqlClient.RunSelectAsync(
            args.Sql, ctx, "erp_bc.data.run_sql");

        return new Result
        {
            Columns  = outcome.Columns,
            RowCount = outcome.RowCount,
            Rows     = outcome.Rows,
        };
    }
}
