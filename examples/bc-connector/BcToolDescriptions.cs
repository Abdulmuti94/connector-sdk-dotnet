namespace BcConnector;

/// <summary>Shared tool-schema descriptions shown to the model.</summary>
internal static class BcToolDescriptions
{
    public const string Country =
        "Optional country: SA (Saudi Arabia, default), KW (Kuwait), OM (Oman), QA (Qatar), AE (UAE). " +
        "Omit when unsure — the connector auto-routes from entity numbers (e.g. ICP-ASG-UAEAD → AE). " +
        "Do not pass SA for UAE/KW/OM/QA customers.";

    public const string LookupCustomer =
        "Preferred for customer questions. Look up by customer number OR search by partial name in one call. When exactly one match is found, returns the full customer card (same as get_customer). When several match, returns resolved=false with a short candidate list — ask the user to pick or narrow the name.";

    public const string LookupItem =
        "Preferred for item questions. Look up by item number OR search by partial description in one call. When exactly one match is found, returns the full item card (same as get_item). When several match, returns resolved=false with candidates.";

    public const string LookupVendor =
        "Preferred for vendor questions. Look up by vendor number OR search by partial name in one call. When exactly one match is found, returns the full vendor card (same as get_vendor). When several match, returns resolved=false with candidates.";

    public const string IncludeBalance =
        "Optional. When true, include balance fields in search results (slower). Default false — use lookup_* or get_* when balance is needed.";

    public const string IncludeInventory =
        "Optional. When true, include on-hand inventory in search results (slower). Default false — use lookup_item or get_item when stock is needed.";

    public const string StoreNameContains =
        "Optional. Partial store name to resolve to Store No. and location (e.g. 'مخرج 9', 'Exit 9', 'اليرموك'). " +
        "The connector maps this to the LS Central store code (e.g. S004) before querying. " +
        "Prefer this when the user names a branch; use storeNo only when the code is already known.";

    public const string StoreNo =
        "Optional LS Central store number (e.g. S004). You may also pass a partial store name here — " +
        "the connector resolves it to the store code automatically.";

    public const string RunSql =
        "Run a single read-only T-SQL SELECT directly against the Business Central SQL Server for " +
        "ad-hoc analytics the other data tools can't express (joins, GROUP BY, window functions). " +
        "Read-only: only one SELECT is allowed. Prefer count_records / sum_records / query_records when " +
        "they fit; use this only for genuinely ad-hoc queries.";

    // Catalog shown to the model so it writes valid SQL. Keep this in sync as you add curated views.
    // TODO: once the read-only 'ai' view schema exists, list the views/columns here and set
    // BC_SQL_ALLOWED_SCHEMAS=ai so queries are restricted to them.
    public const string SqlCatalog =
        "The database is Microsoft Dynamics 365 Business Central / LS Central (on-prem SQL Server). " +
        "Company tables are named '[<Company>$<Table>$<id>]', e.g. [ASG$Customer$437dbf0e-...]; each " +
        "company has its own set of tables. Prefer the curated read-only views in the 'ai' schema when " +
        "available (e.g. ai.vCustomers, ai.vSalesLines). IMPORTANT: BC FlowFields (e.g. Customer " +
        "\"Balance (LCY)\", item Inventory) are NOT stored columns — they read as 0/empty in raw tables; " +
        "use the ai.* views (which resolve them) or the entity/gateway tools for those values.";
}
