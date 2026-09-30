namespace BcConnector;

/// <summary>Shared tool-schema descriptions shown to the model.</summary>
internal static class BcToolDescriptions
{
    public const string Country =
        "Optional country: SA (Saudi Arabia, default), KW (Kuwait), OM (Oman), QA (Qatar), AE (UAE). " +
        "Omit when unsure — the connector auto-routes from entity numbers (e.g. ICP-ASG-UAEAD → AE). " +
        "Do not pass SA for UAE/KW/OM/QA customers. " +
        "Each country is a separate company with its OWN local currency (SA=SAR, KW=KWD, OM=OMR, " +
        "QA=QAR, AE=AED). NEVER add amounts from different countries into one total — report each " +
        "country's amount in its own currency, or list them side by side.";

    public const string LookupCustomer =
        "Preferred for customer questions. Look up by customer number OR search by partial name in one call. When exactly one match is found, returns the full customer card (same as get_customer). When several match, returns resolved=false with a short candidate list — ask the user to pick or narrow the name.";

    public const string LookupItem =
        "Preferred for item questions. Look up by item number, BARCODE (scanned/typed digit string — pass it as barcode), OR partial description in one call. When exactly one match is found, returns the full item card (same as get_item) — including matchedBarcode with the unit that barcode identifies (a barcode can mean a dozen, not a piece). When several match, returns resolved=false with candidates. An unknown itemNo is retried as a barcode automatically. unitPrice here EXCLUDES VAT and ignores store price groups and offers — for what the item costs at the till use get_item_prices.";

    // Intercompany transactions. Shared with every agent, so it has to explain
    // the two-sided model (outbox here, inbox there) on its own.
    public const string FindIntercompanyTransactions =
        "INTERCOMPANY (IC) transactions between the ASG companies (SA head office, KW, OM, QA, AE — IC partners ICP-ASG-KSA / KWT / OM / QA / UAE). Lists the current company's IC OUTBOX (documents it sent, or must still send, to a partner) and IC INBOX (documents received from a partner), open and handled: partner, source document (sales order SO-…, posted invoice PSI-…, credit memo, journal), dates and status. Use it for 'did our invoice/order reach Qatar', 'what is pending in the IC outbox', 'what did we receive from head office', 'IC transactions with Kuwait this month'. country picks WHOSE boxes are read (SA = what head office sent/received; QA = Qatar's side). An intercompany SALE is an ordinary sales order on the ICP-* customer (Sales agent: create_sales_order → add_sales_order_line → post_sales_order) — BC flags it as an IC document itself; posting writes the outbox transaction, which here must be SENT by a person (autoSendTransactions=false) before it appears in the partner's inbox, where accepting it creates the purchase document. Returns the full list (top, default 50, max 200) — present every row; hasMore means narrow or page with skip.";

    // The price check. Shared with every agent (Agents = "*" on the tool), so the
    // description has to stand on its own for an agent that knows nothing about
    // LS Central pricing: it says what the two prices are, which offers are and
    // are not inside them, and that the store matters.
    public const string GetItemPrices =
        "PRICE CHECK — what an item costs at the till. Returns per item: itemNo, barcode, description, vendorItemNo, the regular shelf price INCLUDING VAT (priceInclVat, resolved by LS Central for the store's price groups — NOT the item card unitPrice, which excludes VAT), and priceAfterDiscountInclVat: the price the customer pays for ONE unit today after the Disc. Offer and automatic Line Discount promotions the POS would apply (offerNo / lineDiscountOfferNos say which). Use it for ANY 'how much is / what's the price of / كم سعر' question and for price lists — never compute a price from unitPrice yourself. Scope with itemNo, itemNos (up to 500 in ONE call), barcode (priced in the barcode's unit), offerNo (every item on an offer from the Discount Offer / Line Discount Offer / Mix&Match Offer lists), or a filter (descriptionContains, itemCategoryCode, retailProductGroupCode, vendorNo). Prices are STORE-SPECIFIC: pass storeNo or storeNameContains when the user names a branch; otherwise the head-office store is used and every row says which store priced it. Mix&Match / Multibuy offers need a basket, so they are listed in basketOffers and NOT applied. Returns the FULL LIST in rows[] (up to top rows, default 100, max 300 — not a sample): when the user asked about several items, present EVERY row, not just the first; hasMore=true means the scope holds more than that, so say the list is partial and narrow it or call again with skip. When the user asks WHY there is no discount, or why an offer visible in BC does not show in the price, call again with explain=true and relay offerDiagnostics — it names every offer covering the item and the rule that applied or blocked it. Every amount carries currencyCode — state it.";

