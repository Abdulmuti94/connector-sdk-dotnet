using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VestedAI.ConnectorSdk.Errors;
using VestedAI.ConnectorSdk.Tool;

namespace BcConnector;

/// <summary>
/// Resolves LS Central store names (e.g. "مخرج 9", "Exit 9") to store number and
/// location code before retail/inventory gateway calls.
/// </summary>
internal static class BcStoreResolver
{
    private static readonly Regex StoreCodePattern = new(
        @"^[A-Z]{1,6}\d{0,5}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly string[] StoreNoFields = ["storeNo", "createdAtStore"];
    private static readonly string[] StoreNameFields = ["storeNameContains"];

    private sealed class StoreRow
    {
        public string StoreNo { get; set; } = "";
        public string Name { get; set; } = "";
        public string LocationCode { get; set; } = "";
        public string LocationName { get; set; } = "";
    }

    private static readonly ConcurrentDictionary<string, List<StoreRow>> StoresByCompany = new();

    public static bool LooksLikeStoreCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Any(c => char.IsWhiteSpace(c) || c > 127))
            return false;

        return StoreCodePattern.IsMatch(trimmed);
    }

    public static async Task ResolveInPayloadAsync(
        JsonObject node,
        string company,
        string toolKey,
        ToolContext ctx)
    {
        foreach (var field in StoreNoFields)
            await ResolveStoreFieldAsync(node, company, toolKey, ctx, field);

        foreach (var field in StoreNameFields)
            await ResolveNamedFieldAsync(node, company, toolKey, ctx, field);

        await ResolveFilterFieldsAsync(node, company, toolKey, ctx, node["filters"] as JsonArray);
        await ResolveFilterFieldsAsync(node, company, toolKey, ctx, node["Filters"] as JsonArray);

        if (node["items"] is JsonArray items)
        {
            foreach (var item in items)
            {
                if (item is JsonObject itemObj)
                    await ResolveFilterFieldsAsync(itemObj, company, toolKey, ctx, itemObj["filters"] as JsonArray);
            }
        }
    }

    private static async Task ResolveStoreFieldAsync(
        JsonObject node,
        string company,
        string toolKey,
        ToolContext ctx,
        string fieldName)
    {
        var key = FindKey(node, fieldName);
        if (key is null)
            return;

        var value = node[key]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(value) || LooksLikeStoreCode(value))
            return;

        var resolved = await ResolveByNameAsync(company, value, toolKey, ctx);
        ApplyResolvedStore(node, fieldName, resolved.StoreNo);
        LogResolution(value, resolved, toolKey);
    }

    private static async Task ResolveNamedFieldAsync(
        JsonObject node,
        string company,
        string toolKey,
        ToolContext ctx,
        string fieldName)
    {
        var key = FindKey(node, fieldName);
        if (key is null)
            return;

        var value = node[key]?.GetValue<string>()?.Trim();
        node.Remove(key);
        if (string.IsNullOrWhiteSpace(value))
            return;

        var resolved = await ResolveByNameAsync(company, value, toolKey, ctx);
        var targetField = FindKey(node, "createdAtStore") is not null ? "createdAtStore" : "storeNo";
        ApplyResolvedStore(node, targetField, resolved.StoreNo);
        LogResolution(value, resolved, toolKey);
    }

    private static async Task ResolveFilterFieldsAsync(
        JsonObject owner,
        string company,
        string toolKey,
        ToolContext ctx,
        JsonArray? filters)
    {
        if (filters is null)
            return;

        foreach (var filterNode in filters)
        {
            if (filterNode is not JsonObject filter)
                continue;

            var fieldKey = FindKey(filter, "field");
            var valueKey = FindKey(filter, "value");
            if (fieldKey is null || valueKey is null)
                continue;

            var field = filter[fieldKey]?.GetValue<string>()?.Trim();
            if (!IsStoreFilterField(field))
                continue;

            var value = filter[valueKey]?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(value) || LooksLikeStoreCode(value))
                continue;

            var resolved = await ResolveByNameAsync(company, value, toolKey, ctx);
            filter[valueKey] = resolved.StoreNo;
            LogResolution(value, resolved, toolKey);
        }
    }

    private static void ApplyResolvedStore(JsonObject node, string targetField, string storeNo)
    {
        // Filter-driven operations (Sum/Count and anything carrying preset/entity/
        // filters/items) read the store only from a "Store No." filter — writing a
        // top-level storeNo there just adds a key the gateway doesn't expect. Field-
        // driven operations (find_pos_transactions, net_inventory, find_customer_orders)
        // read the store from their own field instead.
        if (UsesStoreFilter(node))
        {
            AddStoreFilter(node, storeNo);
            return;
        }

        var key = FindKey(node, targetField) ?? targetField;
        node[key] = storeNo;
    }

    private static bool UsesStoreFilter(JsonObject node) =>
        FindKey(node, "preset") is not null ||
        FindKey(node, "entity") is not null ||
        node["filters"] is JsonArray { Count: > 0 } ||
        node["items"] is JsonArray;

    private static void AddStoreFilter(JsonObject node, string storeNo)
    {
        var filtersKey = FindKey(node, "filters") ?? "filters";
        var filters = node[filtersKey] as JsonArray ?? new JsonArray();
        if (HasStoreFilter(filters))
        {
            node[filtersKey] = filters;
            return;
        }

        filters.Add(new JsonObject
        {
            ["field"] = "Store No.",
            ["operator"] = "=",
            ["value"] = storeNo,
        });
        node[filtersKey] = filters;

        if (node["items"] is not JsonArray items)
            return;

        foreach (var item in items)
        {
            if (item is JsonObject itemObj)
                AddStoreFilter(itemObj, storeNo);
        }
    }

    private static bool HasStoreFilter(JsonArray filters) =>
        filters.Any(f =>
            f is JsonObject obj &&
            IsStoreFilterField(obj["field"]?.GetValue<string>() ??
                               obj["Field"]?.GetValue<string>()));

    private static bool IsStoreFilterField(string? field) =>
        string.Equals(field, "Store No.", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(field, "Created at Store", StringComparison.OrdinalIgnoreCase);

    private static async Task<StoreRow> ResolveByNameAsync(
        string company,
        string nameQuery,
        string toolKey,
        ToolContext ctx)
    {
        var matches = await FindStoresAsync(company, nameQuery, toolKey, ctx);

        if (matches.Count == 0)
        {
            throw new ToolValidationException(
                toolKey,
                $"No LS Central store matches '{nameQuery}'. Use find_stores to browse store names, " +
                "then retry with storeNo (e.g. S004) or a narrower storeNameContains.");
        }

        if (matches.Count == 1)
            return matches[0];

        var candidates = string.Join(
            "; ",
            matches.Take(8).Select(s =>
                $"{s.StoreNo} — {s.Name} (location {s.LocationCode})"));

        throw new ToolValidationException(
            toolKey,
            $"Store name '{nameQuery}' matches several stores: {candidates}. " +
            "Ask the user to pick a store or pass an exact storeNo.");
    }

    // Store resolution always works against the full per-company store list, loaded
    // once and cached. We deliberately do NOT cache name-scoped subsets: a partial
    // cache can make a broad name match exactly one row it happens to hold (a false
    // unique match) or be mistaken for the complete list (a false "no match"). One
    // authoritative top:500 load, filtered locally, avoids both failure modes.
    private static async Task<List<StoreRow>> FindStoresAsync(
        string company,
        string nameQuery,
        string toolKey,
        ToolContext ctx)
    {
        var all = await LoadAllStoresAsync(company, toolKey, ctx);
        return FilterByName(all, nameQuery);
    }

    private static async Task<List<StoreRow>> LoadAllStoresAsync(
        string company,
        string toolKey,
        ToolContext ctx)
    {
        if (StoresByCompany.TryGetValue(company, out var cached))
            return cached;

        var data = await BcClient.ExecuteGatewayAsync(
            "FindStores",
            new { top = 500 },
            company,
            toolKey,
            ctx);

        var stores = DeserializeStores(data);
        if (stores.Count > 0)
            StoresByCompany[company] = stores;

        return stores;
    }

    private static List<StoreRow> DeserializeStores(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object &&
        data.TryGetProperty("stores", out var storesEl)
            ? storesEl.Deserialize<List<StoreRow>>(BcClient.JsonOptions) ?? []
            : [];

    private static List<StoreRow> FilterByName(IEnumerable<StoreRow> stores, string nameQuery)
    {
        var query = Normalize(nameQuery);
        return stores
            .Where(s => Normalize(s.Name).Contains(query, StringComparison.Ordinal) ||
                        Normalize(s.StoreNo).Contains(query, StringComparison.Ordinal))
            .ToList();
    }

    private static string Normalize(string value) =>
        value.Trim().ToUpperInvariant();

    private static string? FindKey(JsonObject node, string name) =>
        node.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Key;

    private static void LogResolution(string query, StoreRow resolved, string toolKey) =>
        Console.WriteLine(
            $"[bc] {toolKey}: resolved store '{query}' → {resolved.StoreNo} ({resolved.Name}), " +
            $"location {resolved.LocationCode}.");
}
