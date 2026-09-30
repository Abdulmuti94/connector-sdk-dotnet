using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VestedAI.ConnectorSdk.Errors;
using VestedAI.ConnectorSdk.Tool;

namespace BcConnector;

// ---------------------------------------------------------------------------
// bc.inventory.bulk_stock
//
// Exact, LIVE net inventory for a large item scope — tens of thousands of items,
// optionally broken down per branch. It exists because the other two answers do
// not cover this shape:
//
//   net_inventory   is right for a few items or a few branches. An item list
//                   becomes a filter expression that runs out of room at ~64
//                   items, and every page recomputes the whole result.
//   the snapshot    is store × category grain, so it can say what a branch holds
//                   but never what a named item holds.
//
// The gateway stages the item scope into a table, JOINS it into the six component
// reads (SQL restricts and aggregates — nothing is computed item by item), writes
// the result once, and serves pages as indexed reads. The run is resumable: each
// gateway call computes for a time budget and reports where it stopped, because a
// full run takes minutes and no HTTP call in this stack lives that long.
// ---------------------------------------------------------------------------

[Tool(
    Key         = "erp_bc.inventory.bulk_stock",
    Description = BcToolDescriptions.BulkStock,
    Sensitivity = "read",
    // A bulk run is minutes of real work, and this tool drives it to completion
    // across many short gateway calls before the first page exists. The per-call
    // BC timeout (BC_TIMEOUT_SECONDS) still bounds each of those individually;
    // this bounds the whole drive. waitSeconds bounds it more tightly per call and
    // hands back a resumable runId rather than sitting on the deadline.
    DefaultDeadlineMs = 600_000)]
public class GetBulkStock : PaginatedToolHandler<GetBulkStock.Args, GetBulkStock.Row>
{
    // Each gateway call computes for this long, then commits and returns. Short
    // enough to stay well inside BC_TIMEOUT_SECONDS (120s), long enough that the
    // per-call overhead is noise.
    private const int ChunkBudgetSeconds = 45;

    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        [JsonPropertyName("country")]
        public string Country { get; set; } = "";

        [Description("Item numbers to price the stock of — this is the bulk path, so a list of thousands is the point. Combines with itemNo. An unknown number fails the run rather than silently shrinking the scope.")]
        [JsonPropertyName("itemNos")]
        [JsonConverter(typeof(TolerantListConverter<string>))]
        public List<string> ItemNos { get; set; } = new();

        [Description("A single item number, for convenience. Prefer itemNos.")]
        [JsonPropertyName("itemNo")]
        public string ItemNo { get; set; } = "";

        [Description("Restrict to one item category. A FILTER, not a list — it covers the whole category however many items that is, and is the cheapest way to scope a bulk run.")]
        [JsonPropertyName("itemCategoryCode")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("Partial item description. A FILTER: it covers every Inventory item whose description matches, with no cap.")]
        [JsonPropertyName("descriptionContains")]
        public string DescriptionContains { get; set; } = "";

        [Description("Scope by any FIELD on the item card, as \"Field=Value\" entries \u2014 e.g. [\"E-Commerce Item=Yes\"], [\"Vendor No.=V00010\"], [\"Blocked=No\"]. Several entries are ANDed. This is how you scope \"every item flagged for X\" WITHOUT listing the items: the gateway resolves the set server-side. Field names are whatever describe_entity shows for Item (name or caption). Use this instead of building an item list \u2014 a list of thousands cannot be passed through a tool argument at all.")]
        [JsonPropertyName("itemFilters")]
        [JsonConverter(typeof(TolerantListConverter<string>))]
        public List<string> ItemFilters { get; set; } = new();

        [Description("Single LS Central store number to scope to, e.g. S004.")]
        [JsonPropertyName("storeNo")]
        public string StoreNo { get; set; } = "";

        [Description("Store numbers to scope to. Omit to cover every store — for this tool that is normal, and costs no extra gateway calls.")]
        [JsonPropertyName("storeNos")]
        [JsonConverter(typeof(TolerantListConverter<string>))]
        public List<string> StoreNos { get; set; } = new();

        [Description(BcToolDescriptions.StoreNameContains)]
        [JsonPropertyName("storeNameContains")]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. Restrict to a single location code.")]
        [JsonPropertyName("locationCode")]
        public string LocationCode { get; set; } = "";