    // ---------------------------------------------------------------------
    // Editing. Every optional argument is nullable on purpose: omitted means
    // "leave alone", supplied-but-empty means "clear". The model has to be told
    // that, or it will send every field it happens to know and overwrite things
    // the user never mentioned.
    // ---------------------------------------------------------------------

    public const string EditOmitSemantics =
        "Only send the fields you are actually changing. A field you omit is left exactly as it is; " +
        "a field you send empty is CLEARED. Never re-send values you just read back from the record " +
        "\u2014 that is how unrelated fields get overwritten.";

    // Gateway 1.12.0.0 (HandleCreateTransferOrder / ApplyTransferRegionBranch). The
    // rules are LS Central's: the header takes the Store-from card's Global
    // Dimension 1 Code, and the shipment posting re-applies it.
    public const string TransferRegionBranch =
        "Region/branch (Shortcut Dimension 1 Code, dimension REGION/BRANCH): the region code, a dash and " +
        "the branch, e.g. 1100-S068 = Central Area, store S068. Every transfer order must carry one, and it " +
        "is the SOURCE branch's \u2014 LS Central switches the order to the destination branch itself when " +
        "the receipt posts. Leave it EMPTY when the source location is a store (L068): it comes from the " +
        "store card, and a different value is refused because LS re-applies the store's value when the " +
        "shipment posts. For a store's sub-location (L068-D, L068-M, L997-DAM) the owning store's value is " +
        "used unless you pass another. Pass it when the source is not tied to a store (MK-PLACE, HO, L072 " +
        "\u2026) \u2014 the order is refused without it then, and the error says why. A region heading " +
        "such as 1100 is not a branch and is refused, as is a blocked (closed) branch.";

    public const string UpdateTransferOrder =
        "Correct the header of an existing transfer order \u2014 dates, locations, external document " +
        "number, shipping agent, region/branch (shortcutDimension1Code). " + EditOmitSemantics + " " +
        "Business Central only allows a RELEASED order's externalDocumentNo to change; everything else " +
        "needs the order reopened first. The source, destination and in-transit locations cannot change " +
        "once any line has shipped, and neither can the region/branch. Changing a location, a date, the " +
        "shipping agent or the region/branch CASCADES to every line, and shipment and receipt dates " +
        "recalculate each other through the transfer route \u2014 re-read the order afterwards if exact " +
        "line dates matter. A new source location brings its own region/branch with it. An order with an " +
        "empty shortcutDimension1Code (made before the gateway set one) is repaired by passing it: for a " +
        "store source it must be that store's value, and any other value is refused with the right one " +
        "named. The repair also fills in storeFrom / storeTo, which LS needs to post the shipment and the " +
        "receipt to the right branches. To change quantities use update_transfer_order_line.";

    public const string UpdateTransferOrderLine =
        "Correct one line of a transfer order \u2014 quantity, variant, its dates, or its BINS (transferToBinCode / transferFromBinCode, e.g. to set TRENDYOL-MDA on a line going to MK-PLACE). " + EditOmitSemantics + " " +
        "A quantity can never go BELOW what has already shipped on that line; the error tells you the " +
        "floor. The order must be Open. To remove a line entirely use delete_transfer_order_line; to " +
        "add one, add_transfer_order_line. The item on a line cannot be swapped \u2014 delete the line and " +
        "add the right one.";

    public const string DeleteTransferOrderLine =
        "Remove ONE line from a transfer order, leaving the order itself in place. Applies the same " +
        "rules as deleting the whole order, to that line: stock SHIPPED BUT NOT RECEIVED, a reservation, " +
        "or an open warehouse document all block it, and the order must be Open. Pass dryRun=true to " +
        "check without deleting. Deleting the last line leaves an empty order that cannot be posted \u2014 " +
        "the response says so; use delete_transfer_order if the whole order should go.";

