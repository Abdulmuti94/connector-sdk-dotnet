# Business Central Connector — .NET example

A runnable Vested AI connector for **on-prem Dynamics 365 Business Central**,
demonstrating the core `VestedAI.ConnectorSdk` attribute API against a real ERP
backend through a **single custom API endpoint** (the *ASG AI Gateway*) with
NavUserPassword (basic auth).

The connector ships six agents and fifty-four tools. Instead of one published page
per object, every tool dispatches a named **operation** (`GetCustomer`,
`CreateSalesOrder`, …) to one gateway endpoint in BC, which routes it internally
and enforces business rules in AL. See the `ASG AI Gateway` AL extension's
`README.md` for the BC-side objects and wire contract.

---

## What this example demonstrates

| Feature | Where |
|---|---|
| `[Agent]` + `[Instruction]` attributes | `Agents.cs` |
| `[Tool]` with `Sensitivity` | `Tools.cs` |
| POCO `Args` / `Result` with `[Description]` | `Tools.cs` |
| `ToolHandler<TArgs, TResult>` base class | `Tools.cs` |
| `ToolValidationException` for not-found / backend errors | `Tools.cs`, `BcClient.cs` |
| One tool → one named operation dispatched to a single endpoint | `Tools.cs` |
| List results (search) returned as a typed `Result` from the gateway | `Tools.cs` |
| Calling a single custom API endpoint (bound action, basic auth) | `BcClient.cs` |
| Resolving the gateway record id per company and caching it | `BcClient.cs` |
| Process-wide HTTP client built from env at startup | `BcClient.cs`, `Program.cs` |
| `ConnectorHost.RunFromEnvironmentAsync` entrypoint | `Program.cs` |
| Multi-stage Docker build (SDK → runtime base) | `Dockerfile` |

> **Why a static client?** The SDK creates tool handlers fresh per call via
> `Activator.CreateInstance` and provides no DI container, so the BC client is a
> process-wide singleton (`BcClient`) configured once at startup and reused by
> every handler. A single `HttpClient` is shared for the process lifetime.

---

## Domain model

### Agents

| Agent key | Name | Purpose |
|---|---|---|
| `erp_bc.sales` | BC Sales | Customer look-ups; create / add lines / post / browse sales orders, including intercompany sales to the ICP-* partner companies |
| `erp_bc.inventory` | BC Inventory | Items (look up, create, update, barcodes, units, prices, release), stock (on hand, net, available, BOM), stores/locations, and transfer orders (create, add lines, post, update, delete) |
| `erp_bc.finance` | BC Finance & Accounting | AR/AP ledger entries, aging, posted invoices, G/L, bank balances; create bank accounts and staged journal batches/lines; intercompany outbox/inbox transactions |
| `erp_bc.purchasing` | BC Purchasing | Vendor look-ups; create / add lines / post / browse purchase orders, including intercompany purchases |
| `erp_bc.data` | BC Data & Analytics | Generic count, sum, query, SQL and company list across whitelisted entities. **Read-only** — hands writes to the owning agent |
| `erp_bc.retail` | BC Retail & POS | POS transactions, customer orders, retail sales totals and counts, loyalty members & points, promotions, price check; create offers & coupons |
| `erp_bc.service` | BC Service & Repairs | After-sales repair lifecycle: service orders, service items (SVI), posted service invoices & shipments, service analytics. **Read-only** |

**The Description on each `[Agent]` is the routing text.** The platform's orchestrator sees every
sub-agent as `key — description` and picks by that text alone (see
`OrchestratorCatalogInstruction` in vested-ai-core). So each description spells out the agent's
WRITES, not only its reads: when the Inventory agent's description said "answers item and stock
questions", a request to create a transfer order went to the Data agent, which has no write tools
and logged it as a capability gap. Every agent also carries `BcSharedInstructions.WriteOwners` —
the map of who creates/changes what — so a misrouted write is answered with the owning agent's
name, never with "not possible".

### Tools

Three tools are bound to **every** agent rather than to the one whose namespace they sit in —
`erp_bc.data.run_sql`, `erp_bc.inventory.net_inventory` and `erp_bc.retail.get_item_prices`, all
declared with `Agents = new[] { "*" }`. See *Shared tools* below for why, and for what that means
for the agent prompts.

| Tool key | Sensitivity | Description |
|---|---|---|
| `erp_bc.sales.lookup_customer` | `read` | **Preferred.** Resolve a customer by No. or partial name in one call (`ResolveCustomer`) |
| `erp_bc.sales.find_customers` | `read` | Search/browse customers by name; **paginated dataset** — sample + `dataset_ref`, full set exportable (no balance by default) |
| `erp_bc.sales.get_customer` | `read` | Look up a customer by No.; returns name, contact, balance, credit limit, blocked status |
| `erp_bc.sales.create_sales_order` | `write` | Create a sales order header for a customer; returns the generated order number |
| `erp_bc.sales.add_sales_order_line` | `write` | Add an item line (item No. + quantity) to an existing sales order |
| `erp_bc.sales.post_sales_order` | `write` | Post a sales order — ship and/or invoice (`PostSalesOrder`) |
| `erp_bc.sales.get_sales_order` | `read` | Look up an order by No.; returns customer, status, dates, totals, and ALL lines with shipped/invoiced progress |
| `erp_bc.sales.find_sales_orders` | `read` | Browse unposted sales orders by customer, status, order-date range, or order/PO substring; newest first; **paginated dataset** (`FindSalesOrders`) |
| `erp_bc.inventory.lookup_item` | `read` | **Preferred.** Resolve an item by No., **barcode**, or partial description in one call; barcode matches report the unit that barcode identifies (`ResolveItem`) |
| `erp_bc.inventory.find_items` | `read` | Search/browse items by description; **paginated dataset** — sample + `dataset_ref`, full set exportable (no inventory by default) |
| `erp_bc.inventory.get_item` | `read` | Look up an item by No. or **barcode**; returns description, unit price, on-hand inventory, UoM conversions, and the item's barcodes |
| `erp_bc.inventory.list_item_categories` | `read` | List item categories (departments) with the No. Series each numbers items from and the next number it will issue (`ListItemCategories`) |
| `erp_bc.inventory.create_item` | `write` | Create an item, numbered from its **category's** No. Series, with the category template's defaults, base UoM, barcode and price (`CreateItem`) |
| `erp_bc.inventory.update_item` | `write` | Correct an item card — description, vendor, weights, units, department. Price/blocking/number are other tools (`UpdateItem`) |
| `erp_bc.inventory.add_item_unit_of_measure` | `write` | Add a UoM conversion to an item, e.g. درزن = 12 (`AddItemUnitOfMeasure`) |
| `erp_bc.inventory.add_item_barcode` | `write` | Attach a barcode to an item against a specific unit (`AddItemBarcode`) |
| `erp_bc.inventory.set_item_price` | `write` | Set an item's price on the card and a customer price group; accepts incl- or excl-VAT (`SetItemPrice`) |
| `erp_bc.inventory.release_item` | `write` | Release a new item for sale, or block an item (`SetItemBlocked`) |
| `erp_bc.inventory.net_inventory` | `read` | LS Central net (available) store inventory; group by detail/item/store/category; batch over `itemNos` (≤500) **and `storeNos`** (≤50) in one call, or scope with the uncapped `descriptionContains` / `itemCategoryCode` filters; **paginated dataset** with exact total row count. **Shared with every agent** (`Agents = "*"`) — see *Shared tools* below (`GetNetInventory`) |
| `erp_bc.inventory.available_inventory` | `read` | Net inventory **minus stock committed to outbound transfer orders** — what can actually be promised. Same filters/groupings; adds `toReservedQuantity`, `availableInventory`, `toIncomingQuantity`. Inventory agent only (`GetAvailableInventory`) |
| `erp_bc.inventory.find_stores` | `read` | List LS Central stores with name, location code, and location name (`FindStores`) |
| `erp_bc.inventory.find_locations` | `read` | List BC warehouse locations with code, name and bin usage (`binMandatory`, `binCount`) (`FindLocations`) |
| `erp_bc.inventory.find_bins` | `read` | List a location's bins (e.g. MK-PLACE → TRENDYOL-MDA, NOON-FLEX …), with an item's quantity per bin and its default bin when `itemNo` is given; the read half of the transfer-line bin arguments. Needs gateway **1.8.1.0** (`FindBins`) |
| `erp_bc.finance.list_open_customer_entries` | `read` | List a customer's open ledger entries with overdue flags and aging detail |
| `erp_bc.finance.lookup_customer` | `read` | **Preferred.** Resolve a customer by No. or partial name in one call (`ResolveCustomer`) |
| `erp_bc.finance.find_customers` | `read` | Search customers by name (finance-scoped wrapper); **paginated dataset** |
| `erp_bc.finance.get_customer` | `read` | Look up a customer by No.; balance, credit limit, blocked |
| `erp_bc.finance.list_customers_with_overdue` | `read` | List customers with overdue open receivables |
| `erp_bc.finance.get_customer_aging` | `read` | Aging-bucket summary for one customer's open receivables |
| `erp_bc.finance.lookup_vendor` | `read` | **Preferred.** Resolve a vendor by No. or partial name in one call (`ResolveVendor`) |
| `erp_bc.finance.find_vendors` | `read` | Search vendors by name (finance-scoped wrapper); **paginated dataset** |
| `erp_bc.finance.get_vendor` | `read` | Look up a vendor by No.; balance owed, blocked |
| `erp_bc.finance.list_open_vendor_entries` | `read` | List a vendor's open ledger entries with overdue flags |
| `erp_bc.finance.list_vendors_with_overdue` | `read` | List vendors with overdue open payables |
| `erp_bc.finance.get_vendor_aging` | `read` | Aging-bucket summary for one vendor's open payables (positive = owed) (`GetVendorAging`) |
| `erp_bc.finance.get_sales_invoice` | `read` | Look up a posted sales invoice by No.; header + lines |
| `erp_bc.finance.find_sales_invoices` | `read` | Search posted sales invoices by customer, no., date, or `unpaidOnly`; rows carry `currencyCode` |
| `erp_bc.finance.get_purchase_invoice` | `read` | Look up a posted purchase invoice by No.; header + lines |
| `erp_bc.finance.find_purchase_invoices` | `read` | Search posted purchase invoices by vendor, no., or date |
| `erp_bc.finance.find_gl_accounts` | `read` | Search G/L accounts by number or name |
| `erp_bc.finance.get_gl_account` | `read` | Look up a G/L account by No.; balance, type, blocked |
| `erp_bc.finance.list_gl_entries` | `read` | List G/L entries for an account, optionally by date range |
| `erp_bc.finance.list_bank_accounts` | `read` | List bank accounts with balances |
| `erp_bc.finance.get_bank_account` | `read` | Look up a bank account by No.; balance and currency |
| `erp_bc.finance.create_bank_account` | `write` | Create a bank account (master data). `bankAccountNo` is the BC record No. (omit for the number series); `bankAccountNumber` is the number at the bank (`CreateBankAccount`) |
| `erp_bc.finance.create_journal_batch` | `write` | Create an empty, **unposted** general journal batch to stage entries in (`CreateJournalBatch`) |
| `erp_bc.finance.add_journal_line` | `write` | Add one unposted line to a journal batch (`amount` + = debit, − = credit, or `debitAmount`/`creditAmount`); returns the batch's running balance (`AddJournalLine`) |
| `erp_bc.finance.get_journal_batch` | `read` | Read back a staged journal batch: its lines plus `totalDebitLcy`/`totalCreditLcy`/`balanceLcy`/`balanced` (`GetJournalBatch`) |
| `erp_bc.finance.find_intercompany_transactions` | `read` | Intercompany (IC) outbox and inbox transactions of a company — open and handled — with partner, source document, dates and status; full list with `top`/`skip`. **Shared with every agent** (`Agents = "*"`). Needs gateway **1.8.0.0** (`FindIcTransactions`) |
| `erp_bc.purchasing.lookup_vendor` | `read` | **Preferred.** Resolve a vendor by No. or partial name in one call (`ResolveVendor`) |
| `erp_bc.purchasing.find_vendors` | `read` | Search/browse vendors by name; **paginated dataset** — sample + `dataset_ref`, full set exportable (no balance by default) |
| `erp_bc.purchasing.get_vendor` | `read` | Look up a vendor by No.; returns name, contact, balance owed (+ balance due), blocked status |
| `erp_bc.purchasing.create_purchase_order` | `write` | Create a purchase order header for a vendor; returns the generated order number |
| `erp_bc.purchasing.add_purchase_order_line` | `write` | Add an item line (item No. + quantity) to an existing purchase order |
| `erp_bc.purchasing.post_purchase_order` | `write` | Post a purchase order — receive and/or invoice; accepts `vendorInvoiceNo` (required for invoicing) (`PostPurchaseOrder`) |
| `erp_bc.purchasing.get_purchase_order` | `read` | Look up a purchase order by No.; returns vendor, status, dates, totals, and ALL lines with received/invoiced progress |
| `erp_bc.purchasing.find_purchase_orders` | `read` | Browse unposted purchase orders by vendor, status, order/expected-receipt date range, or number substring; newest first; **paginated dataset** (`FindPurchaseOrders`) |
| `erp_bc.inventory.create_transfer_order` | `write` | Create a transfer order header (from/to/in-transit locations) stamped with the source branch's REGION/BRANCH dimension and LS Store-from/Store-to; returns the generated number |
| `erp_bc.inventory.add_transfer_order_line` | `write` | Add an item line (item No. + quantity) to an existing transfer order, with the destination / source **bin** (`transferToBinCode` / `transferFromBinCode`); warns when a bin-mandatory location still has no bin on the line (gateway 1.8.1.0+) |
| `erp_bc.inventory.get_transfer_order` | `read` | Look up a transfer order by No.; returns header (locations, status, dates) and all lines |
| `erp_bc.inventory.find_transfer_orders` | `read` | List transfer orders by source/destination location, newest first; **paginated dataset** |
| `erp_bc.inventory.get_transfer_orders` | `read` | **Batch** full detail (header + lines) for many transfer orders (≤100) in one call (`GetTransferOrders`) |
| `erp_bc.inventory.post_transfer_order` | `write` | Post a transfer order's shipment and/or receipt (Ship \| Receive \| ShipAndReceive) |
| `erp_bc.inventory.delete_transfer_order` | `destructive` | Delete a transfer order, applying BC's own rules — refuses stock in transit, reservations, open warehouse documents, or a Released order; `dryRun` checks without deleting (`DeleteTransferOrder`). Every call needs the user's approval |
| `erp_bc.inventory.delete_transfer_orders` | `destructive` | Delete up to 50 transfer orders in ONE call — one approval for the whole list; each order checked and deleted on its own, the rest reported (`DeleteTransferOrders`, gateway 1.13.0.0+) |
| `erp_bc.inventory.check_transfer_order_deletion` | `read` | Which of up to 50 transfer orders could be deleted, and what blocks the rest; changes nothing, needs no approval (`CheckTransferOrderDeletion`, gateway 1.13.0.0+) |
| `erp_bc.inventory.update_transfer_order` | `write` | Correct a transfer order header (dates, locations, external doc no., region/branch); cascades to lines (`UpdateTransferOrder`) |
| `erp_bc.inventory.update_transfer_order_line` | `write` | Correct one line's quantity, variant, dates or bins (`transferToBinCode` / `transferFromBinCode`); quantity cannot drop below what shipped (`UpdateTransferOrderLine`) |
| `erp_bc.inventory.delete_transfer_order_line` | `write` | Remove one line, same blockers as deleting the order; `dryRun` supported (`DeleteTransferOrderLine`) |
| `erp_bc.data.count_records` | `read` | Count records of any supported entity, with optional filters; `allCompanies` returns a per-company breakdown + total |
| `erp_bc.data.sum_records` | `read` | Sum a numeric field (sales totals, not row counts); presets: `posSales`, `posNetSales`, `customerOrderSales`, `salesInvoices`, `bcSalesOrders`; batch `items[]` returns `grandTotal` |
| `erp_bc.data.query_records` | `read` | List/filter records of any supported entity, returning chosen or default fields (ad-hoc queries); **paginated dataset** — cursor drives the gateway `top`/`skip` |
| `erp_bc.data.list_companies` | `read` | List the Business Central companies available on the server |
| `erp_bc.data.run_sql` | `read` | Run a single validated read-only `SELECT` directly against the BC SQL Server (ad-hoc joins/`GROUP BY`/window functions); **paginated dataset** — the agent sees a sample + `dataset_ref`, the full set is exported page-by-page (give the query a deterministic `ORDER BY`). Optional — enabled by `BC_SQL_*`. See *Direct read-only SQL* below |
| `erp_bc.data.describe_schema` | `read` | **Platform-internal.** This connector's canonical schema for one BC company: `Scope` = company, `Part` = `entities` \| `relations`; **paginated rowset**. Backs platform schema extraction (see *Relational source* below), not user questions. Optional — needs `BC_SQL_*` |
| `erp_bc.retail.get_pos_transaction` | `read` | Look up a POS receipt by receipt number or store + terminal + transaction no. (`GetPosTransaction`) |
| `erp_bc.retail.get_pos_transactions` | `read` | **Batch** full detail (header + lines + tenders) for many receipts (≤100) in one call; avoids the per-receipt fan-out (`GetPosTransactions`) |
| `erp_bc.retail.get_top_items` | `read` | Best-selling / top items for a store and/or date range, aggregated server-side and ranked by quantity / sales / cost (`GetTopItems`) |
| `erp_bc.retail.find_pos_transactions` | `read` | Browse POS receipts; filter by store, customer, receipt, **mobile number**, **customer order ID**, **`retrievedFromReceiptNo`** (the refunds booked against a given sale), date range, or **returnsOnly** (refund receipts); **paginated dataset** — cursor drives the gateway `top`/`skip` (`FindPosTransactions`) |
| `erp_bc.retail.get_customer_order` | `read` | Look up an LS Central customer order by document ID or external ID |
| `erp_bc.retail.find_customer_orders` | `read` | Search customer orders by store, customer, mobile phone, status, dates; newest first; **paginated dataset** |
| `erp_bc.retail.get_customer_orders` | `read` | **Batch** full detail (header + lines) for many customer orders (≤100) in one call (`GetCustomerOrders`) |
| `erp_bc.retail.sum_sales` | `read` | Retail-scoped alias for POS/customer-order sales totals (`Sum` presets) |
| `erp_bc.retail.count_transactions` | `read` | Retail-scoped alias for counting POS transaction rows (`Count`) |
| `erp_bc.retail.lookup_member` | `read` | Resolve a loyalty member by account/card/contact/mobile/e-mail or fuzzy name — unique match returns full detail with point balances (`ResolveMember`) |
| `erp_bc.retail.get_member` | `read` | Full member detail: account, point balances (incl. expiring 30/90 days), contacts, membership cards (`GetMember`) |
| `erp_bc.retail.find_members` | `read` | Search member contacts by name, mobile, e-mail, account, club, or scheme; **paginated dataset** (`FindMembers`) |
| `erp_bc.retail.get_member_points` | `read` | Point balance + point transaction history (earned/redeemed/expired), filterable by entry type and date (`GetMemberPoints`) |
| `erp_bc.retail.get_sales_by_tender` | `read` | Sales by payment method: per-tender amounts (LCY) ranked largest first, with descriptions (`GetSalesByTender`) |
| `erp_bc.retail.get_staff_sales` | `read` | Per-cashier sales for a period: turnover, payments, discounts, counts, voids, avg basket — ranked by turnover (`GetStaffSales`) |
| `erp_bc.retail.find_staff` | `read` | List POS staff by store, name, or ID (`FindStaff`) |
| `erp_bc.retail.get_hourly_sales` | `read` | Hourly sales distribution (24 rows): counts + gross/net/discount per hour, plus totals (`GetHourlySales`) |
| `erp_bc.retail.find_statements` | `read` | List end-of-day statements (posted by default) with sales/VAT/income/expenses and counted-vs-recorded difference; **paginated dataset** (`FindStatements`) |
| `erp_bc.retail.get_statement` | `read` | One statement's header + per-tender reconciliation lines: recorded vs counted vs difference (`GetStatement`) |
| `erp_bc.retail.find_offers` | `read` | List promotions (LSC Periodic Discount): enabled/active-on-date offers, by type (incl. Mix&Match), name, or item; optional statistics; **paginated dataset** (`FindOffers`) |
| `erp_bc.retail.get_offer` | `read` | One promotion's detail: header, sales statistics, and offer lines — items, line groups, deal price / discount %, exclude flag (`GetOffer`) |
| `erp_bc.retail.get_item_prices` | `read` | **Price check.** Item No., barcode, description, vendor item no., shelf price incl. VAT and the POS's single-unit price after the current Disc. Offer / Line Discount, per store; scope by item(s), barcode, offer, or filter; returns the **full list** (`top`/`skip`, default 100, max 300) rather than a dataset sample. **Shared with every agent** (`Agents = "*"`) — see *Shared tools* below. Needs gateway **1.7.0.0** (`GetItemPrices`) |
| `erp_bc.retail.find_coupons` | `read` | List coupons (LSC Coupon Header): Store/Manufacturer/Return, valid-on-date, with optional issued/used statistics; **paginated dataset** (`FindCoupons`) |
| `erp_bc.retail.get_coupon` | `read` | One coupon's detail: trigger rules, usage statistics, and Use/Issue item lines (`GetCoupon`) |
| `erp_bc.retail.create_offer` | `write` | Create a promotion (periodic discount) header — created **Disabled** (`CreateOffer`) |
| `erp_bc.retail.add_offer_line` | `write` | Add an item / product group / item category / special group line to an offer (`AddOfferLine`) |
| `erp_bc.retail.set_offer_status` | `write` | Enable (go live) or disable an offer; refuses to enable one with no lines (`SetOfferStatus`) |
| `erp_bc.retail.create_coupon` | `write` | Create a store / manufacturer / return coupon — created **Disabled**; `code` is max 10 chars (`CreateCoupon`) |
| `erp_bc.retail.add_coupon_line` | `write` | Add a `Use` or `Issue` line to a coupon (`AddCouponLine`) |
| `erp_bc.retail.set_coupon_status` | `write` | Enable (go live) or disable a coupon; refuses to enable one with no `Use` lines (`SetCouponStatus`) |

