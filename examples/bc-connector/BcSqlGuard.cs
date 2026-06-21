using Microsoft.SqlServer.TransactSql.ScriptDom;
using VestedAI.ConnectorSdk.Errors;

namespace BcConnector;

// ---------------------------------------------------------------------------
// Read-only SQL gate. The REAL "SELECT only" guarantee is the least-privilege
// SQL login (it is granted SELECT and denied INSERT/UPDATE/DELETE/EXEC at the
// database). This guard is defense-in-depth on top: it parses the statement with
// the actual T-SQL parser (ScriptDom) — never regex — and rejects anything that
// is not a single, side-effect-free SELECT before the query ever reaches the
// server, yielding a clear tool error instead of a SQL permission failure.
// ---------------------------------------------------------------------------
internal static class BcSqlGuard
{
    /// <summary>
    /// Throw <see cref="ToolValidationException"/> unless <paramref name="sql"/> is exactly
    /// one read-only SELECT. When <paramref name="allowedSchemas"/> is non-empty, every table
    /// reference must be qualified with one of those schemas (e.g. <c>ai</c>).
    /// </summary>
    public static void EnsureSingleReadOnlySelect(
        string sql, IReadOnlyCollection<string> allowedSchemas, string toolKey)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ToolValidationException(toolKey, "sql is required.");

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> errors;
        using (var reader = new StringReader(sql))
            fragment = parser.Parse(reader, out errors);

        if (errors is { Count: > 0 })
        {
            var first = errors[0];
            throw new ToolValidationException(
                toolKey, $"SQL could not be parsed: {first.Message} (line {first.Line}, col {first.Column}).");
        }

        // Flatten every statement across every batch — a single GO-free, ';'-free SELECT
        // produces exactly one statement here; anything else is rejected.
        var statements = (fragment as TSqlScript)?.Batches.SelectMany(b => b.Statements).ToList()
                         ?? new List<TSqlStatement>();

        if (statements.Count != 1)
            throw new ToolValidationException(
                toolKey, "Exactly one statement is allowed — no batches and no ';'-separated statements.");

        if (statements[0] is not SelectStatement)
            throw new ToolValidationException(
                toolKey, "Only SELECT is allowed (no INSERT/UPDATE/DELETE/MERGE/EXEC/DDL).");

        // First pass: collect CTE names so the schema allow-list does not mistake a WITH-clause
        // alias (which is never schema-qualified) for a forbidden table reference.
        var cteNames = new CteNameCollector();
        fragment.Accept(cteNames);

        var visitor = new GuardVisitor(allowedSchemas, cteNames.Names);
        fragment.Accept(visitor);
        if (visitor.Violation is not null)
            throw new ToolValidationException(toolKey, visitor.Violation);
    }

    private sealed class CteNameCollector : TSqlFragmentVisitor
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(CommonTableExpression node)
        {
            if (node.ExpressionName?.Value is { } name)
                Names.Add(name);
        }
    }

    // Walks the parsed tree (ScriptDom drives traversal via Accept) and records the first
    // violation: SELECT ... INTO (creates a table), EXEC, or an out-of-allow-list schema.
    private sealed class GuardVisitor : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _allowed;
        private readonly HashSet<string> _cteNames;
        public string? Violation { get; private set; }

        public GuardVisitor(IReadOnlyCollection<string> allowedSchemas, HashSet<string> cteNames)
        {
            _allowed = new HashSet<string>(allowedSchemas, StringComparer.OrdinalIgnoreCase);
            _cteNames = cteNames;
        }

        public override void Visit(SelectStatement node)
        {
            if (node.Into is not null)
                Fail("SELECT ... INTO is not allowed — it creates a table.");
        }

        public override void Visit(ExecuteStatement node) =>
            Fail("EXEC / stored-procedure execution is not allowed.");

        public override void Visit(NamedTableReference node)
        {
            if (_allowed.Count == 0)
                return; // no allow-list configured: rely on the read-only login's grants

            var schema = node.SchemaObject?.SchemaIdentifier?.Value;
            var baseName = node.SchemaObject?.BaseIdentifier?.Value;
            if (string.IsNullOrEmpty(schema))
            {
                if (baseName is not null && _cteNames.Contains(baseName))
                    return; // a WITH-clause CTE reference, not a real table
                Fail($"Tables must be schema-qualified with one of: {string.Join(", ", _allowed)}.");
            }
            else if (!_allowed.Contains(schema))
            {
                Fail($"Schema '{schema}' is not allowed. Allowed: {string.Join(", ", _allowed)}.");
            }
        }

        private void Fail(string message) => Violation ??= message;
    }
}