        [Description("Optional. Restrict to a single item variant code.")]
        [JsonPropertyName("variantCode")]
        public string VariantCode { get; set; } = "";

        [Description("When true, break results down per item variant. Defaults to false.")]
        [JsonPropertyName("byVariant")]
        public bool ByVariant { get; set; }

        [Description("Breakdown: 'item' (default — ONE row per item, its stock summed over every store in scope) or 'detail' (one row per item PER STORE). detail over a large scope is the expensive shape: 25,000 items across 73 branches is up to 1.8 million rows. Ask for item unless the answer genuinely needs the per-branch split.")]
        [JsonPropertyName("groupBy")]
        public string GroupBy { get; set; } = "";

        [Description("When true, include rows with no inventory activity. Defaults to false — the rows you get back are the ones that have stock or movement.")]
        [JsonPropertyName("includeZero")]
        public bool IncludeZero { get; set; }

        [Description("Resume a run that did not finish inside waitSeconds: pass the runId the previous attempt reported, with the SAME scope arguments. Leave empty to start a new run.")]
        [JsonPropertyName("runId")]
        public string RunId { get; set; } = "";

        [Description("How long to drive the run before handing back a resumable runId, in seconds (default 240, max 540). It is not a limit on the run — it is how long THIS call waits.")]
        [JsonPropertyName("waitSeconds")]
        public int WaitSeconds { get; set; }

