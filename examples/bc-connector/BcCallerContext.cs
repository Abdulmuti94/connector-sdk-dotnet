using System.Text.Json.Nodes;
using VestedAI.ConnectorSdk.Errors;
using VestedAI.ConnectorSdk.Tool;

namespace BcConnector;

// ---------------------------------------------------------------------------
// Caller ERP-identity context. These fields (employee number, ERP identifier,
// department identifiers) arrive on every ToolCallRequest via ToolContext and are
// authenticated by the hub — they are NOT model-supplied tool arguments. They are
// therefore injected here at call time and travel to the AL gateway in a dedicated
// 'callerContext' field on the action body, kept entirely separate from the
// model-controlled 'payload' so args can never spoof identity.
// ---------------------------------------------------------------------------
internal static class BcCallerContext
{
    /// <summary>
    /// True when the request carries at least one stable caller identifier
    /// (employee number or ERP identifier). Department membership alone does not
    /// count as identity.
    /// </summary>
    public static bool IsPresent(ToolContext ctx) =>
        !string.IsNullOrWhiteSpace(ctx.EmployeeNo) ||
        !string.IsNullOrWhiteSpace(ctx.ErpIdentifier);

    /// <summary>
    /// Guard for write/sensitive tools: a data-changing operation must run on
    /// behalf of a known user. Throws when no caller identity is present.
    /// Read tools do not call this — they proceed unscoped.
    /// </summary>
    public static void Require(ToolContext ctx, string toolKey)
    {
        if (!IsPresent(ctx))
            throw new ToolValidationException(
                toolKey,
                "This action changes Business Central data and must run on behalf of a known user, " +
                "but the request carried no caller identity (employee number or ERP identifier).");
    }

    /// <summary>
    /// Serialize the authenticated caller identity for the gateway action body.
    /// Always returns a JSON object string (fields may be empty / departments may be
    /// an empty array); never throws. The gateway logs the identity and uses
    /// <c>departments</c> to scope results where a department dimension exists.
    /// </summary>
    public static string Serialize(ToolContext ctx)
    {
        var departments = new JsonArray();
        foreach (var department in ctx.ErpDepartmentIdentifiers)
            if (!string.IsNullOrWhiteSpace(department))
                departments.Add(department.Trim());

        var obj = new JsonObject
        {
            ["employeeNo"] = ctx.EmployeeNo,
            ["erpIdentifier"] = ctx.ErpIdentifier,
            ["departments"] = departments,
        };

        return obj.ToJsonString();
    }
}