> All finance gateway operations are implemented in the ASG AI Gateway `Dispatch` table (v1.1.2.0+).
> Posted **credit memos** have no dedicated tool — query them via `query_records` with entity
> `Sales Cr.Memo Header` / `Purch. Cr. Memo Hdr.` (lines: `Sales Cr.Memo Line` / `Purch. Cr. Memo Line`).

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) (for `dotnet run`)
- A Vested AI connector token and hub address (see the platform docs)
- An on-prem Business Central instance with:
  - **API services enabled** on the BC Server instance (`ODataServicesEnabled` /
    `ApiServicesEnabled` — both on by default).
  - **NavUserPassword** credential type (basic auth) configured.
  - A BC user account **per agent user**, each with their own **Web Service Access
    Key** (BC → Users → the user → Web Service Access Key). Tools run as the
    calling person, so a user without a BC account cannot use the connector.
  - The **ASG AI Gateway** AL extension installed — a standalone app (publisher
    `asg`, id range 52765–52800) that publishes the custom API (publisher `asg`,
    group `ai`, version `v1.0`). Custom API pages are published automatically — no
    manual Web Services registration is needed. The BC user must have permission to
    read/write the Customer, Item, Vendor, Sales, Purchase and Cust. Ledger tables
    the operations touch.

---

## Configuration

Copy `.env.example` to `.env` and fill in the values:

| Variable | Required | Description |
|---|---|---|
| `VESTED_CONNECTOR_TOKEN` | yes | Connector JWT from the Vested AI platform |
| `VESTED_CONNECTOR_HUB` | yes | Hub gRPC endpoint, `host:port` |
| `LOG_LEVEL` | no | `Trace`…`Error` (default `Information`) |
| `BC_BASE_URL` | yes | BC ODataV4 base URL: `http(s)://<host>:<port>/<serverinstance>/ODataV4` (only OData Services need to be enabled, not API Services) |
| `BC_COMPANY` | yes | **Default** company name exactly as in BC (e.g. `ASG`). Used when a tool call omits `country`. |
| `BC_COMPANY_MAP` | no | Semicolon-separated `country\|company\|gatewayId` entries. Pre-configuring `gatewayId` skips the `aiGateway` lookup for that company. |
| `VESTED_CREDENTIAL_PRIVATE_KEY` | **not while the credential declaration is disabled** (2026-08-13) | PKCS#8 PEM private half of this connector's credential keypair (generated in Core, public half registered there). Opens users' sealed BC credentials. During key rotation hold both keys, newest first, separated by a blank line. Required again the moment the `[Credential]` attributes in `BcUserCredentialHandler.cs` are uncommented — the SDK throws at startup without it. |
| `VESTED_CREDENTIAL_PRIVATE_KEY_FILE` | no | Path to a file holding the above, instead of the inline value. |
| `BC_TIMEOUT_SECONDS` | no | Per-request HTTP timeout in seconds (default `120`). Must be **>= the largest tool `DefaultDeadlineMs`** (`net_inventory` / `available_inventory` declare 120 s), or this aborts the call before the tool's own deadline is reached. |
| `BC_SQL_CONNECTION` | no | Full read-only SQL Server connection string. Enables `erp_bc.data.run_sql`. Use this **or** the discrete `BC_SQL_*` variables below. |
| `BC_SQL_SERVER` | no | SQL Server host/instance (e.g. `bc-sql\\BC`). With `BC_SQL_DATABASE/USERNAME/PASSWORD`, the connector builds a read-only (`ApplicationIntent=ReadOnly`, `Encrypt=true`) connection string. |
| `BC_SQL_DATABASE` | no | BC database name. Required when `BC_SQL_SERVER` is set. |
| `BC_SQL_USERNAME` | no | **Read-only** SQL login (granted `SELECT`, denied `INSERT/UPDATE/DELETE/EXEC`). Required when `BC_SQL_SERVER` is set. |
| `BC_SQL_PASSWORD` | no | Password for the read-only SQL login. Required when `BC_SQL_SERVER` is set. |
| `BC_SQL_TRUST_CERT` | no | `false` to require a CA-signed server cert; default trusts a self-signed cert (typical on-prem). |
| `BC_SQL_TIMEOUT_SECONDS` | no | SQL connect + command timeout in seconds (default `60`). |
| `BC_SQL_ALLOWED_SCHEMAS` | no | Comma-separated schema allow-list (e.g. `ai`). When set, `run_sql` only permits tables/views in those schemas. Empty = rely on the login's grants. Does **not** apply to catalog reads — see *Relational source*. |
| `BC_SQL_CATALOG_TIMEOUT_SECONDS` | no | Command timeout for the catalog reads behind `describe_schema` (default `300`). Separate from `BC_SQL_TIMEOUT_SECONDS`: a full `sys.columns` scan is a different workload from an ad-hoc query. |
| `BC_SQL_CATALOG_CACHE_SECONDS` | no | How long the catalog read is cached in memory (default `300`, `0` disables). The cache holds every table and column in the database — see the memory note under *Relational source*. |

`BcClient.Configure()` (called from `Program.cs`) validates the `BC_*` variables
at startup and throws with a clear message if any are missing — the process exits
before connecting to the hub.

### Direct read-only SQL (`erp_bc.data.run_sql`)

Optional. When the `BC_SQL_*` variables are set, the Data & Analytics agent also exposes
**`run_sql`** — a single, validated, read-only `SELECT` straight against the BC SQL Server
for ad-hoc analytics (joins, `GROUP BY`, window functions) the gateway data tools can't express.
The feature is **disabled** when no `BC_SQL_*` variables are present (the tool returns a clear
"not configured" error rather than failing startup). Layers enforcing "SELECT only":

1. **The SQL login is the real boundary** — grant it `SELECT`, deny `INSERT/UPDATE/DELETE/EXEC` at
   the database. Even a bug here cannot write.
2. **`BcSqlGuard`** parses each statement with `Microsoft.SqlServer.TransactSql.ScriptDom` (not regex)
   and rejects anything but one read-only `SELECT` — no DML/DDL/`EXEC`, no `SELECT … INTO`, no stacked
   statements, optional schema allow-list.
3. Reads run under `READ UNCOMMITTED` so analytics never block live BC users. Results are served as a
   **paginated dataset** (SDK 0.3.0 `PaginatedToolHandler`): each page re-executes the `SELECT` and
   skips the rows already delivered off the streaming reader, so any `SELECT` shape works; the model
   is instructed to include a deterministic `ORDER BY` so pages line up. The command timeout is the
   backstop against runaway scans.