        [Description("Abort the run if it would produce more than this many rows (default 1,000,000). A guard against a detail run over a scope nobody meant to ask for.")]
        [JsonPropertyName("maxRows")]
        public int MaxRows { get; set; }
    }

    public class Row
    {
        [Description("Item number.")]
        public string ItemNo { get; set; } = "";

        [Description("Variant code, when byVariant is set.")]
        public string VariantCode { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Item category code.")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("Store number. Only present on a groupBy 'detail' row; an 'item' row is the total across every store in scope.")]
        public string StoreNo { get; set; } = "";

        [Description("Store name (detail rows).")]
        public string StoreName { get; set; } = "";

        [Description("Location code (detail rows).")]
        public string LocationCode { get; set; } = "";

        [Description("Location name (detail rows).")]
        public string LocationName { get; set; } = "";

        [Description("Physical on-hand inventory component.")]
        public decimal PhysInventory { get; set; }

        [Description("Unposted POS sales component (already signed as on the standard page).")]
        public decimal TotalSales { get; set; }

        [Description("Posted POS sales component.")]
        public decimal PostedSales { get; set; }

        [Description("Total inventory adjustments component.")]
        public decimal TotalInvAdjmt { get; set; }

        [Description("Posted inventory adjustments component.")]
        public decimal PostedInvAdjmt { get; set; }

        [Description("Click & collect reservation component.")]
        public decimal CoResEntries { get; set; }

        [Description("Net (available) inventory for this row.")]
        public decimal NetInventory { get; set; }

        [Description("Always 'live' — a bulk run is computed from the ledger, never read from the snapshot.")]
        public string Source { get; set; } = "";
    }

    // Wire shape of the run envelope (RunBulkStock / ContinueBulkStock / the page).
    private class RunEnvelope
    {
        public string RunId { get; set; } = "";
        public string Status { get; set; } = "";
        public bool Complete { get; set; }
        public int ItemCount { get; set; }
        public int ChunksTotal { get; set; }
        public int ChunksDone { get; set; }
        public int RowCount { get; set; }
        public string LastError { get; set; } = "";
    }

    private sealed class PageEnvelope : RunEnvelope
    {
        public int Count { get; set; }
        public int Top { get; set; }
        public int Skip { get; set; }
        public bool HasMore { get; set; }
        public long? TotalRows { get; set; }
        public List<Row> Rows { get; set; } = new();
    }

    public override async Task<DatasetPage<Row>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.inventory.bulk_stock";

        var hasScope =
            !string.IsNullOrWhiteSpace(args.ItemNo) ||
            args.ItemNos.Any(n => !string.IsNullOrWhiteSpace(n)) ||
            args.ItemFilters.Any(f => !string.IsNullOrWhiteSpace(f)) ||
            !string.IsNullOrWhiteSpace(args.ItemCategoryCode) ||
            !string.IsNullOrWhiteSpace(args.DescriptionContains);

        // The whole point of this tool is an ITEM scope. Without one the question is a
        // branch rollup, which net_inventory answers from the snapshot in milliseconds —
        // sending it here would compute the entire catalogue the slow way instead.
        if (!hasScope)
            throw new ToolValidationException(
                ToolKey,
                "bulk_stock needs an item scope: itemFilters, itemNos, itemCategoryCode or descriptionContains. " +
                "For per-branch totals over the whole catalogue use net_inventory with groupBy 'store'.");

        var pageSize = cursor.PageSize > 0 ? cursor.PageSize : BcPaging.SampleRows;

        // A continuation page: the run is already computed, so this is a read.
        if (cursor.Token is not null)
        {
            var (cursorRunId, offset) = ParseCursor(cursor.Token, ToolKey);
            var next = await FetchStoredPageAsync(cursorRunId, args.Country, pageSize, offset, ToolKey, ctx);
            return ToDatasetPage(next, cursorRunId, offset);
        }

        var runId = string.IsNullOrWhiteSpace(args.RunId)
            ? Guid.NewGuid().ToString("N")
            : args.RunId.Trim();

        var waitSeconds = args.WaitSeconds > 0 ? Math.Min(args.WaitSeconds, 540) : 240;
        var watch = Stopwatch.StartNew();

        var env = await StartOrResumeAsync(args, runId, ToolKey, ctx);

        // Drive the run to completion in short gateway calls. Each one commits what it
        // computed, so this loop never repeats work — and an abandoned call leaves a
        // run that a later call can resume rather than a rollback.
        while (!env.Complete)
        {
            if (string.Equals(env.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                throw new ToolValidationException(
                    ToolKey, $"Bulk stock run {runId} failed: {env.LastError}");

            if (watch.Elapsed.TotalSeconds >= waitSeconds)
                throw new ToolValidationException(
                    ToolKey,
                    $"Bulk stock run {runId} is still computing: {env.ChunksDone} of {env.ChunksTotal} " +
                    $"chunks done over {env.ItemCount} items, {env.RowCount} rows so far. Nothing is lost — " +
                    $"call bulk_stock again with runId '{runId}' and the same scope to carry on from here.");

            env = await BcClient.ExecuteAsync<RunEnvelope>(
                "ContinueBulkStock", ContinuePayload(runId, args.Country), ToolKey, ctx);
        }

        var page = await FetchStoredPageAsync(runId, args.Country, pageSize, 0, ToolKey, ctx);
        return ToDatasetPage(page, runId, 0);
    }

    private static async Task<RunEnvelope> StartOrResumeAsync(
        Args args, string runId, string toolKey, ToolContext ctx)
    {
        // Resuming: the run already carries its scope, so re-sending it would be at best
        // ignored and at worst a second run under the same id.
        if (!string.IsNullOrWhiteSpace(args.RunId))
            return await BcClient.ExecuteAsync<RunEnvelope>(
                "ContinueBulkStock", ContinuePayload(runId, args.Country), toolKey, ctx);

        var payload = BcPaging.WithPaging(args, 0, 0);
        payload.Remove("top");
        payload.Remove("skip");
        payload.Remove("runId");
        payload.Remove("waitSeconds");
        payload["runId"] = runId;
        payload["budgetSeconds"] = ChunkBudgetSeconds;

        return await BcClient.ExecuteAsync<RunEnvelope>("RunBulkStock", payload, toolKey, ctx);
    }

    private static async Task<PageEnvelope> FetchStoredPageAsync(
        string runId, string country, int top, int skip, string toolKey, ToolContext ctx)
        => await BcClient.ExecuteAsync<PageEnvelope>(
            "GetBulkStockPage",
            WithCountry(new JsonObject { ["runId"] = runId, ["top"] = top, ["skip"] = skip }, country),
            toolKey, ctx);

    private static JsonObject ContinuePayload(string runId, string country)
        => WithCountry(
            new JsonObject { ["runId"] = runId, ["budgetSeconds"] = ChunkBudgetSeconds }, country);

    // BcClient routes to a company from the payload's `country` key. A run lives in ONE
    // company's tables, so every follow-up call has to name the same one — without this
    // a resume or a page goes to the default company and reports the run as unknown.
    private static JsonObject WithCountry(JsonObject payload, string country)
    {
        if (!string.IsNullOrWhiteSpace(country))
            payload["country"] = country;
        return payload;
    }

    private static DatasetPage<Row> ToDatasetPage(PageEnvelope page, string runId, int offset)
        => new()
        {
            Rows       = page.Rows,
            NextCursor = page.HasMore ? $"{runId}|{offset + page.Rows.Count}" : null,
            Total      = page.TotalRows,
        };

    // "<runId>|<offset>" — the run id has to travel with the cursor, because a page of a
    // bulk result is a read from that run's rows and nothing else identifies them.
    private static (string RunId, int Offset) ParseCursor(string token, string toolKey)
    {
        var parts = token.Split('|');
        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            !int.TryParse(parts[1], out var offset) ||
            offset < 0)
            throw new ToolValidationException(toolKey, $"Invalid pagination cursor '{token}'.");
        return (parts[0], offset);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.inventory_snapshot_status
//
// Until this existed, nothing outside Business Central could tell whether the
// inventory snapshot had ever been built. It had two triggers, both manual and
// both outside the product — a Job Queue Entry someone had to create by hand, or
// a direct API call — so the cache stayed empty, `source: auto` fell back to
// computing live on every branch rollup, and there was no error anywhere to say
// why the fast path never seemed to arrive.
// ---------------------------------------------------------------------------

[Tool(
    Key         = "erp_bc.inventory.inventory_snapshot_status",
    Description = "Whether the precomputed branch-inventory snapshot exists, how old it is, and how the last rebuild went. Use it when a branch rollup came back slowly or carried a snapshotStatus note, when someone asks how current the stock figures are, or before promising an instant answer. hasBuild=false means every branch rollup is being computed live (slow) and the nightly rebuild job has not run — that is an operations problem to report, not something to retry. asOf is the moment the current build started, which is the time to quote alongside any snapshot figure.",
    Sensitivity = "read")]
public class GetInventorySnapshotStatus : ToolHandler<GetInventorySnapshotStatus.Args, GetInventorySnapshotStatus.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        [JsonPropertyName("country")]
        public string Country { get; set; } = "";
    }

    public class Result
    {
        [Description("True when a usable build exists. False means branch rollups are being computed live.")]
        public bool HasBuild { get; set; }

        [Description("The build readers are currently served from. 0 = never built.")]
        public int BuildNo { get; set; }

        [Description("When the current build's figures were true — the moment its rebuild STARTED. Quote this alongside any snapshot figure.")]
        public string AsOf { get; set; } = "";

        [Description("How many minutes old the current build is. -1 when never built.")]
        public int AgeMinutes { get; set; }

        [Description("Idle, Staging, Computing, Complete or Failed.")]
        public string Status { get; set; } = "";

        [Description("True while a rebuild is in flight; readers keep being served the previous build meanwhile.")]
        public bool Running { get; set; }

        [Description("When the last rebuild started.")]
        public string BuildStartedAt { get; set; } = "";

        [Description("When the last rebuild finished.")]
        public string BuildFinishedAt { get; set; } = "";

        [Description("How long the last rebuild took, in milliseconds.")]
        public long BuildDurationMs { get; set; }

        [Description("Of that time, how long was spent staging the item scope (one insert per item).")]
        public long StagingMs { get; set; }

        [Description("Of that time, how long was spent on the six component reads per chunk.")]
        public long ReadMs { get; set; }

        [Description("Of that time, how long was spent writing rows. Together with stagingMs and readMs this says which part to fix when a rebuild is slow.")]
        public long WriteMs { get; set; }

        [Description("How many items the last rebuild covered.")]
        public int ItemCount { get; set; }

        [Description("Chunks in the last rebuild, and how many completed. Unequal means it stopped early.")]
        public int ChunksTotal { get; set; }

        [Description("Chunks completed in the last rebuild.")]
        public int ChunksDone { get; set; }

        [Description("Rows in the current build (stores plus store × category).")]
        public int Rows { get; set; }

        [Description("Who or what ran the last rebuild.")]
        public string LastRunBy { get; set; } = "";

        [Description("The last rebuild's error, empty when it succeeded. Report it verbatim — it is an operations problem, not a data question.")]
        public string LastError { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
        => await BcClient.ExecuteAsync<Result>(
            "GetInventorySnapshotStatus",
            JsonSerializer.SerializeToNode(args, BcClient.JsonOptions)!.AsObject(),
            "erp_bc.inventory.inventory_snapshot_status", ctx);
}