    public const string UpdateItem =
        "Correct an existing item card \u2014 description, vendor, weights, GTIN, sales/purchase unit, or " +
        "which department it is filed under. " + EditOmitSemantics + " " +
        "Some things are deliberately NOT editable here and the tool will tell you which to use instead: " +
        "price is set_item_price (it writes the price list too), blocking is release_item, and the item " +
        "number, base unit of measure, type and costing method cannot change on an item that exists. " +
        "Re-filing an item under a different category does NOT renumber it \u2014 CAT01-005534 keeps its " +
        "number even when moved to CAT02. itemCategoryCode and retailProductGroupCode must agree; change " +
        "the product group alone and the category follows it automatically.";

    // ---------------------------------------------------------------------
    // Transfer order deletion. The refusal cases matter more than the happy
    // path, so they are on the tool rather than left to the agent prompt.
    // ---------------------------------------------------------------------

    public const string DeleteTransferOrder =
        "Delete a transfer order and all its lines. IRREVERSIBLE \u2014 the order would have to be " +
        "created again from scratch, so confirm with the user before calling this without dryRun. " +
        "Business Central refuses the delete in several cases and so does this tool, with a " +
        "`blockers` list explaining each one: stock already SHIPPED BUT NOT RECEIVED (it is sitting " +
        "in the in-transit location and would be stranded \u2014 receive it first with " +
        "post_transfer_order postType=Receive), a line that is RESERVED, an open WAREHOUSE document " +
        "(pick, put-away, receipt or shipment), or an order that is RELEASED rather than Open " +
        "(pass reopenIfReleased=true to reopen and delete in one step). " +
        "Pass dryRun=true to find out whether an order can be deleted without deleting it \u2014 use " +
        "that when the user is only asking. A fully shipped AND received order no longer exists: " +
        "posting removes it, so 'not found' can mean it completed. ONE ORDER ONLY: every call needs " +
        "the user's approval, so for two or more orders use delete_transfer_orders (one approval for " +
        "the whole list) and check_transfer_order_deletion (no approval) instead of calling this " +
        "repeatedly.";

    public const string CheckTransferOrderDeletion =
        "Check up to 50 transfer orders at once for deletion \u2014 which could be deleted now, and what " +
        "blocks the rest (stock SHIPPED BUT NOT RECEIVED, a RESERVATION, an open WAREHOUSE document, " +
        "RELEASED status) \u2014 with exactly delete_transfer_order's rules. CHANGES NOTHING and needs no " +
        "approval, so use it before deleting several orders, and whenever the user only asks whether " +
        "orders can be deleted. Pass explicit transferOrderNos (find_transfer_orders gives them). Returns " +
        "deletableOrderNos \u2014 the list to hand to delete_transfer_orders once the user agrees \u2014 and " +
        "one entry per order with its blockers.";

    public const string DeleteTransferOrders =
        "Delete up to 50 transfer orders in ONE call, and so with ONE approval from the user instead of " +
        "one per order. IRREVERSIBLE. Flow: check_transfer_order_deletion first; tell the user which " +
        "orders can be deleted and why any cannot; once they agree, call this with that list " +
        "(deletableOrderNos). The approval shows every number in transferOrderNos, and exactly that list " +
        "is what runs. Explicit order numbers only \u2014 never a filter. Each order is checked and " +
        "deleted on its own with delete_transfer_order's rules: every order that can still be deleted " +
        "IS deleted, and any that cannot (blocked, not found, or refused by Business Central) is left " +
        "alone and reported in orders[] with the reason \u2014 one order failing never undoes another. " +
        "Report deletedCount and deletedOrderNos, never the size of the list, and relay every order " +
        "that was NOT deleted with its detail. reopenIfReleased applies to the whole list. More than 50: " +
        "split into batches of 50, one approval each.";