> ⚠️ **Not yet scoped per caller.** `run_sql` currently returns **unscoped** data (it bypasses the
> gateway's `departments` filter). Until per-caller ERP-permission scoping is added (see the `TODO`
> in `BcSqlClient.RunSelectPageAsync`), point it at non-sensitive views/tables, or set
> `BC_SQL_ALLOWED_SCHEMAS` to a curated read-only view schema.

### Relational source (schema extraction)

`BcSchemaProvider` (`BcSchemaSource.cs`) carries `[RelationalSource]`, so every
`Register` tells the platform that this connector fronts a SQL Server database,
which tool describes it (`erp_bc.data.describe_schema`), which tool runs SQL
(`erp_bc.data.run_sql`), and that the SQL travels in the **`Sql`** argument —
PascalCase, matching `RunSql.Args.Sql` on the wire. `ConnectorHostBuilder.Build()`
cross-checks all three at startup, so a typo fails the process instead of leaving
a query tool ungoverned.

The model itself comes from the SDK: `SqlServerProvider` decomposes
`<Company>$<Logical>$<AppGuid>`, groups a logical entity's variant set and picks
its base. The only hand-written part is `BcCatalogReader` — three reads of
`sys.tables`, `sys.columns` and BC's `$ndo$navapptableextension`. Catalog reads
deliberately bypass `BcSqlGuard`: the statements are constants in this assembly,
and the guard's schema allow-list would otherwise reject `sys` /
`INFORMATION_SCHEMA` and silently disable extraction on hardened deployments.
The least-privilege login and `READ UNCOMMITTED` still apply.

> ⚠️ **Memory.** `ICatalogReader.ColumnsAsync` takes no scope, so describing ONE
> company still materialises every column in the database — on the ASG catalog
> that is 26,191 tables and roughly two million columns, an estimated ~250 MB
> resident while a describe runs, and the same again for `BC_SQL_CATALOG_CACHE_SECONDS`
> after it. Size the connector's memory limit accordingly, or set that variable to
> `0`. The first catalog read logs the real counts.

Withdrawing the declaration (deleting the attribute) stops extraction on the next
`Register` — the core reads the current baseline.

It can be declared **alongside** a `credential_schema`, as it is here. That was
not true before 2026-08-16: the hub refused every schema op with
`403 credential_gated` while credentials were declared, because extraction is a
system operation and there was no principal whose sealed credential it could
spend. The core now supplies one — `connectors.schema_automation_user_id` names
the user a connector's automated schema work acts as, and the schema-op route
resolves that user's credential through the same gate the dispatch path uses.

⚠ The platform-side precondition is that erp_bc has `schema_automation_user_id`
set to a user holding a `valid` credential for this connector. Without one the
hub still refuses `credential_gated` — unchanged, and fail-closed by design.
**Do not answer that refusal by deleting `[RelationalSource]`**: that leaves the
real cause untouched and turns extraction off. See the block comment in
`BcUserCredentialHandler.cs`.

### Countries (multi-company)

The connector serves multiple Business Central companies, one per country. Every
tool accepts an optional **`country`** argument (two-letter code); when omitted,
Saudi Arabia (`SA`) is used. The agents map a country mentioned by the user to
the matching code:

| `country` | BC company | Country |
|---|---|---|
| `SA` | `ASG` | Saudi Arabia (السعودية) — **default** |
| `KW` | `ASG - KWT` | Kuwait (الكويت) |
| `OM` | `ASG - OM` | Oman (عُمان) |
| `QA` | `ASG - QAR` | Qatar (قطر) |
| `AE` | `ASG - UAE` | UAE (الإمارات) |

`country` is a routing field: `BcClient` strips it from the args, resolves the BC
company name via `BcCompanyRegistry`, and never forwards it in the operation payload.
Gateway IDs can be pre-configured in `BC_COMPANY_MAP` (recommended); otherwise they
are resolved via OData on first use and cached per company. Each company must have
the ASG Customization app installed (its install/upgrade codeunit seeds the gateway
record).

Example `BC_COMPANY_MAP`:

```
SA|ASG|3c4b6c11-7f63-f111-8483-6045bd6a7964;KW|ASG - KWT|<guid>;OM|ASG - OM|<guid>;QA|ASG - QAR|<guid>;AE|ASG - UAE|<guid>
```

Obtain each `<guid>` with:

```
GET {BC_BASE_URL}/Company('{company}')/aiGateway?$top=1
```

Legacy tool calls that still pass `company` (exact BC name) are accepted as a fallback.

### How requests are formed

For each call, `BcClient` resolves the gateway record id for the target company
(from `BC_COMPANY_MAP` when configured, otherwise via OData and cached per company):

```
GET {BC_BASE_URL}/Company('{company}')/aiGateway?$top=1   → gateway record id (skipped when mapped)
```

Every tool then `POST`s its operation to the gateway's bound action:

```
POST {BC_BASE_URL}/Company('{company}')/aiGateway({gatewayId})/NAV.executeOperation
Content-Type: application/json

{
  "operation": "GetCustomer",
  "payload": "{\"customerNo\":\"C00010\"}",
  "callerContext": "{\"employeeNo\":\"E123\",\"erpIdentifier\":\"jdoe\",\"departments\":[\"SALES\"]}"
}
```

`payload` is the tool's `args` serialized (camelCase) as a JSON **string**.
`callerContext` is a separate JSON **string** carrying the authenticated caller
identity — see [Caller identity](#caller-identity) below. BC
returns the envelope as a string in OData's `value`:

```
{ "value": "{\"success\":true,\"data\":{ ... }}" }
```

`BcClient` parses `value`, checks `success`, throws `ToolValidationException` with
`error` on failure, and otherwise deserializes `data` into the tool's `Result`.

### Per-user authentication

> **This is LIVE.** The `[Credential]` / `[CredentialField]` attributes in
> `BcUserCredentialHandler.cs` are declared, and production carries this
> `credential_schema` with four `valid` credentials (measured 2026-08-16).
>
> It is declared **alongside** the `relational_source`. Between 2026-08-13 and
> 2026-08-16 that was impossible — the hub refused schema extraction with
> `403 credential_gated` whenever credentials were declared, so the two had to be
> traded off and schema extraction was chosen first. The core now names an
> automation principal for extraction, so both stand together; see *Relational
> source* above.
>
> ⚠ Withdrawing this declaration is a **live regression**, not a no-op: the
> gateway path has no service-account fallback, so every erp_bc OData tool would
> answer "no Business Central account is connected for you" and only the SQL
> tools (`run_sql`, `describe_schema`) would keep working.

There is no service account. Every request's `Authorization: Basic` header carries
`base64(<that user's BC name>:<their Web Service Access Key>)`, taken from the
credential they store themselves. The platform cannot read it: it is sealed in the
user's browser to this connector's public key, and only
`VESTED_CREDENTIAL_PRIVATE_KEY` opens it.

This is what makes BC's own authorization real — permission sets and security
filters apply to the actual person, and the Change Log names them rather than a
robot. `BcUserCredentialHandler` declares the form and validates a new credential
by listing the companies that sign-in can reach.

**A call with no stored credential fails closed** with a message telling the user
to connect their BC account. It never falls back to a shared identity, which would
silently reinstate the blind spot this design removes.

**Isolation between concurrent callers.** The credential is resolved from the
per-invocation `ToolContext` at the point of use and passed down as a parameter —
never held in static state. The shared `HttpClient` carries no default
`Authorization`; each `HttpRequestMessage` sets its own. Sharing the client is safe
*because NavUserPassword is HTTP Basic*, a stateless per-request header. That
assumption is load-bearing: switching this environment to NTLM or Negotiate would
bind authentication to the connection, and a pooled connection could then carry one
user's identity into another user's request — that change would require per-user
`HttpClient`s or handler-pool partitioning, not just a different header.

Caches are keyed accordingly. Store lookups (`BcStoreResolver`) are keyed by
company **and** BC user, because security filters mean two users can legitimately
see different stores in the same company. The gateway-record id cache stays shared:
it is a per-company singleton seeded at install, identical for everyone and not
user data.

Company access is BC's decision, made per request. The connector no longer probes
or narrows the company list at startup — that used to apply one service account's
access to every caller.

> `erp_bc.data.run_sql` is the exception: it still uses the shared read-only SQL
> login (`BC_SQL_*`) and is **not** scoped to the calling user. Keep it pointed at
> non-sensitive curated views until per-user scoping lands.

### Caller identity

Every `ToolCallRequest` carries the calling user's ERP identity in `ToolContext`
(`EmployeeNo`, `ErpIdentifier`, `ErpDepartmentIdentifiers`). These are
**authenticated by the hub, not model-supplied** — they are therefore **not** part
of any tool's `Args` schema. `BcClient.ExecuteAsync` injects them from `ctx` at call
time (via `BcCallerContext`) into the dedicated `callerContext` action field, kept
separate from `payload` so the model can never reach or spoof them.

- **Missing identity** (`EmployeeNo` and `ErpIdentifier` both empty):
  - **Read tools** proceed **unscoped** — the identity is still forwarded for audit.
  - **Write/sensitive tools** (`create_*`, `add_*_line`, `post_*`, `set_*_status`,
    `update_*`, `delete_*`) go through `BcCallerContext.Require` — a data-changing action
    must be attributable to a known user. **Two things satisfy it:** an ERP identity on the
    platform profile, **or the caller's own sealed Business Central credential** (the
    account connected under *My Systems*). The gate predates per-user credentials, when
    every call ran as one shared service account and the ERP identity was the only way to
    name a person; now the call runs as the user's own BC sign-in, BC enforces that user's
    permissions, and the Change Log and the ASG AI Request Log (`User ID`) name them. A
    user with a connected BC account and no employee number therefore passes; a user
    with neither gets an error that names both fixes. An empty department list is always
    allowed. Note this is an *identity* check, not an authorization one: any identified
    caller may invoke any write tool. Per-caller authorization is not implemented yet,
    which is why journal **posting** is deliberately absent (see `add_journal_line` below).
- **Department scoping** (`ErpDepartmentIdentifiers`): forwarded on every call; the
  gateway constrains results to those departments **where the entity carries a
  department dimension** (Global/Shortcut Dimension 1). Applied to finance list/aggregate
  tools (`list_open_customer_entries`, `list_open_vendor_entries`, `list_customers_with_overdue`,
  `list_vendors_with_overdue`, `list_gl_entries`) and the generic count/query engine. **Not** applied to
  master-data lookups (`find_*`/`get_customer`/`get_item`/`get_vendor`) — those records
  have no department — nor to get-by-id document lookups (explicit key).
- The gateway records `Caller Employee No.`, `Caller ERP Identifier`, and
  `Caller Departments` on every row of the **ASG AI Request Log** for audit.

---

## Project layout

```
bc-connector/
├── BcConnector.csproj    # Exe project; references the SDK via ProjectReference
├── Program.cs            # BcClient.Configure() → ConnectorHost → RunFromEnvironmentAsync
├── Agents.cs             # [Agent] + [Instruction] declarations (7 agents)
├── Tools.cs              # [Tool] ToolHandler<,> implementations for sales/inventory/finance/purchasing/retail/data
├── ServiceTools.cs       # [Tool] implementations for the erp_bc.service domain (read-only; see the note in the file)
├── ItemTools.cs          # [Tool] implementations for the item CREATION workflow (see “Creating items” below)
├── BcSqlTools.cs         # The direct read-only SQL tool (erp_bc.data.run_sql)
├── BcClient.cs           # Shared ASG AI Gateway client over ODataV4 (basic auth; country routing + gateway-id resolution)
├── BcCompanyRegistry.cs  # Country → BC company name + optional pre-configured gateway IDs (BC_COMPANY_MAP)
├── BcToolDescriptions.cs # Shared tool-schema descriptions for the model
├── .env.example          # Environment variable template
├── Dockerfile            # Multi-stage build (dotnet/sdk:8.0 → dotnet/runtime:8.0)
└── README.md             # This file
```

---

## Connecting to the platform

The connector is a **long-lived process**: it dials the hub over gRPC, authenticates
with the connector JWT, registers its agents and tools, then stays in a receive loop.
The integration shows as **active** on the dashboard only while that process is
running and registered.

### 1. Match the agent/tool keys to your namespace

The hub rejects registration (exit code `78`, `namespace_violation`) unless **every**
`[Agent]` and `[Tool]` key starts with the connector's namespace followed by a dot —
the `ns` claim in your JWT. For the `erp_bc` integration that means keys like
`erp_bc.sales` and `erp_bc.sales.get_customer`. Check `Agents.cs` / `Tools.cs` if you
change namespaces.

### 2. Load the environment, then run

