using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using VestedAI.ConnectorSdk.Errors;

namespace BcConnector;

// ---------------------------------------------------------------------------
// Shared plumbing for the PaginatedToolHandler tools that page through the ASG
// AI Gateway: an offset cursor mapped onto the gateway's top/skip parameters.
// The gateway echoes 'skip' in every paged response; a missing echo means the
// deployed AL app predates paging for that operation, and paging against it
// would silently return the same first page forever — so the helper fails
// loudly instead.
// ---------------------------------------------------------------------------
internal static class BcPaging
{
    /// <summary>
    /// Rows to ask the gateway for when the caller is the model itself rather than a
    /// cursor-driven page fetch (DatasetCursor.PageSize == 0). The hub reduces such a
    /// result to a fixed-size dataset sample before the model ever sees it
    /// (boundDatasetSample(rows, 20) in runtime/internal/connectorhub/dispatcher.go),
    /// so anything beyond this is computed by BC, serialized, shipped and dropped.
    /// Kept in step with that constant by hand — grep boundDatasetSample if it moves.
    /// </summary>
    public const int SampleRows = 20;

    /// <summary>
    /// What one dataset can carry, from the platform's own caps (dataset.max_rows in
    /// laravel/app/Services/Datasets/DatasetCaps.php). The gateway cannot see that limit,
    /// so tools that can produce more rows than this state it on the request and let BC
    /// refuse an undeliverable scope BEFORE computing it rather than after.
    /// </summary>
    public const int DatasetRowCeiling = 50_000;

    /// <summary>
    /// Parse a cursor that may carry a materialisation key: "&lt;key&gt;|&lt;offset&gt;" once the
    /// gateway has stored the result, or a bare offset while it has not. Returns
    /// (null, 0) for the first page.
    /// </summary>
    public static (string? Key, int Offset) ParseWindowCursor(string? token, string toolKey)
    {
        if (token is null)
            return (null, 0);
        var bar = token.IndexOf('|');
        if (bar < 0)
            return (null, ParseOffset(token, toolKey));
        var key = token[..bar];
        if (key.Length == 0 ||
            !int.TryParse(token[(bar + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ||
            offset < 0)
            throw new ToolValidationException(toolKey, $"Invalid pagination cursor '{token}'.");
        return (key, offset);
    }

    /// <summary>Parse the opaque cursor token (a non-negative row offset); null = first page.</summary>
    public static int ParseOffset(string? token, string toolKey)
    {
        if (token is null)
            return 0;
        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0)
            throw new ToolValidationException(toolKey, $"Invalid pagination cursor '{token}'.");
        return offset;
    }

    /// <summary>
    /// Serialize the LLM-visible args and graft the cursor-driven top/skip onto the payload —
    /// they are not part of the tool's schema, so the model never manages them.
    /// </summary>
    public static JsonObject WithPaging(object args, int top, int skip)
    {
        var payload = JsonSerializer.SerializeToNode(args, BcClient.JsonOptions)!.AsObject();
        payload["top"]  = top;
        payload["skip"] = skip;
        return payload;
    }

    /// <summary>Fail when the gateway response did not echo 'skip' (AL app too old to page this op).</summary>
    public static void RequireSkipEcho(int? skipEcho, string operation, string toolKey)
    {
        if (skipEcho is null)
            throw new ToolValidationException(
                toolKey,
                $"The connected ASG AI Gateway does not support paging on {operation} " +
                "(no 'skip' in its response). Update the ASG AI Gateway app in Business Central.");
    }

    /// <summary>Read a case-insensitive property from a gateway 'data' object; false when absent.</summary>
    public static bool TryGetProperty(JsonElement data, string name, out JsonElement value)
    {
        value = default;
        if (data.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var prop in data.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        return false;
    }

    /// <summary>Read an optional int (null when the property is absent or not a number).</summary>
    public static int? ReadNullableInt(JsonElement data, string name) =>
        TryGetProperty(data, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : null;

    /// <summary>Read a bool (default false when absent).</summary>
    public static bool ReadBool(JsonElement data, string name) =>
        TryGetProperty(data, name, out var v) && v.ValueKind == JsonValueKind.True;

    // Candidate keys the gateway might use for the row array, in priority order. The
    // gateway normally uses "rows", but tolerating alternatives (and a bare top-level
    // array) means a gateway response-shape change degrades to "found the rows"
    // rather than a silent empty dataset.
    private static readonly string[] RowContainerKeys =
        ["rows", "records", "results", "items", "entries", "value", "data"];

    /// <summary>
    /// Locate and deserialize the row array from a gateway page envelope, tolerating
    /// alternative container keys or a bare top-level array. Returns an empty list when
    /// no array can be found (a genuinely empty result).
    /// </summary>
    public static List<T> ExtractRows<T>(JsonElement data, JsonSerializerOptions opts)
    {
        if (data.ValueKind == JsonValueKind.Array)
            return data.Deserialize<List<T>>(opts) ?? new List<T>();

        if (data.ValueKind != JsonValueKind.Object)
            return new List<T>();

        foreach (var key in RowContainerKeys)
            if (TryGetProperty(data, key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.Deserialize<List<T>>(opts) ?? new List<T>();

        // Last resort: the first array-valued property, whatever it is called.
        foreach (var prop in data.EnumerateObject())
            if (prop.Value.ValueKind == JsonValueKind.Array)
                return prop.Value.Deserialize<List<T>>(opts) ?? new List<T>();

        return new List<T>();
    }
}