    public const string AvailableInventory =
        "Available-to-send stock: net inventory MINUS what is already committed to outbound " +
        "transfer orders from that location. Use this whenever the question is whether stock can " +
        "actually be given away \u2014 promising an order, sending a transfer, answering \"can we spare " +
        "any\" \u2014 because net_inventory still counts units that are physically on the shelf but already " +
        "promised to another branch. Same arguments, filters and groupings as net_inventory, and it " +
        "returns everything net_inventory does plus toReservedQuantity (the committed outbound " +
        "quantity, subtracted), availableInventory (the number to quote), and toIncomingQuantity " +
        "(on its way IN \u2014 reported for context, NEVER subtracted). This reproduces the " +
        "\"Available Item By Location - ASG\" report, so the figure matches what the branches see. " +
        "Reach for plain net_inventory only when the question is about stock on hand rather than " +
        "stock that can be committed. The grouping and cost rules are net_inventory's: groupBy " +
        "'total' is ONE row for the whole scope (never a row per store — that is groupBy " +
        "'store'), and a call costs roughly (stores in scope) x (items in scope), so an all-items " +
        "rollup over many branches will time out. Narrow the item scope rather than calling once " +
        "per branch. Unlike net_inventory this is ALWAYS computed live and is never served from the " +
        "inventory snapshot: it subtracts transfer commitments that change by the minute, and a " +
        "stale net minus a live commitment is a number that means nothing.";

    public const string BulkStock =
        "EXACT live stock for a LARGE set of items \u2014 thousands of them \u2014 in one run, optionally " +
        "broken down per branch. This is the tool for \"the stock of these 25,000 items\", a category-wide " +
        "or catalogue-wide item list, a replenishment or allocation export. Scope it by NAMING the set, " +
        "never by listing it: itemFilters ([\"E-Commerce Item=Yes\"] \u2014 any field on the item card, " +
        "ANDed), itemCategoryCode, descriptionContains, or itemNos when the caller genuinely handed you " +
        "the numbers. NEVER assemble an item list yourself (run_sql, code_interpreter, a file) to feed " +
        "itemNos: thousands of item numbers do not fit in a tool argument, and itemFilters resolves the " +
        "same set server-side in one call. Optionally scope storeNos / locationCode; " +
        "omitting the store scope covers every branch and costs nothing extra. groupBy 'item' (the " +
        "default) returns ONE row per item, its stock summed over every store in scope; groupBy 'detail' " +
        "returns one row per item PER STORE, which over a large scope can be a million rows \u2014 ask for it " +
        "only when the per-branch split is the answer \u2014 it writes one row per item per branch, so a " +
        "catalogue-sized scope is rejected up front rather than run for twenty minutes. \"\u062d\u0635\u0631 \u0645\u062e\u0632\u0648\u0646\" / " +
        "\"how much of each item do we hold\" is groupBy 'item'. Returns a dataset: a sample plus a dataset_ref, so " +
        "the full set is exported or computed over rather than read into the conversation. The figures " +
        "are computed from the ledger at run time (never from the snapshot) and every row says " +
        "source='live'. It takes MINUTES for a large scope, and the work is done server-side in one pass " +
        "per chunk \u2014 never call it once per item, and never fall back to net_inventory in a loop. If a " +
        "call reports the run is still computing, call again with the runId it gives you and the same " +
        "scope: the run resumes where it stopped and nothing is recomputed. Choosing between the three " +
        "stock tools: a few items or a few branches \u2192 net_inventory; what a BRANCH holds in total, or " +
        "per category \u2192 net_inventory groupBy store/category (instant, from the snapshot); what these " +
        "MANY NAMED ITEMS hold \u2192 this tool. For anything about to be promised, sent or sold, use " +
        "available_inventory instead \u2014 bulk_stock is net inventory and does not subtract outbound " +
        "transfer commitments.";

    public const string ItemType =
        "Item type: Inventory, Service, or Non-Inventory. ONLY Inventory items carry real stock \u2014 " +
        "Service and Non-Inventory items always show zero on hand, so never report them as out of " +
        "stock, and never suggest reordering or replenishing them.";

    // ---------------------------------------------------------------------
    // Item creation. The numbering rule below is the single most important
    // thing for the model to get right, so it is stated on the tool itself
    // rather than left to the agent prompt.
    // ---------------------------------------------------------------------

    public const string CreateItem =
        "Create a new item. The item number is NOT chosen by you \u2014 it is drawn from the number " +
        "series belonging to the item's category (category CAT17 numbers items CAT17-011627), so pass " +
        "itemCategoryCode or retailProductGroupCode and let the number come back in the response. " +
        "Use list_item_categories first when you do not know which department the item belongs to, or " +
        "get_item on a similar item and reuse its itemCategoryCode / retailProductGroupCode. " +
        "The category's template supplies the posting groups, type and costing method automatically. " +
        "This also creates the base unit of measure, plus the barcode and the ALL-price-group price " +
        "when you supply them \u2014 one call produces a usable item. " +
        "The item is created SALES-BLOCKED: report the number to the user and tell them it must be " +
        "checked and released with release_item before it can be sold. " +
        "Items can only be created in the head-office company that owns the numbering (SA); the other " +
        "countries hold replicated copies and the tool will refuse there.";

