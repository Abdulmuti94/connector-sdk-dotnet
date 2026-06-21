using System.Text.Json.Nodes;

namespace BcConnector;

/// <summary>
/// Infers BC company country from entity numbers when the model omits or mis-guesses
/// <c>country</c>. IC partner and branch codes often embed the country (e.g.
/// <c>ICP-ASG-UAEAD</c> → AE).
/// </summary>
internal static class BcCountryInference
{
    private static readonly string[] EntityNumberFields =
    [
        "customerNo",
        "vendorNo",
        "customerNumber",
        "vendorNumber",
    ];

    /// <summary>
    /// Returns a two-letter country code when <paramref name="entityNo"/> embeds one.
    /// </summary>
    public static string? InferFromEntityNumber(string? entityNo)
    {
        if (string.IsNullOrWhiteSpace(entityNo))
            return null;

        var upper = entityNo.Trim().ToUpperInvariant();

        // Order matters: check longer / more specific tokens first.
        if (upper.Contains("-UAE", StringComparison.Ordinal) ||
            upper.Contains("ICP-ASG-UAE", StringComparison.Ordinal))
            return "AE";

        if (upper.Contains("-KWT", StringComparison.Ordinal) ||
            upper.Contains("-KW", StringComparison.Ordinal))
            return "KW";

        if (upper.Contains("-QAR", StringComparison.Ordinal) ||
            upper.Contains("-QA", StringComparison.Ordinal))
            return "QA";

        if (upper.Contains("-OM", StringComparison.Ordinal))
            return "OM";

        return null;
    }

    /// <summary>
    /// Scans common No. fields on the tool payload for an embedded country code.
    /// </summary>
    public static string? InferFromPayload(JsonObject node)
    {
        foreach (var field in EntityNumberFields)
        {
            var key = node.FirstOrDefault(
                p => string.Equals(p.Key, field, StringComparison.OrdinalIgnoreCase)).Key;
            if (key is null)
                continue;

            var inferred = InferFromEntityNumber(node[key]?.GetValue<string>());
            if (inferred is not null)
                return inferred;
        }

        return null;
    }

    /// <summary>
    /// When the model passes the wrong country but the entity number names another,
    /// prefer the number (e.g. SA + ICP-ASG-UAEAD → AE).
    /// </summary>
    public static bool ShouldOverrideExplicitCountry(string explicitCountry, string? inferredCountry) =>
        inferredCountry is not null &&
        !string.Equals(explicitCountry, inferredCountry, StringComparison.OrdinalIgnoreCase);
}