`RunFromEnvironmentAsync()` reads `VESTED_CONNECTOR_TOKEN` and `VESTED_CONNECTOR_HUB`
from the **process environment** — it does **not** auto-load the `.env` file. Export
the values first (or use a dotenv loader / your shell's mechanism).

PowerShell:

```powershell
cd examples/bc-connector
Get-Content .env | Where-Object { $_ -match '^\s*[^#].*=' } | ForEach-Object {
  $k,$v = $_ -split '=',2
  Set-Item -Path "env:$($k.Trim())" -Value $v.Trim()
}
dotnet run
```

bash / zsh:

```bash
cd examples/bc-connector
set -a; . ./.env; set +a
dotnet run
```

A successful connection logs:

```
[vested] connected to hub: connector_id=<id> namespace=erp_bc max_concurrent=16
[vested] registered with hub
```

The process configures the BC client, connects to the hub, registers all four
agents and fifty-three tools, then enters the steady-state receive loop. Press Ctrl-C
for a graceful shutdown (exit code 0).

For a local hub over plain HTTP, also call `.UseInsecureTransport()` in `Program.cs`.

### 3. Exit codes

| Code | Meaning |
|---|---|
| `0` | Graceful shutdown (SIGINT / SIGTERM) |
| `78` | Token rejected or missing environment variable |
| `1` | Unexpected error (incl. missing `BC_*` config) — supervisor will reconnect with backoff |

---

## Shared tools

A tool normally belongs to the agent whose key namespace it sits under: the host binds tools
to agents by prefix, so `erp_bc.inventory.*` reaches the Inventory agent and nothing else.
Three tools opt out of that with `Agents = new[] { "*" }` on the `[Tool]` attribute:

| Tool | Why it is shared |
|---|---|
| `erp_bc.data.run_sql` | Ad-hoc SQL is not a Data-agent question; any domain can hit something its purpose-built tools cannot express |
| `erp_bc.inventory.net_inventory` | "Do we have stock?" is asked by Sales before promising an order, Purchasing before reordering, Retail about a branch, and Service about a spare part |
| `erp_bc.retail.get_item_prices` | "How much is it?" is asked of Sales before a quote, Inventory about an item, Purchasing against a vendor item no., Service for a spare part — and the only correct answer (incl. VAT, after the current offer, per store) lives in the POS logic under `erp_bc.retail` |
| `erp_bc.finance.find_intercompany_transactions` | "Did our invoice reach Qatar?" is asked of Sales (who made the order), Purchasing (who receives it), Finance (who settles it) and Data; the IC outbox/inbox is one trail |

Three things follow from sharing a tool, and all three are deliberate:

* **The key does not move.** Renaming `erp_bc.inventory.net_inventory` to `erp_bc.shared.*`
  would deprecate the existing Tool row on the platform and drop any grants or admin-set
  sensitivity keyed on it. The namespace simply stops implying exclusive ownership.
* **The alternative was duplication.** Binding by prefix meant the only other way to give six
  agents the same capability was six near-identical handler classes under six namespaces.
  `"*"` also means an agent added later picks it up with no change here.
* **Every agent that gains a tool gains guidance for it.** A tool an agent holds but was never
  told about is how `groupBy=detail` without a scope filter turns into a timeout. The
  cross-domain text lives once, in `BcSharedInstructions.NetInventory` and
  `BcSharedInstructions.ItemPrices` in `Agents.cs`, and is concatenated onto each agent's
  instruction body — attribute arguments are compile-time constants, so this is a `+` rather
  than six copies that drift apart. The owning agent keeps its own fuller version instead
  (Inventory for `net_inventory`, Retail for `get_item_prices`).

The shared guidance is written **defensively**, matching how `run_sql` is described: a shared
tool is still subject to per-agent grants, so each agent is told to check the tool is really
in its own list before planning around it, and to hand the question on rather than retry if
it is not.

---

## Tool reference

### Sales — `erp_bc.sales`

#### `find_customers` (sensitivity: `read`)

Searches customers by partial name. Operation `FindCustomers` (case-insensitive
`contains`, optionally capped at `top`, ordered by name — applied in AL).

**Args:** `nameContains` (string, optional — omit to browse all), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `customers[]` (each: `customerNo`, `name`, `city`, `balanceLcy`, `blocked`).
**Behaviour:** when `nameContains` is blank the filter is skipped and the first page
of customers (by name) is returned; returns an empty list (not an error) when nothing
matches; throws `ToolValidationException` on backend failure.

#### `get_customer` (sensitivity: `read`)

Looks up a customer by number. Operation `GetCustomer`.

**Args:** `customerNo` (string).
**Result:** `customerNo`, `name`, `email`, `phoneNo`, `balanceLcy`, `creditLimitLcy`, `blocked`.
**Error:** `ToolValidationException` when no customer matches, or on auth / network / HTTP failures.

#### `create_sales_order` (sensitivity: `write`)

Creates a sales order header. Operation `CreateSalesOrder` (inserts a `Sales Header`
of type `Order` and validates the sell-to customer in AL). The order has no lines
until you add them.

**Args:** `customerNo` (string), `externalDocumentNo` (string, optional).
**Result:** `orderNo`, `customerNo`, `customerName`.

#### `add_sales_order_line` (sensitivity: `write`)

Adds an item line to an existing order. Operation `AddSalesOrderLine` (inserts a
`Sales Line` of type `Item` and validates `No.`, `Quantity` and optional
`Location Code` in AL).

**Args:** `orderNo` (string), `itemNo` (string), `quantity` (decimal > 0), `locationCode` (string, optional).
**Result:** `orderNo`, `lineNo`, `itemNo`, `description`, `quantity`, `unitPrice`, `lineAmount`.
**Error:** `ToolValidationException` on blank args, non-positive quantity, or backend failure
(e.g. unknown order or item).

#### `get_sales_order` (sensitivity: `read`)

Looks up an order header by number. Operation `GetSalesOrder`.

**Args:** `orderNo` (string).
**Result:** `orderNo`, `customerNo`, `customerName`, `status`, `orderDate`, `externalDocumentNo`, `currencyCode`, `amountIncludingVat`.
**Error:** `ToolValidationException` when no order matches, or on backend failure.

### Inventory — `erp_bc.inventory`

#### `find_items` (sensitivity: `read`)

Searches items by partial description. Operation `FindItems` (case-insensitive
`contains`, optionally capped at `top`, ordered by description — applied in AL).

**Args:** `descriptionContains` (string, optional — omit to browse all), `includeInventory` (bool, optional).
**Result:** paginated dataset of `itemNo`, `description`, `type`, `baseUnitOfMeasure`,
`itemCategoryCode`, `unitPrice`, `unitCost`, `inventory` (only when `includeInventory`), `blocked`.
**Behaviour:** when `descriptionContains` is blank the filter is skipped and items are
browsed by description; returns an empty list (not an error) when nothing matches.
`type` matters — only `Inventory` items carry stock, so a `Service` item reading zero
on hand is not "out of stock".

#### `get_item` (sensitivity: `read`)

Looks up an item by number **or barcode**. Operation `GetItem`.

**Args:** `itemNo` (string) or `barcode` (string) — an unknown `itemNo` is retried as a barcode.
**Result:** the full item card — `itemNo`, `description`, `description2`, `type`,
`baseUnitOfMeasure` / `salesUnitOfMeasure` / `purchUnitOfMeasure`, `itemCategoryCode`,
`retailProductGroupCode` + `retailProductGroupDescription`, `unitPrice` (excl. VAT),
`unitCost`, `lastDirectCost`, `costingMethod`, `vendorNo`, `vendorItemNo`, `gtin`,
`grossWeight`, `netWeight`, the three posting groups, `inventory`, `qtyOnSalesOrder`,
`qtyOnPurchOrder`, `blocked` / `salesBlocked` / `purchasingBlocked`, `matchedBarcode`,
`unitsOfMeasure[]`, `barcodes[]` (≤50) and the LS Central audit trail.
**Error:** `ToolValidationException` when no item or barcode matches, or on backend failure.

### Creating items — `erp_bc.inventory`

**Item numbers are per category, and the series code *is* the category code.** Item
categories `CAT01`…`CAT30` each own a No. Series of the identical code, so category
`CAT17` numbers its items `CAT17-011627`. Inventory Setup's `Item Nos.` (`CREATEITEM`)
is a decoy — Manual, not Default, and with no No. Series Lines — so standard BC
auto-numbering assigns nothing and the number has to be drawn from the category's own
series. `CAT31`/`CAT32` have no series and are refused.

That is why `create_item` takes a **category** and returns a **number**: the caller does
not choose it. The category also selects the `Config. Template` of the same code, which
supplies Gen. Prod. Posting Group, VAT Prod. Posting Group, Type, Inventory Posting Group
and Costing Method — the same defaults a person gets from the item card.

Two safety rules are enforced in AL:

* **New items are created `salesBlocked`.** They replicate to the POS tills and the price
  is the thing most likely to be wrong, so `release_item` is a separate, deliberate step —
  the same posture as offers and coupons being created disabled.
* **Only the head-office company can create items.** The country companies (KW, OM, QA, AE)
  hold copies of the catalogue pushed from HQ; their CAT series stopped advancing when
  replication took over and are thousands of numbers behind the items that exist. The
  handler detects exactly that — the number the series offers already belongs to an item —
  and refuses with an explanation instead of failing on a duplicate key.

Prices are stored **excluding VAT** (a 1,000 shelf price is stored as 869.57 at 15%).
Pass `retailPriceInclVat` for a shelf price and `unitPriceExclVat` for a net one.

#### `list_item_categories` (sensitivity: `read`)

The No. Series page as a tool. Operation `ListItemCategories`.

**Args:** `codeContains` (string, optional), `creatableOnly` (bool, optional), `includeProductGroups` (bool, optional).
**Result:** `count`, `creatableCount`, `categories[]` (each: `code`, `description`,
`noSeriesCode`, `noSeriesExists`, `noSeriesOpen`, `startingNo`, `lastNoUsed`,
`lastDateUsed`, `nextNo`, `templateExists`, `creatable`, `notCreatableReason`,
`retailProductGroups[]`).
**Behaviour:** `nextNo` is a peek — it does not consume the number, and the series is
consumed by people continuously, so treat it as indicative. A category whose series
cannot issue numbers is reported with `creatable: false` rather than omitted.

#### `create_item` (sensitivity: `write`)

Creates the item, its base unit of measure, and — when supplied — its barcode and its
price on the `ALL` price group, in one transaction. Operation `CreateItem`.

**Args:** `itemCategoryCode` **or** `retailProductGroupCode` (the group carries its own
category, so either identifies the department; two that disagree is an error),
`description` (required), `baseUnitOfMeasure` (required), `description2`,
`unitPriceExclVat` **or** `retailPriceInclVat`, `barcode`, `vendorNo`, `vendorItemNo`,
`salesUnitOfMeasure`, `purchUnitOfMeasure`, `grossWeight`, `netWeight`,
`salesBlocked` (omit — defaults to `true`), `itemNo` (escape hatch; do not use normally).
**Result:** the item card plus `numberFromNoSeries`, `noSeries`, `templateApplied`,
`barcodeCreated`, `salesPriceCreated` and a `note` covering the sales block, any missing
price, and POS replication.
**Error:** unknown category or product group; a category with no series; a barcode that
belongs to another item; a drawn number that already exists (the replica-company case).

#### `add_item_unit_of_measure` (sensitivity: `write`)

Adds a UoM conversion, e.g. درزن = 12 حبة. Operation `AddItemUnitOfMeasure`.

**Args:** `itemNo`, `unitOfMeasureCode`, `qtyPerUnitOfMeasure` (base units per unit, > 0).
**Result:** `itemNo`, `unitOfMeasureCode`, `qtyPerUnitOfMeasure`, `baseUnitOfMeasure`, `note`.

#### `add_item_barcode` (sensitivity: `write`)

Attaches a barcode against a specific unit. Operation `AddItemBarcode`.

**Args:** `itemNo`, `barcode`, `unitOfMeasureCode` (defaults to the base unit),
`variantCode`, `showForItem` (defaults `true`), `description`.
**Result:** `itemNo`, `barcodeNo`, `unitOfMeasureCode`, `variantCode`, `showForItem`,
`qtyPerUnitOfMeasure`, `note`.
**Behaviour:** barcode numbers are globally unique, so one already in use is refused
naming the item that owns it. The unit must already exist on the item — otherwise the
till would price the wrong quantity.

#### `set_item_price` (sensitivity: `write`)

Sets the item card's Unit Price and the matching `Sales Price` row. Operation `SetItemPrice`.

**Args:** `itemNo`, `unitPriceExclVat` **or** `retailPriceInclVat`, `customerPriceGroup`
(defaults `ALL` — "All Stores"), `unitOfMeasureCode`, `startingDate`.
**Result:** `itemNo`, `description`, `unitPriceExclVat`, `vatPct`, `priceInclVat`,
`customerPriceGroup`, `unitOfMeasureCode`, `startingDate`, `salesBlocked`, `note`.
**Behaviour:** the amount is stored the way the target price group expects — every group
carrying prices today is VAT-exclusive, but the store-level groups are flagged
VAT-inclusive and the handler grosses the figure up for those. `priceInclVat` comes back
so the caller can confirm the right figure landed.

#### `release_item` (sensitivity: `write`)

Releases a new item for sale, or blocks one. Operation `SetItemBlocked`.

**Args:** `itemNo`, and at least one of `salesBlocked`, `purchasingBlocked`, `blocked`
(each optional — an omitted flag is left unchanged).
**Result:** `itemNo`, `description`, `blocked`, `salesBlocked`, `purchasingBlocked`,
`unitPrice`, `note`.
**Behaviour:** the go-live step for anything `create_item` made. Items with history
cannot be deleted in BC, so blocking is also how one is retired.

### Finance & Accounting — `erp_bc.finance`

Read-only finance tools covering accounts receivable, accounts payable, posted
invoices, the chart of accounts, and bank balances. Customer/vendor find/get
wrappers reuse the same gateway operations as Sales/Purchasing so the finance
agent can be assigned a self-contained toolset.

#### `list_open_customer_entries` (sensitivity: `read`)

Lists a customer's open ledger entries. Operation `ListOpenCustomerEntries`.

**Args:** `customerNo` (string), `overdueOnly` (bool, default false), `documentNoContains` (string, optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `customerNo`, `customerName`, `count`, `totalRemainingLcy`, `totalOverdueLcy`, `entries[]` (each: `entryNo`, `documentType`, `documentNo`, `externalDocumentNo`, `postingDate`, `dueDate`, `currencyCode`, `originalAmountLcy`, `remainingAmountLcy`, `overdue`, `daysOverdue`).
**Behaviour:** empty list when the customer has no open entries; `overdue` and `daysOverdue` computed in AL against the BC work date.

#### `find_customers` (sensitivity: `read`)

Finance-scoped customer search. Operation `FindCustomers` (same as Sales).

**Args:** `nameContains` (string, optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `customers[]` (each: `customerNo`, `name`, `city`, `balanceLcy`, `blocked`).

#### `get_customer` (sensitivity: `read`)

Finance-scoped customer look-up. Operation `GetCustomer`.

**Args:** `customerNo` (string).
**Result:** `customerNo`, `name`, `email`, `phoneNo`, `balanceLcy`, `creditLimitLcy`, `blocked`.

#### `list_customers_with_overdue` (sensitivity: `read`)

Cross-customer overdue report. Operation `ListCustomersWithOverdue`.

**Args:** `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `customers[]` (each: `customerNo`, `name`, `totalOverdueLcy`, `oldestDueDate`).

#### `get_customer_aging` (sensitivity: `read`)

Aging-bucket summary for one customer. Operation `GetCustomerAging`.

**Args:** `customerNo` (string).
**Result:** `customerNo`, `customerName`, `currentLcy`, `days1To30Lcy`, `days31To60Lcy`, `days61To90Lcy`, `over90DaysLcy`, `totalLcy`.

#### `find_vendors` (sensitivity: `read`)

Finance-scoped vendor search. Operation `FindVendors`.

**Args:** `nameContains` (string, optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `vendors[]` (each: `vendorNo`, `name`, `city`, `balanceLcy`, `blocked`).

#### `get_vendor` (sensitivity: `read`)

Finance-scoped vendor look-up. Operation `GetVendor`.

**Args:** `vendorNo` (string).
**Result:** `vendorNo`, `name`, `email`, `phoneNo`, `balanceLcy`, `blocked`.

#### `list_open_vendor_entries` (sensitivity: `read`)

Lists a vendor's open ledger entries. Operation `ListOpenVendorEntries` (AP mirror of `list_open_customer_entries`).

**Args:** `vendorNo` (string), `overdueOnly` (bool, default false), `documentNoContains` (string, optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `vendorNo`, `vendorName`, `count`, `totalRemainingLcy`, `totalOverdueLcy`, `entries[]` (same entry shape as customer ledger).

#### `list_vendors_with_overdue` (sensitivity: `read`)

Cross-vendor overdue report. Operation `ListVendorsWithOverdue`.

**Args:** `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `vendors[]` (each: `vendorNo`, `name`, `totalOverdueLcy`, `oldestDueDate`).

#### `get_sales_invoice` (sensitivity: `read`)

Posted sales invoice look-up. Operation `GetSalesInvoice`.

**Args:** `invoiceNo` (string).
**Result:** `invoiceNo`, `customerNo`, `customerName`, `postingDate`, `documentDate`, `dueDate`, `externalDocumentNo`, `currencyCode`, `amountIncludingVat`, `remainingAmountLcy`, `lines[]`.
**Error:** `ToolValidationException` when no invoice matches.

#### `find_sales_invoices` (sensitivity: `read`)

Search posted sales invoices. Operation `FindSalesInvoices`.

**Args:** `customerNo` (string, optional), `invoiceNoContains` (string, optional), `fromDate` / `toDate` (yyyy-MM-dd, optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `invoices[]` (each: `invoiceNo`, `customerNo`, `customerName`, `postingDate`, `dueDate`, `amountIncludingVat`, `remainingAmountLcy`).

#### `get_purchase_invoice` (sensitivity: `read`)

Posted purchase invoice look-up. Operation `GetPurchaseInvoice`.

**Args:** `invoiceNo` (string).
**Result:** `invoiceNo`, `vendorNo`, `vendorName`, `vendorInvoiceNo`, `postingDate`, `documentDate`, `dueDate`, `currencyCode`, `amountIncludingVat`, `remainingAmountLcy`, `lines[]`.

#### `find_purchase_invoices` (sensitivity: `read`)

Search posted purchase invoices. Operation `FindPurchaseInvoices`.

**Args:** `vendorNo` (string, optional), `invoiceNoContains` (string, optional), `fromDate` / `toDate` (optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `invoices[]`.

#### `find_gl_accounts` (sensitivity: `read`)

Search chart of accounts. Operation `FindGlAccounts`.

**Args:** `noOrNameContains` (string, optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `accounts[]` (each: `glAccountNo`, `name`, `accountType`, `balanceLcy`, `blocked`).

#### `get_gl_account` (sensitivity: `read`)

G/L account look-up. Operation `GetGlAccount`.

**Args:** `glAccountNo` (string).
**Result:** `glAccountNo`, `name`, `accountType`, `balanceLcy`, `blocked`.

#### `list_gl_entries` (sensitivity: `read`)

G/L entries for an account. Operation `ListGlEntries`.

**Args:** `glAccountNo` (string), `fromDate` / `toDate` (optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `glAccountNo`, `glAccountName`, `count`, `entries[]` (each: `entryNo`, `postingDate`, `documentType`, `documentNo`, `description`, `amountLcy`, `debitCredit`).

#### `list_bank_accounts` (sensitivity: `read`)

List bank accounts. Operation `ListBankAccounts`.

**Args:** `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `bankAccounts[]` (each: `bankAccountNo`, `name`, `currencyCode`, `balanceLcy`, `blocked`).

#### `get_bank_account` (sensitivity: `read`)

Bank account look-up. Operation `GetBankAccount`.

**Args:** `bankAccountNo` (string).
**Result:** `bankAccountNo`, `name`, `currencyCode`, `balanceLcy`, `blocked`.

#### `create_bank_account` (sensitivity: `write`)

Creates a bank account **master record**. Operation `CreateBankAccount`. This is setup
data — it does not move money or change any balance.

Two arguments are easy to confuse: `bankAccountNo` is the Business Central record key
(`Bank Account."No."`), while `bankAccountNumber` is the account number **at the bank**
(`Bank Account."Bank Account No."`). Omit `bankAccountNo` to draw the next number from the
Bank Account Nos. series in General Ledger Setup.

**Args:** `name` (string, **required**), `bankAccountNo` (string, optional),
`bankAccPostingGroup` (string — set it, or the account cannot be used in postings),
`currencyCode`, `bankAccountNumber`, `bankBranchNo`, `iban`, `swiftCode`,
`countryRegionCode`, `address`, `city`, `postCode`, `contact`, `phoneNo`, `email`
(all optional).
**Result:** the stored fields plus `blocked` and `numberFromNoSeries` (true when the
number came from the series rather than the caller).

#### `create_journal_batch` (sensitivity: `write`)

Creates an empty, **unposted** general journal batch. Operation `CreateJournalBatch`.
`templateName` defaults to the first non-recurring template of type `General`.

**Args:** `batchName` (string, **required**, max 10 chars), `templateName`, `description`,
`balAccountType`, `balAccountNo` (all optional).
**Result:** `templateName`, `batchName`, `description`, `balAccountType`, `balAccountNo`,
`noSeries`, `posted` (always `false`), `note`.

#### `add_journal_line` (sensitivity: `write`)

Adds one **unposted** line to a journal batch. Operation `AddJournalLine`.

**Sign convention:** `amount` is **positive for a debit** and **negative for a credit**.
`debitAmount` / `creditAmount` are the sign-safe alternative — pass one of the three, never
a combination. The result carries the batch's running totals so the caller can tell whether
the batch balances; `balanceLcy` excludes self-balancing lines (those with their own
`balAccountNo`), which would otherwise make a valid batch look unbalanced.

**Args:** `batchName` (string, **required**), `accountNo` (string, **required**),
`amount` **or** `debitAmount` **or** `creditAmount` (decimal, one required and non-zero),
`templateName`, `postingDate` (defaults to the BC work date), `documentNo` (defaults to the
batch's existing document number, else its number series), `documentType`, `accountType`
(default `G/L Account`), `description`, `currencyCode`, `balAccountType`, `balAccountNo`,
`dimension1Code`, `dimension2Code`, `externalDocumentNo` (all optional).
**Result:** the line fields plus `batchLineCount`, `totalDebitLcy`, `totalCreditLcy`,
`balanceLcy`, `selfBalancingLines`, `balanced`, `posted` (always `false`), `note`.

> **There is no posting tool.** Posting a general journal writes irreversible G/L entries,
> so the gateway stages the batch and a person reviews and posts it in Business Central.
> The AL operation `PostJournalBatch` exists only to return an explicit "not available"
> error instead of a generic unknown-operation failure; no connector tool exposes it. The
> two prerequisites before it could ship — per-caller authorization and idempotency — are
> written up in the TODO at the end of codeunit `ASG AI Gateway Finance Ops`.

#### `get_journal_batch` (sensitivity: `read`)

Reads back a staged batch. Operation `GetJournalBatch`.

**Args:** `batchName` (string, **required**), `templateName` (optional).
**Result:** batch header, `lineCount`, `lines[]` (each: `lineNo`, `postingDate`,
`documentType`, `documentNo`, `accountType`, `accountNo`, `lineDescription`, `amount`,
`amountLcy`, `currencyCode`, `balAccountType`, `balAccountNo`, `dimension1Code`,
`dimension2Code`, `externalDocumentNo`), plus the same totals as `add_journal_line`.

#### `find_intercompany_transactions` (sensitivity: `read`, shared with every agent)

The trail a document leaves when it crosses between ASG companies. Operation `FindIcTransactions`
(gateway codeunit 53105 `ASG AI Gateway Finance Ops`, **gateway 1.8.0.0+**). Plain list, not a
dataset.

The five companies are Business Central IC partners of each other (`ICP-ASG-KSA`, `ICP-ASG-KWT`,
`ICP-ASG-OM`, `ICP-ASG-QA`, `ICP-ASG-UAE`); each partner is also a customer and a vendor in the
counterpart company. An intercompany sale is an ordinary sales order on the `ICP-*` customer —
BC sets *Send IC Document* itself because the customer carries an IC Partner Code — and posting it
writes an **IC Outbox** transaction. *Auto. Send Transactions* is off in every company, so someone
must send it; it then appears in the partner company's **IC Inbox**, where accepting it creates the
purchase document. Handled transactions move to the Handled tables on each side (head office had
1,802 handled outbox transactions and 3 open ones on 2026-09-09).

**Args:** `partnerCode?`, `direction?` (`Outbox` / `Inbox` / Any), `state?` (`Open` / `Handled` /
Any), `documentNo?`, `documentNoContains?`, `sourceType?` (Journal / Sales Document / Purchase
Document), `documentType?` (Order / Invoice / Credit Memo / Return Order / Payment / Refund),
`fromDate?`, `toDate?` (posting date), `top?` (default 50, max 200), `skip?`; `country` chooses
whose outbox/inbox is read.

**Result:** `company`, `companyIcPartnerCode`, `autoSendTransactions`, `openOutbox`, `openInbox`,
`count`, `top`, `skip`, `hasMore`, `note`, `rows[]{transactionNo, direction, state, partnerCode,
partnerName, partnerCompany, transactionSource, sourceType, documentType, documentNo,
originalDocumentNo, postingDate, documentDate, status, icPartnerGlAccNo}` — open outbox, open
inbox, then handled outbox and inbox, each newest first. An open outbox row with status
`No Action` has not been sent yet.

### Purchasing — `erp_bc.purchasing`

The buy-side mirror of Sales: vendors instead of customers, purchase orders instead
of sales orders. Purchase lines carry a **direct unit cost** (what you pay), not a sales price.

#### `find_vendors` (sensitivity: `read`)

Searches vendors by partial name. Operation `FindVendors` (case-insensitive
`contains`, optionally capped at `top`, ordered by name — applied in AL).

**Args:** `nameContains` (string, optional — omit to browse all), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `vendors[]` (each: `vendorNo`, `name`, `city`, `balanceLcy`, `blocked`).
**Behaviour:** when `nameContains` is blank the filter is skipped and the first page
of vendors (by name) is returned; returns an empty list (not an error) when nothing
matches.

#### `get_vendor` (sensitivity: `read`)

Looks up a vendor by number. Operation `GetVendor`.

**Args:** `vendorNo` (string).
**Result:** `vendorNo`, `name`, `email`, `phoneNo`, `balanceLcy`, `blocked`.
**Error:** `ToolValidationException` when no vendor matches, or on backend failure.

#### `create_purchase_order` (sensitivity: `write`)

Creates a purchase order header. Operation `CreatePurchaseOrder` (inserts a
`Purchase Header` of type `Order` and validates the buy-from vendor in AL). The
order has no lines until you add them.

**Args:** `vendorNo` (string), `vendorInvoiceNo` (string, optional).
**Result:** `orderNo`, `vendorNo`, `vendorName`.

#### `add_purchase_order_line` (sensitivity: `write`)

Adds an item line to an existing order. Operation `AddPurchaseOrderLine` (inserts a
`Purchase Line` of type `Item` and validates `No.`, `Quantity` and optional
`Location Code` in AL).

**Args:** `orderNo` (string), `itemNo` (string), `quantity` (decimal > 0), `locationCode` (string, optional).
**Result:** `orderNo`, `lineNo`, `itemNo`, `description`, `quantity`, `directUnitCost`, `lineAmount`.
**Error:** `ToolValidationException` on blank args, non-positive quantity, or backend failure
(e.g. unknown order or item).

#### `get_purchase_order` (sensitivity: `read`)

Looks up an order header by number. Operation `GetPurchaseOrder`.

**Args:** `orderNo` (string).
**Result:** `orderNo`, `vendorNo`, `vendorName`, `status`, `orderDate`, `vendorInvoiceNo`, `currencyCode`, `amountIncludingVat`.
**Error:** `ToolValidationException` when no order matches, or on backend failure.

### Inventory transfers — `erp_bc.inventory`

A transfer order moves stock between two locations through an **in-transit** location:
create the order, add lines, post the shipment (stock leaves the source into transit),
then post the receipt (stock arrives at the destination).

#### `create_transfer_order` (sensitivity: `write`)

Creates a transfer order header. Operation `CreateTransferOrder` (inserts a
`Transfer Header` and validates the from/to/in-transit locations in AL). The order
has no lines until you add them.

**Args:** `transferFromCode` (string), `transferToCode` (string), `inTransitCode` (string), `postingDate`/`shipmentDate`/`receiptDate` (string `yyyy-MM-dd`, optional), `externalDocumentNo` (string, optional), `shortcutDimension1Code` (string, optional — see below).
**Result:** `transferOrderNo`, `transferFromCode`, `transferFromName`, `transferToCode`, `transferToName`, `inTransitCode`, `status`, `postingDate`, `shipmentDate`, `receiptDate`, `externalDocumentNo`, `storeFrom`, `storeTo`, `shortcutDimension1Code`, `shortcutDimension1Name`, `receiptShortcutDimension1Code`, `shortcutDimension1Source`.
**Region/branch (gateway 1.12.0.0+):** every transfer order must carry a Shortcut Dimension 1 Code
(`REGION/BRANCH`: region, dash, branch — `1100-S068` is store S068 in the Central Area). BC 19's Transfer
Header has no default dimensions of its own; the value comes from LS Central's `Store-from` (the source
store card's value, re-applied when the shipment posts), and the receipt posting switches to `Store-to`'s.
Earlier gateways set neither, so their orders had no dimension and would have posted receipts to the source
branch. The gateway now sets `storeFrom` / `storeTo` from the locations and takes the value from, in order:
the source store's card (a different caller value is refused); `shortcutDimension1Code`; the store a
sub-location belongs to (`L068-D` → S068, `L997-DAM` → S997). With none of those (`MK-PLACE`, `HO`, …) the
create is refused. Values must exist, be `Standard` (not a region heading such as `1100`) and not be blocked.
Details: the gateway README, *Region/branch on transfer orders*.
**Error:** `ToolValidationException` on blank from/to/in-transit codes, or backend failure (including no resolvable region/branch).

#### `add_transfer_order_line` (sensitivity: `write`)

Adds an item line to an existing transfer order. Operation `AddTransferOrderLine`
(inserts a `Transfer Line` and validates `Item No.`, optional `Variant Code`, and
`Quantity` in AL; locations come from the header).

**Args:** `transferOrderNo` (string), `itemNo` (string), `quantity` (decimal > 0), `variantCode` (string, optional),
`transferToBinCode` / `transferFromBinCode` (string, optional — the bin at the destination / source location).
**Result:** `transferOrderNo`, `lineNo`, `itemNo`, `variantCode`, `description`, `quantity`, `unitOfMeasureCode`, `qtyToShip`, `qtyToReceive`, `outstandingQuantity`, `transferFromBinCode`, `transferToBinCode`, `shortcutDimension1Code` (inherited from the header), `note` (also warns when the order has no region/branch).
**Bins (gateway 1.8.1.0+):** bins are a line property in BC. A *Bin Mandatory* destination — `MK-PLACE`, the
marketplace warehouse with one bin per platform (`TRENDYOL-MDA`, `NOON-FLEX`, `AMAZON-FBN`, …) — cannot be
received into without a bin on every line, and BC would only say so at receipt. The gateway checks a given
bin against the header's location before touching the line (bin-mandatory, not directed put-away/pick, bin
exists — the same rules as Transfer Line's OnValidate, but the error names the location and lists its bins),
and `note` warns when a bin-mandatory side still has none. `find_locations` reports `binMandatory`;
`find_bins` lists a location's bins.
**Error:** `ToolValidationException` on blank args, non-positive quantity, an unknown bin, or backend failure.

#### `get_transfer_order` (sensitivity: `read`)

Looks up a transfer order header and its lines by number. Operation `GetTransferOrder`.

**Args:** `transferOrderNo` (string).
**Result:** header fields (as in `create_transfer_order`) plus `lineCount` and `lines[]` (each: `lineNo`, `itemNo`, `variantCode`, `description`, `quantity`, `unitOfMeasureCode`, `qtyToShip`, `qtyShipped`, `qtyToReceive`, `qtyReceived`, `outstandingQuantity`, `transferFromBinCode`, `transferToBinCode`, `shortcutDimension1Code`).
**Error:** `ToolValidationException` when no order matches, or on backend failure.

#### `find_transfer_orders` (sensitivity: `read`)

Lists transfer orders, optionally filtered by location. Operation `FindTransferOrders`
(applies `Transfer-from Code` / `Transfer-to Code` ranges, optionally capped at `top` — in AL).

**Args:** `transferFromCode` (string, optional), `transferToCode` (string, optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `transferOrders[]` (each: `transferOrderNo`, `transferFromCode`, `transferToCode`, `inTransitCode`, `status`, `postingDate`, `shipmentDate`, `receiptDate`, `externalDocumentNo`).
**Behaviour:** when both location filters are blank, the first page of orders is returned; empty list (not an error) when nothing matches.

#### `get_transfer_orders` (batch, sensitivity: `read`)

Full detail for several transfer orders in one call. Operation `GetTransferOrders`. Use this instead of
calling `get_transfer_order` once per order — typically after `find_transfer_orders` returns the numbers.

**Args:** `orders[]` (1–100), each item `{transferOrderNo}`.
**Result:** `requested`, `count`, `transferOrders[]` (each the same shape as `get_transfer_order`: header +
`lines[]`), `notFound[]{transferOrderNo,reason}`.

#### `post_transfer_order` (sensitivity: `write`)

Posts a transfer order's shipment and/or receipt. Operation `PostTransferOrder`
(releases the order via `Release Transfer Document` when shipping an Open order, then
runs `TransferOrder-Post Shipment` / `TransferOrder-Post Receipt` in AL).

**Args:** `transferOrderNo` (string), `postType` (string: `Ship` | `Receive` | `ShipAndReceive`, default `ShipAndReceive`).
**Result:** `transferOrderNo`, `postType`, `shipped` (bool), `received` (bool), `completed` (bool — true when the fully-posted order was removed), `status` (present only when the order still exists).
**Error:** `ToolValidationException` on blank `transferOrderNo`, invalid `postType`, or a posting failure (e.g. insufficient inventory, closed posting period).


#### `delete_transfer_order` (sensitivity: `destructive`)

Deletes a transfer order and its lines. Operation `DeleteTransferOrder`. **Irreversible** —
the order can only be created again, not restored.

**Args:** `transferOrderNo` (string, required), `dryRun` (bool, optional — check only,
change nothing), `reopenIfReleased` (bool, optional — reopen a Released order and delete
it in one step).
**Result:** `transferOrderNo`, `transferFromCode`, `transferToCode`, `statusBefore`,
`lineCount`, `dryRun`, `deleted`, `deletable`, `reopened`, `blockerCount`, `blockers[]`
(each: `reason`, `lineNo`, `itemNo`, `description`, `quantity`, `quantityShipped`,
`quantityReceived`, `inTransitQuantity`, `reservedQtyInboundBase`,
`reservedQtyOutboundBase`, `relatedTable`, `detail`), `note`.
**Error:** raised — not returned — when a real (non-dry-run) delete is blocked, with the
full blocker list as the message. A write tool must never report success for something it
did not do, so `deleted` is only ever true when the order is really gone.

##### What it refuses, and why

The rules are not invented; they are exactly what Business Central enforces, read off the
BC 19.3 base application (`Transfer Header.OnDelete` and `Transfer Line.OnDelete`):

| `reason` | BC check | Meaning |
|---|---|---|
| `ShippedNotReceived` | `TestField("Quantity Shipped", "Quantity Received")` and the `(Base)` pair | Stock has shipped but not been received, so it is in the in-transit location. Deleting would strand it with no document to receive it against. Receive it first. |
| `Reserved` | `TestField("Reserved Qty. Inbnd./Outbnd. (Base)", 0)` | The line is reserved. Cancel the reservation in BC. |
| `WarehouseActivity` | `WhseValidateSourceLine.TransLineDelete` | An open warehouse receipt, shipment or activity line (pick/put-away) exists. `relatedTable` names which. |
| `Released` | `TestField(Status, Status::Open)` | BC only deletes orders with status Open. `reopenIfReleased=true` reopens and deletes in one step. |

The gateway evaluates all of them up front so one response names every offending line,
instead of BC aborting on whichever line happened to be first. It then still calls
`Delete(true)` and lets BC re-run its own validation — the pre-flight is for the message,
never a substitute, so an extension subscriber or LS Central rule can still stop the delete.

Two details worth knowing:

* The reservation case genuinely blocks. The header's `OnDelete` calls
  `DeleteDocumentReservation` first, which looks like it clears the way, but that only
  clears item-tracking fields on tracked entries — it does not remove the reservation
  quantity, so the line-level `TestField` still fires.
* Reopening a Released order only flips its Warehouse Request status back to Open
  (`WhseTransferRelease.Reopen`); it does not remove warehouse documents. So a
  `WarehouseActivity` blocker is real and `reopenIfReleased` will not clear it.

A fully shipped **and** received order no longer exists — posting deletes it — so "no
transfer order found" can mean it completed normally.

#### `delete_transfer_orders` (sensitivity: `destructive`) and `check_transfer_order_deletion` (sensitivity: `read`)

Gateway 1.13.0.0+, operations `DeleteTransferOrders` / `CheckTransferOrderDeletion`
(`TransferBatchTools.cs`).

**Why they exist.** The platform asks the run's user to approve every call of a
`destructive` tool — Laravel `StartRunService` sets `requires_approval` on the tool def and
the runtime's `awaitApproval` gates each call. Deleting twenty orders with
`delete_transfer_order` was twenty calls and twenty Approve clicks. `delete_transfer_orders`
takes the list, so it is one call and one approval. Nothing about the gate changes: it is
still per call, same-user, time-limited and fail-closed, and the runtime dispatches exactly
the arguments that were approved, so the list cannot grow after the click.

**Args (both):** `transferOrderNos` (1–50 explicit order numbers — never a filter, so what the
approver reads is what gets deleted), `reopenIfReleased` (bool, optional, applies to the
whole list). No `dryRun` on the delete: checking is `check_transfer_order_deletion`, which is
a read tool and so needs no approval (a dry run of the single delete is still a destructive
call). The schema publishes `minItems: 1` / `maxItems: 50` and `additionalProperties: false`,
so the hub refuses an oversized list before the connector sees it; the connector also trims,
upper-cases and refuses blanks, over-long numbers and duplicates before calling BC.

**Result — delete:** `requested`, `reopenIfReleased`, `deletedCount`, `blockedCount`,
`notFoundCount`, `failedCount`, `deletedOrderNos[]`, `orders[]` (each: `transferOrderNo`,
`outcome` = `deleted` / `blocked` / `notFound` / `failed`, `transferFromCode`,
`transferToCode`, `statusBefore`, `lineCount`, `reopened`, `blockerCount`, `blockers[]`,
`detail`), `batchLogEntryNo`, `note`.
**Result — check:** `requested`, `reopenIfReleased`, `deletableCount`, `blockedCount`,
`notFoundCount`, `deletableOrderNos[]`, `orders[]` (outcome `deletable` / `blocked` /
`notFound`), `note`.

**Partial success, by design.** Every order that can still be deleted is deleted; every other
order is left alone and reported with its reason. The gateway runs each order through the
single delete's own handler, each in its own transaction, so one failure never undoes
another and the rules are exactly `delete_transfer_order`'s. The call itself only fails when
the LIST is invalid — and then nothing has been touched.

**Audit.** The platform keeps one approval record for the batch, holding the full list. The
gateway keeps the batch's request-log row plus one row per order (Operation
`DeleteTransferOrder`, request JSON with `batchLogEntryNo`), written before each delete runs.

**Approval on WhatsApp/Telegram.** The channel prompt shows the count and the numbers:
`erp_bc.inventory.delete_transfer_orders — 20 transferOrderNos: TO-…, TO-…, … (+12 more)`,
plus any flag set to true (`· reopenIfReleased`). Cut to 300 characters, the count and the
flags always survive (vested-ai-core `ChannelApprovalService::batchSummary`).


### Editing — `erp_bc.inventory`

Every optional argument on an edit tool is **nullable**, and that is the whole design:
an omitted field is left alone, a field supplied as empty is **cleared**. `GetDate` cannot
express that on its own (an absent key and an empty one both parse to `0D`), so the AL
handlers gate every field on the gateway's `HasValue` helper rather than on the parsed
value. Callers must send only what is changing — echoing a whole record back would clear
or overwrite fields nobody asked about.

Each tool returns `changedFields[]` listing what actually changed. A field you sent whose
value already matched is not listed, so an update that matched everything is reported as an
error rather than a silent no-op.

#### `update_transfer_order` (sensitivity: `write`)

Corrects a transfer order header. Operation `UpdateTransferOrder`.

**Args:** `transferOrderNo` (required), plus any of `transferFromCode`, `transferToCode`,
`inTransitCode`, `postingDate`, `shipmentDate`, `receiptDate`, `externalDocumentNo`,
`shippingAgentCode`, `shortcutDimension1Code` (gateway 1.12.0.0+).
**Result:** the header and all lines, plus `changedFieldCount`, `changedFields[]`, `note`.

Business Central's rules, applied here:

* A **Released** order only allows `externalDocumentNo` to change — BC gives that field its
  own released-order handling, and every other editable field calls `TestStatusOpen`.
  Checked up front so the message names the situation rather than whichever field failed first.
* Locations cannot change once anything has shipped — `Transfer Line."Transfer-from Code"`
  does `TestField("Quantity Shipped", 0)`.
* The region/branch follows the **source**: a new `transferFromCode` re-derives it (same order
  as `create_transfer_order`), and `storeFrom` / `storeTo` follow the locations. Passing
  `shortcutDimension1Code` sets or repairs it and fills in the store fields of an order made
  before 1.12. It cannot be cleared, and cannot change once a line has shipped (BC's
  `ConfirmShippedDimChange`: the Inventory Interim account would go out of balance per
  dimension).
* Location, date, shipping-agent and region/branch changes **cascade to every line**
  (`TransferHeader.UpdateTransLines`), and shipment/receipt dates recalculate each other
  through the transfer route. The dates that come back may therefore differ from the ones
  sent; the `note` says when a cascade happened.

The handler calls `SetHideValidationDialog(true)` before validating. Without it,
`Transfer-from/to Code` call `Confirm()`, which in a web-service session returns its default
(`false`) — the code would change while the location's name, address and route silently did
not.

#### `update_transfer_order_line` (sensitivity: `write`)

Corrects one line. Operation `UpdateTransferOrderLine`.

**Args:** `transferOrderNo`, `lineNo` (both required), plus any of `quantity`,
`variantCode`, `shipmentDate`, `receiptDate`, `transferToBinCode`, `transferFromBinCode`.
**Result:** the line's fields after the update (bins included), plus `changedFieldCount`, `changedFields[]`, `note`.
**Behaviour:** the order must be Open. `quantity` can never be less than `Quantity Shipped`
— BC raises `must not be less than Quantity Shipped`; the gateway says the same thing with
the numbers in it. A variant cannot change once the line has shipped; the destination bin cannot
change once the line has been received, the source bin once it has shipped. The item on a line
cannot be swapped: delete the line and add the right one.

#### `find_bins` (sensitivity: `read`)

The bins of one location. Operation `FindBins` (gateway 1.8.1.0+). Plain list.

**Args:** `locationCode` (required), `codeContains?`, `itemNo?` (adds the item's on-hand quantity
per bin and its default bin), `includeEmpty?` (default true), `top?` (default 100, max 500), `skip?`.
**Result:** `locationCode`, `locationName`, `binMandatory`, `directedPutAwayAndPick`, `itemNo`,
`count`, `top`, `skip`, `hasMore`, `bins[]{locationCode, code, description, binTypeCode, zoneCode,
empty, dedicated, blockMovement, itemQuantityBase?, defaultForItem?}`.

#### `delete_transfer_order_line` (sensitivity: `write`)

Removes one line, leaving the order. Operation `DeleteTransferOrderLine`.

**Args:** `transferOrderNo`, `lineNo` (both required), `dryRun` (optional).
**Result:** `deleted`, `deletable`, `blockerCount`, `blockers[]` (same shape and reasons as
`delete_transfer_order`), `remainingLineCount`, `note`.
**Behaviour:** shares `AddTransferLineDeleteBlockers` with `delete_transfer_order`, so the
per-line rules cannot drift between the two. Deleting the last line leaves an order with no
lines — legal, but unpostable; the note says so.

#### `update_item` (sensitivity: `write`)

Corrects an item card. Operation `UpdateItem`.

**Args:** `itemNo` (required), plus any of `description`, `description2`,
`searchDescription`, `vendorNo`, `vendorItemNo`, `gtin`, `salesUnitOfMeasure`,
`purchUnitOfMeasure`, `grossWeight`, `netWeight`, `itemCategoryCode`,
`retailProductGroupCode`.
**Result:** the item card, plus `changedFieldCount`, `changedFields[]`, `note`.

Deliberately narrower than `create_item`. These are refused **by name**, pointing at the
right tool, rather than being silently ignored:

| Field | Why | Use instead |
|---|---|---|
| item number | The number came from the category's series and other records point at it | — |
| `baseUnitOfMeasure` | BC calls `TestNoOpenEntriesExist`; stock counted in one unit cannot be reinterpreted in another | `add_item_unit_of_measure` |
| type, costing method | BC calls `TestNoEntriesExist`; both change how the item posts | — |
| price | A price is the `Sales Price` row too, not just the card field | `set_item_price` |
| blocked flags | Releasing for sale is a deliberate step | `release_item` |

Re-filing an item under another category **is** allowed — the catalogue has items that were
re-categorised after creation — and it does **not** renumber the item. `itemCategoryCode`
and `retailProductGroupCode` must agree: across all 84,576 items with a product group the
two match without exception, so a contradiction is refused with both codes named. Changing
only `retailProductGroupCode` is the safe route, because LS Central's own validate moves the
category with it.

#### `count_records` (sensitivity: `read`)

Counts matching rows. Operation `Count`.

**Args:** `entity` (string), `filters[]` (optional), `allCompanies` (bool, optional).
**Result:** `entity`, `count` (single company) or `total`, `accessibleCompanies`, `byCompany[]` (all-companies mode).

#### `sum_records` (sensitivity: `read`)

Sums a decimal field (monetary totals). Operation `Sum`. Use this for **total sales amount**; use `count_records` only when the user wants a **row count**.

**Single sum — Args:** `preset` (optional: `posSales`, `posNetSales`, `customerOrderSales`, `salesInvoices`, `bcSalesOrders`) **or** `entity` + `field`, `filters[]` (optional), `includeCount` (bool, default true).

**Batch — Args:** `items[]` each with `alias?`, `preset?` or `entity` + `field?`, `filters[]`.

**Result (single):** `entity`, `field`, `total`, `count?`.

**Result (batch):** `items[]{alias?,entity,field,total,count?}`, `grandTotal`.

**Examples:**
- Total POS gross sales in 2024: `preset=posSales`, filter `Date` range `2024-01-01..2024-12-31`.
- POS + posted invoices: `items=[{alias:"pos",preset:"posSales",filters:[...]},{alias:"invoices",preset:"salesInvoices",filters:[...]}]`.

#### `net_inventory` (sensitivity: `read`)

LS Central net (available) inventory. Operation `GetNetInventory`.
**Paginated dataset** (SDK 0.3.0): the platform drives the cursor, which maps to the gateway's
`top`/`skip` window over its stable buffer order; the gateway always walks the full buffer, so the
dataset `Total` is the exact qualifying-row count. For a single grand figure use `groupBy=total`.

**Args:** `itemNo?`, `itemNos[]?` (≤500), `descriptionContains?`, `itemCategoryCode?`, `storeNo?`, `storeNos[]?` (≤50), `storeNameContains?`, `locationCode?`, `variantCode?`, `byVariant?`, `groupBy?` (`detail`/`item`/`store`/`category`/`total`), `mode?` (`auto`/`iterate`/`aggregate`), `includeZero?`, `includeComponents?`.

**Row:** item/store/category keys per `groupBy`, the six components, and `netInventory`.


##### Scoping to several items or stores

`itemNos` (max **500**) and `storeNos` (max 50) both take a list and are covered in **one**
request, so comparing six branches is one call rather than six. They combine with the singular
`itemNo` / `storeNo` and with each other, so "these 3 items across these 4 branches" is a single
call — `groupBy detail` for the grid, `groupBy store` to rank branches.

Once the BC filter expression would pass 900 characters the gateway splits `itemNos` into chunks
and runs each read once per chunk into the same accumulator, so no single filter is ever
unbounded (`InvItemFilterChunks` / `MaxFilterExpressionLength` in the codeunit). Chunking is by
expression LENGTH, not by item count, because `Item."No."` is `Code[20]` — a count tuned for
12-character codes would overrun on 20-character ones. The only cost of a long list is extra
round trips inside the one call: 500 ordinary codes is 8 chunks × 6 grouped queries, not 6.

Both lists accept the shapes a routing model actually emits: a JSON array, or a single string
holding several values separated by `,` `;` or `|` (`TolerantListConverter` on the C# side,
`AddInvScope*Nos` on the AL side). Filter metacharacters are stripped so a value can never
turn into a wildcard once the list is joined into a BC filter expression, and every number is
validated up front — an unknown store or item fails the call naming all the bad ones, rather
than silently narrowing the answer to nothing.

`storeNameContains` still resolves exactly **one** branch name (the connector's
`BcStoreResolver` errors on an ambiguous name). For several named branches, resolve them with
`find_stores` and pass `storeNos`.

##### Scoping to *every* matching item (a six-figure catalogue)

`descriptionContains` and `itemCategoryCode` are **filters, not lists, and carry no cap** — the
right scope for "all items that …" over a catalogue far larger than any list. `descriptionContains`
becomes `@*term*` and is pushed into SQL: each of the six aggregate queries carries a
`FilterItemDescription` element over its `Item` join, and the iteration path puts the same filter on
`Item.Description`. Matching 5 items or 5,000 is the same number of queries. It errors only when
**nothing** matches, so a typo still fails loudly instead of reading like "no stock".

(Before gateway 1.8.3.0 `descriptionContains` was pre-resolved into item numbers and failed once
more than 50 items matched, and `itemNos` was capped at 50. Both limits are gone.)

The response echoes the resolved scope back as `itemCount` / `itemNos`, `descriptionFilter`, and
`storeCount` / `storeNos`.

#### `available_inventory` (sensitivity: `read`)

Net inventory less the stock already committed to outbound transfer orders.
Operation `GetAvailableInventory`. **Not shared** — unlike `net_inventory` this stays bound
to the Inventory agent.

**Args:** identical to `net_inventory` (the C# `Args` inherits from it, so the two cannot
drift).
**Result:** every `net_inventory` row field plus:

| Field | Meaning |
|---|---|
| `toReservedQuantity` | Unshipped quantity on transfer orders **leaving** this location — on the shelf, already promised. Subtracted. |
| `availableInventory` | `netInventory - toReservedQuantity`. The number to quote when stock is about to be committed. |
| `toIncomingQuantity` | Unshipped quantity on transfer orders **arriving** at this location. Context only — **never** subtracted, because it is not here yet. |

Envelope totals gain `totalToReservedQuantity`, `totalAvailableInventory` and
`totalToIncomingQuantity`.

##### Where the formula comes from

It reproduces `AvailableQty()` in report 52572 `AvailableItemBylocation-ASG`
(`ASG--Customization/Extension/Reports/REP-52572-AvailableItemas.al`), so the tool and the
report the branches already use give the same number:

```al
AvailableQty := <LSC Inventory Lookup net inventory> - TRLineQty()

TRLineQty():  Transfer Line where "Item No." = item,
                                  "Transfer-from Code" = location,
                                  Quantity > 0
              CalcSums(Quantity, "Quantity Shipped")
              -> Quantity - "Quantity Shipped"
```

`Quantity - Quantity Shipped` is the unshipped part of an outbound transfer. It is identical
to `Transfer Line."Outstanding Quantity"` — the base app maintains that field as exactly this
subtraction in `InitOutstandingQty` — but the gateway computes it the report's way from the
two authoritative quantity fields, so the two can never disagree if the maintained field is
ever stale.

One deviation from the report, deliberate: `TRLineQty()` is declared to return an `Integer`,
which rounds a fractional quantity. The gateway keeps it `Decimal`.

##### Grouping and the store/location mismatch

Transfer orders belong to a **location**; result rows can be per **store**. Where several
stores share one location, that location's commitment appears on each of its rows. The
envelope totals are accumulated separately while walking the transfer lines rather than by
summing rows, so they never double-count.

The gateway makes one pass over the transfer lines in scope and buckets them by the same key
the current `groupBy` uses (detail / item / store / category / total), so emitting a row is a
dictionary lookup rather than a query per row. The item universe is filtered to
`Type = Inventory` exactly as the net figure is — subtracting a commitment from a net figure
that never included the item would be worse than not subtracting at all.

`availableInventory` can be **negative** when more is committed than is on hand. That is a
real state (an over-committed location), and the agent is told to report it rather than
clamp it to zero.

#### `get_pos_transactions` (batch, sensitivity: `read`)

Full detail for several receipts in one call. Operation `GetPosTransactions`. Use this instead of
calling `get_pos_transaction` once per receipt — typically after `find_pos_transactions` returns the
list of identifiers for a branch/day.

**Args:** `transactions[]` (1–100), each item `{receiptNo?` **or** `storeNo + posTerminalNo + transactionNo}`.

**Result:** `requested`, `count`, `transactions[]` (each the same shape as `get_pos_transaction`: header +
`salesLines[]` + `payments[]`), `notFound[]{receiptNo,storeNo,posTerminalNo,transactionNo,reason}`.

#### `get_top_items` (sensitivity: `read`)

Best-selling / top items for a store and/or date range, aggregated server-side from `LSC Trans. Sales
Entry` (grouped by item). Operation `GetTopItems`. Answers "top products for branch X yesterday" in one
call rather than pulling every receipt's lines. Quantities net out returns.

**Args:** `storeNo?` / `storeNameContains?` and/or `fromDate?`/`toDate?` (at least one scope required),
`posTerminalNo?`, `itemCategoryCode?`, `rankBy?` (`quantity` (default) / `netAmount` / `costAmount`),
`top?` (default 10, max 200).

**Result:** `storeNo`, `posTerminalNo`, `fromDate`, `toDate`, `rankBy`, `count`, `distinctItems`,
`scannedLines`, `truncated`, `totalQuantity`, `totalNetAmount`, `totalCostAmount`,
`items[]{rank,itemNo,description,quantity,netAmount,costAmount,discountAmount,lineCount}`.

#### `find_pos_transactions` (sensitivity: `read`)

Browse individual POS receipts (not for totals). Operation `FindPosTransactions`.
**Paginated dataset** (SDK 0.3.0): the LLM never passes `top`/`skip` — the platform drives the
cursor, which maps to the gateway's `top`/`skip` over its deterministic newest-first key. The agent
sees a sample of rows plus a `dataset_ref`; `materialize_dataset` replays the pages for a full export.

**Args:** `storeNo?`, `posTerminalNo?`, `customerNo?`, `receiptNoContains?`, `mobileNumber?`, `mobileNumberContains?`, `customerOrderId?`, `customerOrderIdContains?`, `retrievedFromReceiptNo?`, `retrievedFromReceiptNoContains?`, `fromDate?`, `toDate?`, `transactionType?` (default `Sales`), `returnsOnly?`.

**Row:** `transactionNo`, `storeNo`, `posTerminalNo`, `receiptNo`, `mobileNumber`, `customerOrderId`, `currencyCode`, amounts, `retrievedFromReceiptNo`, `refundReceiptNo`, `saleIsReturnSale`, `transIsMixedSaleRefund`, `linkedToOriginalReceipt`, ….

Requires at least one narrow filter: date range, store, receipt substring, customer, mobile number, customer order ID, or `retrievedFromReceiptNo`.

##### Linking a return to the original sale

`retrievedFromReceiptNo` is LS Central's **"Retrieved from Receipt No."** — the receipt of the
ORIGINAL SALE whose lines the POS pulled in when the refund or exchange was rung up. It is the only
field that ties a return back to the sale it reverses, and it is returned on every POS row and
transaction detail (`linkedToOriginalReceipt` is a convenience boolean for "is it set").

- **Refund → original sale:** read the refund's `retrievedFromReceiptNo`, then
  `get_pos_transaction` with that `receiptNo`.
- **Sale → its returns:** `find_pos_transactions` with `retrievedFromReceiptNo` = the sale's receipt
  number lists every refund/exchange booked against it. This filter is specific enough to stand
  alone, without a date or store filter.

> `saleIsReturnSale` is the POS's own return flag and is **not reliable on its own** — it stays
> `false` on some refunds (an exchange settled with a return voucher, for instance). That is why
> `returnsOnly` filters on a net-positive gross amount (POS sales are stored negative) rather than on
> the flag. `transIsMixedSaleRefund` marks a receipt that mixes sale and refund lines.

#### `query_records` (sensitivity: `read`)

Lists matching rows. Operation `Query`.
**Paginated dataset** (SDK 0.3.0): the platform drives the cursor, which maps to the gateway's
`top`/`skip`; rows stream in the entity's primary-key order, so pages are stable. The agent sees a
sample plus a `dataset_ref`; the full set is exported on demand.

**Args:** `entity` (string), `filters[]` (optional), `fields[]` (optional).
**Row:** field-name → value map (default or requested `fields[]`).

### Retail prices & promotions — `erp_bc.retail`

#### `get_item_prices` (sensitivity: `read`, shared with every agent)

The price check: what an item costs at the till. Operation `GetItemPrices` (gateway codeunit
53109 `ASG AI Gateway Price Ops`, **gateway 1.7.1.0+**; 1.7.0.0 grossed up VAT wrongly for
stores without a VAT business group and dropped the ALL-price-group line discounts).

**Deliberately a plain list, not a dataset.** The `find_*` tools are datasets, so the model sees a
*sample* of rows plus a `dataset_ref` — right for browsing a 40,000-item catalogue, wrong for
"price these eight items", where the user expects all eight and was shown one. `get_item_prices`
therefore returns every row of the page in `rows[]` (`top`, default 100, max 300; `skip` for the
next page) with `hasMore` and a `note` when the scope is larger. Rows are ~1 KB each against the
SDK's 1 MiB result cap, and the tool raises its deadline to 180 s because LS Central's price
routine runs once per item.

Two prices per row, both **including VAT**. `priceInclVat` is the regular shelf price LS Central
resolves for the store — the gateway calls `"LSC Retail Price Utils".GetValidRetailPrice2`, the
routine the POS itself uses when a line is scanned (Sales Price rows by the store's price groups
in priority order, then the item card), so price resolution is not re-implemented anywhere.
`priceAfterDiscountInclVat` is that price after the periodic discounts the POS applies to **one
unit on its own**: the winning **Disc. Offer** (lowest `Priority` among the enabled offers valid
for the store, date and time whose lines reach the item — matching Item → Special Group →
Product Group → Item Category → All, honouring Exclude lines) and then the **automatic Line
Discount** offers. The rules are taken from LS Central's `LSC POS Price Utility` and `LSC POS
Offer Ext. Utility`; see the gateway README section *Item prices*.

Not applied, on purpose: Multibuy, Mix&Match, Total Discount and Tender Type offers need the rest
of the basket, so they are listed in `basketOffers` rather than priced; member-, coupon- and
customer-group-gated offers are skipped (walk-in price); Manual Line Discount offers need a
cashier action.

**Args:** scope — `itemNo?`, `itemNos[]?` (≤500), `barcode?` (priced in the barcode's unit),
`offerNo?` (every item on an offer; Product Group / Item Category / Special Group lines are
expanded, Exclude lines removed, an `All` line is refused) **or** a filter `descriptionContains?`,
`itemCategoryCode?`, `retailProductGroupCode?`, `vendorNo?` (filters also narrow an explicit
list); `storeNo?` / `storeNameContains?` (default: the head-office store from LS Retail Setup
*Local Store No.*); `date?` (default today at the current time; another date is priced at noon);
`unitOfMeasureCode?`; `top?` (default 100, max 300), `skip?`; `explain?` (adds `offerDiagnostics`).

**Result:** `storeNo`, `storeName`, `currencyCode`, `priceDate`, `priceTime`, `offersConsidered`,
`count`, `top`, `skip`, `hasMore`, `note` (how to get the rest, when `hasMore`), and
`rows[]{itemNo, description, type, itemCategoryCode, vendorNo, vendorItemNo, blocked, salesBlocked,
barcode, barcodeUnitOfMeasure, unitOfMeasure, qtyPerUnitOfMeasure, currencyCode, vatPct,
priceExclVat, priceInclVat, priceSource, priceGroup, vatBusPostingGroup, offerNo, offerDescription,
offerType, offerEndingDate, offerDiscountAmountInclVat, offerDiscountPct, lineDiscountOfferNos,
lineDiscountAmountInclVat, totalDiscountAmountInclVat, totalDiscountPct, priceAfterDiscountInclVat,
priceAfterDiscountExclVat, hasDiscount, basketOffers, offerDiagnostics[]?}` — in item-number order.

**Two rules learned from the first live run (2026-09-08), both checked against production data:**

* The head-office pseudo-store (`HO-NEW`, the default) has no *Store VAT Bus. Post. Gr.*; LS
  Central's price routine then looks VAT up under a blank business group, which exists at 0 %,
  and returns the net price as if it were gross (955.65 instead of 1,099.00). The gateway now
  resolves the VAT business group itself (store → LS Retail Setup default → `VAT`) and passes
  it in; `vatBusPostingGroup` on the row says which one.
* Every enabled Line Discount offer here has *Price Group Validation* = "Matches Trans. Line
  Price Gr." with price group `ALL`. LS Central's check compares that with the sales line's
  price group, but posted till lines carry a blank price group and those offers still fire
  hundreds of times a month (OFR-00799: 937 lines in one week) — so the tills do not enforce
  the comparison. The gateway follows the tills: an offer counts as valid when its price
  group is valid in the store. `priceGroup` still reports the group the shelf price came from,
  and `explain=true` shows the rule that let each offer through or kept it out.

#### The three LS Central offer pages

*Discount Offer List*, *Line Discount Offer List* and *MixMatch Offer List* are all views of one
table, `LSC Periodic Discount`, filtered on `Type`. They map onto `find_offers` as follows:

| LS Central page | `find_offers` call |
|---|---|
| Discount Offer List (page 99009508) | `offerType = "Disc. Offer"` |
| Line Discount Offer List (page 99001761) | `offerType = "Line Discount"` |
| MixMatch Offer List (page 99009507) | `offerType = "Mix&Match"` |

`find_offers` defaults to **Enabled** offers; the pages show disabled ones as well, so pass
`status = "Any"` to reproduce a page in full. Each row carries `validToday` (the page's *Valid
Today*: validation-period dates, weekday and hours against now), `triggersPopUpOnPos` (the
Mix&Match column), `lineDiscountExecution` (Automatic vs Manual, for Line Discount offers),
`blockLineDiscountOffer` and `maximumDiscountAmount`. `get_offer` lines now include `exclude`,
`triggerPopUpOnPos`, and the VAT-exclusive `standardPrice` / `offerPrice` / `discountAmount`
alongside the VAT-inclusive figures; `dealPriceOrDiscPct` is **always a percent** — a deal price is
stored in `offerPriceIncludingVat`. For what an offer's items cost now, use `get_item_prices` with
`offerNo`.

### Service & Repairs — `erp_bc.service`

The after-sales repair workflow: a customer brings a product into a branch, it becomes a
**service order**, it may travel to a central service centre and back, it is repaired, then
it is shipped back and invoiced. Handlers live in codeunit `ASG AI Gateway Service Ops`
(53107); requires **AI Gateway 1.3.0.0** or later.

**Every tool in this domain is `read`.** There is deliberately no create/update/post tool:
setting `Service Header`.`Status` in Business Central fires an `OnAfterValidate` trigger in the
ASG customization that sends a **live SMS to the customer**. Service writes are a separate
design exercise — do not add one without revisiting that trigger.

#### Data model

```
Service Item (SVI-…)            the physical unit; persists across every repair it ever has
  └─ Service Order (SVO-…)      one repair job; can cover SEVERAL units
       └─ Service Item Line     one unit on that order (fault codes, warranty dates)
            └─ Service Line     parts (Type=Item) and labour (Type=Resource) for THAT unit
  ├─ posts to → Posted Service Shipment   (unit physically handed back; back-link "Order No.")
  └─ posts to → Posted Service Invoice    (what was charged + technician; back-link "Order No.")
```

Service lines are always returned **nested inside their service item line**, never flat — an
order covering two units otherwise loses which part went on which unit. `get_service_order`
returns `postedInvoices` / `postedShipments`, and both posted documents carry `serviceOrderNo`,
so the chain is walkable in both directions.

#### Tools

| Tool | Operation | Notes |
|---|---|---|
| `find_service_orders` | `FindServiceOrders` | **Paginated dataset.** Filter by store, status, SVI, customer, phone, receipt, date range, partial order no. |
| `get_service_order` | `GetServiceOrder` | Header + units + nested parts/labour lines + posted back-links |
| `get_service_orders` | `GetServiceOrders` | Batch (max 100); reuses `GetServiceOrder.Result` |
| `lookup_service_item` | `ResolveServiceItem` | **Preferred entry point.** Resolves by SVI, serial, receipt, phone, or customer; single match returns the full card + history |
| `get_service_item` | `GetServiceItem` | Unit card + complete lifecycle across orders, shipments, invoices |
| `find_posted_service_invoices` | `FindPostedServiceInvoices` | **Paginated dataset.** Filter by store, technician, customer, SVI, order no., date range |
| `get_posted_service_invoice` | `GetPostedServiceInvoice` | Header + lines with `costCategory` (Parts/Labor/Other) and ready-made parts/labour totals |
| `find_posted_service_shipments` | `FindPostedServiceShipments` | **Paginated dataset.** When the unit was physically handed back |
| `get_posted_service_shipment` | `GetPostedServiceShipment` | Header + units + nested lines |
| `get_service_kpis` | `GetServiceKpis` | Volume + revenue, parts vs labour, grouped by total / month / store / technician / serviceType |
| `get_repair_turnaround` | `GetRepairTurnaround` | Backlog aging by total / store / status, with the In-Transit states broken out |

#### Statuses

Business Central's four service-document statuses are extended by the ASG customization with
three of its own, which the tools and agent treat as first-class:

| Ordinal | Status | Meaning |
|---:|---|---|
| 0–3 | Pending, In Process, Finished, On Hold | Base Business Central |
| 50000 | **In-Transit To** | Unit has left the branch for the service centre |
| 50001 | **In-Transit From** | Repaired unit is travelling back to the branch |
| 50002 | **Damage** | ASG-specific |

#### `get_service_kpis` execution paths

`groupBy` `total` and `month` are answered with `CalcSums` over `Service Invoice Line` — one SQL
aggregate per bucket, so cost scales with the number of buckets, not rows, and any period is fast.
`store`, `technician` and `serviceType` live only on the posted header's customization fields, so
those group the header rows and attribute line amounts to them. That path is bounded by an explicit
scan ceiling (120k headers / 400k lines); exceeding it **errors** asking for a narrower period
rather than returning a partial — and therefore wrong — aggregate.

#### Data-quality caveats encoded in the tool descriptions

These are properties of this specific database, verified against it, and the agent instruction
repeats them:

- **Service Type is blank** on ~4 in 10 service orders and on ~89% of posted service invoices.
  Filtering by it silently excludes most records.
- **Repair Status Code is empty on every row** — deliberately not exposed as a filter.
- **`Service Item`.`Sales Date` is never populated** — never date a unit by it.
- **`Service Header`.`Finishing Date` is never populated** — so completed-repair duration cannot be
  derived; `get_repair_turnaround` ages from Order Date to `asOfDate` and says so in `agingBasis`.
- **Store / branch is reliable** (~100% on orders and posted invoices) and is the dimension to scope
  and group by. Technician is on ~94% of posted invoices.

#### Customization fields without an app dependency

`Store No.`, `Store Name`, `SVI`, `Technician ID`, `Service Type` and friends are declared in a
separate ASG extension that this gateway does **not** depend on. They are read through
`RecordRef`/`FieldRef` by field number with a caption fallback — the same idiom codeunit 53101
already uses for the LSC Transaction Header `Mobile Number` field — so the gateway stays
installable without that extension and every such field simply reads blank when it is absent.

#### Service credit memos

Covered by **generic entity access only** (`erp_bc.data.query_records` with entity
`Service Cr.Memo Header` / `Service Cr.Memo Line`). There are four in the database; dedicated
tools are not warranted.

#### Whitelisted service entities (`query_records` / `count_records` / `sum_records`)

`Service Header`, `Service Line`, `Service Item Line`, `Service Item`, `Service Item Group`,
`Service Item Component`, `Service Item Log`, `Service Invoice Header`, `Service Invoice Line`,
`Service Shipment Header`, `Service Shipment Line`, `Service Shipment Item Line`,
`Service Cr.Memo Header`, `Service Cr.Memo Line`, `Service Ledger Entry`,
`Service Order Allocation`, `Service Order Type` — each with the usual aliases
(e.g. `Service Order`, `Posted Service Invoice`, `SVI`).

---

> Field names are **stable, connector-facing camelCase** (`customerNo`, `balanceLcy`,
> `directUnitCost`, `amountIncludingVat`, …) owned by the AL gateway — they do not
> change when the underlying BC pages or tables change. The gateway maps them to the
> real BC fields internally (see codeunit `ASG AI Gateway`). To add or rename a field,
> change the gateway and the matching `Result` property together.

---

## Building and running with Docker

Build from this directory (the SDK is restored from NuGet):

```bash
cd bc-connector

docker build \
  --platform linux/amd64 \
  -t bc-connector:local \
  .
```

Run the image (pass every required variable):

```bash
docker run --rm \
  -e VESTED_CONNECTOR_TOKEN=<your-token> \
  -e VESTED_CONNECTOR_HUB=hub.example.com:4443 \
  -e BC_BASE_URL=https://bc-host:8048/BC/ODataV4 \
  -e BC_COMPANY=ASG \
  -e VESTED_CREDENTIAL_PRIVATE_KEY_FILE=/run/secrets/vested-credential-key.pem \
  -v /path/to/vested-credential-key.pem:/run/secrets/vested-credential-key.pem:ro \
  bc-connector:local
```

> Mount the credential private key rather than passing it with `-e`: an inline PEM
> is multi-line and shows up in `docker inspect` and shell history.

> The container must be able to reach the on-prem BC server. If BC uses a
> self-signed TLS certificate, either trust it in the image or terminate TLS at a
> reverse proxy.

---

## Adapting / extending

1. **Add a new operation (the common case):** add a `case` branch + `Handle*`
   procedure in the AL codeunit `ASG AI Gateway`, then add a matching `[Tool]` +
   `ToolHandler<,>` here that calls `BcClient.ExecuteAsync<Result>("YourOp", args, key)`.
   The assembly scanner in `ConnectorHost.ScanAssembly` picks the tool up
   automatically. Keep the camelCase JSON keys in sync on both sides.
   **Stamp every `Args` property with `[JsonPropertyName("camelCase")]`** (see
   `PriceTools.cs`): the SDK publishes the argument schema with the C# property names
   as-is — PascalCase — under `additionalProperties: false`, and the hub validates the
   model's arguments against that schema before the call reaches the connector. The
   descriptions and agent prompts here all spell arguments camelCase, so a model that
   follows the prose is rejected with `additional properties 'itemNos' not allowed`
   unless the schema says `itemNos` too. The SDK binds arguments case-insensitively, so
   the attribute costs nothing on the connector side.
2. **Add agents:** declare another `[Agent]`; remember tool keys must start with
   `<agentKey>.`.
3. **Switch to OAuth (e.g. BC cloud):** replace the `Basic` header in
   `BcClient.Configure()` with a bearer token and point `BC_BASE_URL` at the cloud
   `…/ODataV4` base. The company routing and `ExecuteAsync` logic is unchanged.
4. **Update `.env.example`** with any new variables.

---

## See also

- [connector-sdk-dotnet quickstart](https://github.com/vestedai/connector-sdk-dotnet/blob/main/docs/quickstart.md) — end-to-end quickstart for the .NET SDK
- [connector-sdk-dotnet API reference](https://github.com/vestedai/connector-sdk-dotnet/blob/main/docs/api.md) — full `[Agent]`, `[Tool]`, `ConnectorHost` API reference
- [connector-sdk-dotnet concepts](https://github.com/vestedai/connector-sdk-dotnet/blob/main/docs/concepts.md) — agents, tools, sensitivity, and the wire protocol