    public const string ListItemCategories =
        "List the item categories (departments) items can be created under \u2014 code, Arabic description, " +
        "the number series each one numbers items from, the next number it will hand out, and " +
        "optionally its LS Central retail product groups. Use this before create_item to pick the right " +
        "department, or to answer 'what number will the next item in this department get'. " +
        "creatable=false means the category has no number series and create_item will refuse it.";

    public const string SetItemPrice =
        "Set an item's selling price \u2014 both the item card's Unit Price and its row on a customer " +
        "price group (ALL = 'All Stores' by default, which is the base price every store falls back to). " +
        "Prices here are stored EXCLUDING VAT: pass retailPriceInclVat when the user quotes a shelf " +
        "price and the VAT is removed using the item's own VAT rate, or unitPriceExclVat when the " +
        "figure is already net. The response shows both so you can confirm the right one was used.";

    public const string ReleaseItem =
        "Release an item for sale, or block it again. Items made by create_item start SALES-BLOCKED, " +
        "and this is the deliberate step that makes one sellable \u2014 only do it when the user has " +
        "confirmed the price and details are right. Also used to block an item that should no longer " +
        "be sold or purchased (items with history cannot be deleted, only blocked).";

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

    // Item types — not every Item is stocked. Only Type = Inventory items have real on-hand
    // quantity; Service / Non-Inventory items (frequently the S-prefix codes here) carry no stock,
    // so stock/best-seller-to-reorder questions must exclude them. Appended to run_sql/query_records.
    public const string ItemTypes =
        "ITEM TYPES: not every Item is stocked. The Item [Type] field is an option: Inventory, " +
        "Service, or Non-Inventory (stored in the raw SQL tables as the integer 0=Inventory, " +
        "1=Service, 2=Non-Inventory). ONLY Type = Inventory items have real on-hand stock that can be " +
        "counted, replenished, or monitored. Service and Non-Inventory items have NO inventory — in " +
        "this environment the S-prefix item codes (e.g. S0001, S0053, S4020, S0056, S5025) are " +
        "typically Service items. When the question is about stock — top/fastest-selling items to " +
        "reorder or monitor, low stock, on-hand quantity — filter to Type = Inventory (e.g. WHERE " +
        "[Type] = 0 in SQL, or a Type=Inventory filter in query_records) and NEVER recommend " +
        "monitoring or replenishing stock for Service/Non-Inventory items, even when they rank high " +
        "in sales. Verify an item's Type before making any inventory recommendation about it.";

    // Company priority — the live company is 'ASG'. 'ASG - HData' is an old/legacy data company
    // and must never be the default. Appended to the data-tool descriptions (run_sql/query/count/sum)
    // so the model targets the right company, especially where it picks tables or iterates companies.
    public const string CompanyPriority =
        "COMPANY PRIORITY: always give priority to the 'ASG' company — it is the live/current data. " +
        "The 'ASG - HData' company holds OLD/legacy data and is NOT important: never target it, and " +
        "never include it in results, unless the user EXPLICITLY asks for the historical (HData) data.";

    // Service statuses — the ASG customization extends Business Central's four
    // service-document statuses with three of its own that describe a unit moving
    // between a branch and the service centre. They are ~14% of the live workload,
    // so the model has to know they exist; nothing in base BC would suggest them.
    public const string ServiceStatuses =
        "SERVICE ORDER STATUSES: Pending, In Process, Finished, On Hold, plus three ASG-specific " +
        "statuses — 'In-Transit To' (the unit has left the branch and is on its way to the service " +
        "centre), 'In-Transit From' (the repaired unit is on its way back to the branch), and " +
        "'Damage'. The two In-Transit statuses are a normal part of the repair flow here, not an " +
        "error state, and together they are a large share of the open workload — always include " +
        "them when reporting on the backlog, and treat 'still in transit for a long time' as a real " +
        "operational finding.";

    // Service Type is the single biggest trap in this domain: it reads like a
    // clean warranty filter but is blank on most rows, so filtering by it quietly
    // drops the majority of the data. Same treatment as ItemTypes.
    public const string ServiceTypeCaveat =
        "SERVICE TYPE IS OFTEN BLANK: the Service Type field (In Warranty / Out of Warranty / " +
        "External / Stores) is NOT reliably filled in. It is blank on roughly 4 in 10 service " +
        "orders and on the large majority of posted service invoices. Filtering by serviceType " +
        "therefore SILENTLY EXCLUDES most matching records: a warranty-vs-non-warranty split built " +
        "on it will understate every bucket. Use it only when the user explicitly asks about " +
        "warranty status, and when you do, say plainly that the figures cover only the records " +
        "where Service Type was recorded. Never present a serviceType breakdown as a complete " +
        "picture of the period.";

    // The repair unit and how the caller usually arrives at it.
    public const string ServiceItemNo =
        "Optional service item number — the 'SVI' code identifying the individual physical unit " +
        "being repaired (e.g. SVI-2500040123). This is the tracking number the customer's paperwork " +
        "carries. Use lookup_service_item when you only have a serial number, receipt number, or " +
        "phone number.";

    public const string ServiceStoreScoping =
        "Store / branch is the reliable way to scope service work: it is populated on virtually " +
        "every service order and posted service invoice. Pass a branch name (Arabic or English) or " +
        "an LS Central store code — the connector resolves names to the store code automatically. " +
        "Do NOT try to scope service work by Repair Status Code: that field exists on the tables " +
        "but is empty on every record in this environment, so it is not exposed as a filter.";

    public const string RunSql =
        "Run a single read-only T-SQL SELECT directly against the Business Central SQL Server. " +
        "This is the ONLY data tool that can JOIN entities, GROUP BY a dimension, rank / Top-N by a " +
        "computed or aggregated value, use DISTINCT / HAVING / window functions, or combine conditions " +
        "the simple {field, operator, value} filters cannot express (OR across fields, subqueries, " +
        "EXISTS, computed expressions). Reach for it when the answer needs any of those — e.g. 'sales " +
        "by item category', 'top 10 customers by revenue', 'items never sold', 'month-by-month trend'. " +
        "Do NOT use it for work the simpler tools already do: a plain row count (count_records), a plain " +
        "one-field total (sum_records — especially presets posSales / posNetSales), a simple 'list rows " +
        "where field = value' (query_records), an all-companies breakdown (count_records " +
        "allCompanies=true), or FlowField values such as Balance (LCY) / Inventory (use the ai.* views " +
        "or the entity tools — raw tables read these as 0). Read-only: exactly one SELECT, no writes. " +
        "Results are returned as a dataset: you see a sample of the rows plus a dataset_ref, and the " +
        "full result can be exported or analyzed on demand — so large row sets are safe, but ALWAYS " +
        "include a deterministic ORDER BY so the pages of the full set line up. " +
        "Unlike the other data tools there is NO country routing — you must target the correct company's " +
        "tables (or ai.* views) yourself.";

    // Catalog shown to the model so it writes valid SQL. Keep this in sync as you add curated views.
    // TODO: once the read-only 'ai' view schema exists, list the views/columns here and set
    // BC_SQL_ALLOWED_SCHEMAS=ai so queries are restricted to them.
    //
    // SCHEMA DISCOVERY GOES THROUGH THE PLATFORM, NOT THROUGH SQL. This text used to open with
    // "NEVER write a table name you have not seen returned by INFORMATION_SCHEMA" and hand the model
    // a SELECT ... FROM INFORMATION_SCHEMA.TABLES to run. It is attached to run_sql's own `Sql`
    // argument, i.e. read at the exact moment the model writes SQL, so it beat every other signal:
    // measured over the 7 days to 2026-08-17, erp_bc.data.run_sql took 13,251 calls (1,244 failed)
    // against 36 for search_schema and 19 for describe_entity. The platform meanwhile held an active,
    // fully embedded snapshot of this database — 2,932 entities, e.g. logical "Customer" -> 149
    // columns and its physical variants ASG$Customer$437dbf0e-... — so the connector was paying a
    // round trip per conversation to re-derive an answer that was already indexed.
    // INFORMATION_SCHEMA stays documented as the FALLBACK: the snapshot covers the ASG scope, and a
    // table outside it must still be discoverable without a dead end.
    public const string SqlCatalog =
        "The database is Microsoft Dynamics 365 Business Central / LS Central (on-prem SQL Server). " +
        "Company tables are named '[<Company>$<Table>$<id>]', e.g. [ASG$Customer$437dbf0e-...]; each " +
        "company has its own set of tables, and the company prefix and trailing <id> vary by " +
        "environment AND by table (base-application tables and each extension carry a different " +
        "app-id GUID). NEVER write a table name you have not seen resolved in THIS conversation — do " +
        "not assemble one from the BC table caption, and do not reuse a GUID you remember from " +
        "another table or another database. " +
        "FIND THE NAME WITH THE SCHEMA TOOLS FIRST: call search_schema with the question (it returns " +
        "the matching entities, their physical table names and join keys), then describe_entity for " +
        "one entity's full column list, and copy the physical name it gives you verbatim. That is " +
        "one indexed lookup instead of a discovery round trip, and it returns the columns too. Only " +
        "if search_schema and describe_entity return nothing for the table you need, fall back to a " +
        "discovery query in its own call: SELECT TOP 50 TABLE_SCHEMA, TABLE_NAME FROM " +
        "INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE '%<keyword>%' (then INFORMATION_SCHEMA.COLUMNS " +
        "for its columns). A guessed name fails with 'Invalid object name'; when that happens go back " +
        "to search_schema / describe_entity — never retry with another guessed name. " +
        "Column names are the BC field captions, NOT the OData/API field names: spaces are PRESERVED " +
        "and only '.' becomes '_' — e.g. the SQL columns are [Tender Type], [No_], [Phone No_], " +
        "[Posting Date], never Tender_Type / No / PhoneNo. Bracket any name containing a space or " +
        "special character. When a column is not obvious, read the real names from describe_entity " +
        "(or, failing that, INFORMATION_SCHEMA.COLUMNS) and copy them verbatim instead of guessing " +
        "an underscore form. " +
        "SIGN CONVENTION (LS Central POS tables): sales rows store NEGATIVE Quantity and amounts " +
        "([LSC Trans_ Sales Entry] Quantity/[Net Amount]/[Cost Amount], [LSC Transaction Header] " +
        "[Gross Amount]/[Net Amount]); returns are positive. For 'units sold' / 'sales value' " +
        "aggregate with -SUM(...) (e.g. ORDER BY -SUM(Quantity) DESC for best-sellers) — a raw " +
        "SUM ... DESC ranks returns first and reports sales as negative. " +
        "Prefer the curated read-only views in the 'ai' schema when " +
        "available (e.g. ai.vCustomers, ai.vSalesLines). IMPORTANT: BC FlowFields (e.g. Customer " +
        "\"Balance (LCY)\", item Inventory) are NOT stored columns — they read as 0/empty in raw tables; " +
        "use the ai.* views (which resolve them) or the entity/gateway tools for those values. " +
        "There is no country argument: pick the tables/views for the company you mean. " +
        CompanyPriority + " In SQL this means default to the '[ASG$...]' tables and NEVER query the " +
        "'[ASG - HData$...]' tables unless the user explicitly asks for the old data. A query spans " +
        "only the company tables you name — for an across-all-companies breakdown use count_records " +
        "allCompanies=true instead. Prefer TOP (plus ORDER BY for Top-N) or an aggregate when only a " +
        "small answer is needed; for genuinely large row sets rely on the dataset paging and give the " +
        "query a deterministic ORDER BY. " +
        "TENDER TYPES (payment method / طريقة الدفع): [LSC Trans_ Payment Entry] stores only the tender " +
        "[Tender Type] CODE. When reporting anything by tender (e.g. 'sales by tender'), JOIN " +
        "[LSC Tender Type] on [Store No_] AND [Code] = [Tender Type] and SELECT its [Description], then " +
        "ALWAYS present the tender by its description (e.g. Card, Voucher, قسيمة استرجاع فقط), never the " +
        "code alone. If a store-specific row is missing, fall back to any store's row for that code. " +
        ItemTypes;
}
