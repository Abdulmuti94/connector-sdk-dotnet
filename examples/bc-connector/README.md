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
| `erp_bc.sales` | BC Sales | Customer search/look-ups and sales order creation (header + lines) |
| `erp_bc.inventory` | BC Inventory | Item search and stock look-ups |
| `erp_bc.finance` | BC Finance & Accounting | AR/AP ledger entries, aging, posted invoices, G/L accounts, and bank balances |
| `erp_bc.purchasing` | BC Purchasing | Vendor search/look-ups and purchase order creation (header + lines) |
| `erp_bc.data` | BC Data & Analytics | Generic count, sum, query, and company list across whitelisted entities |
| `erp_bc.retail` | BC Retail & POS | POS transactions, customer orders, retail sales totals and counts |

### Tools

| Tool key | Sensitivity | Description |
|---|---|---|
| `erp_bc.sales.lookup_customer` | `read` | **Preferred.** Resolve a customer by No. or partial name in one call (`ResolveCustomer`) |
| `erp_bc.sales.find_customers` | `read` | Search customers whose name contains a substring; returns all matches unless `top` is set (no balance by default) |
| `erp_bc.sales.get_customer` | `read` | Look up a customer by No.; returns name, contact, balance, credit limit, blocked status |
| `erp_bc.sales.create_sales_order` | `write` | Create a sales order header for a customer; returns the generated order number |
| `erp_bc.sales.add_sales_order_line` | `write` | Add an item line (item No. + quantity) to an existing sales order |
| `erp_bc.sales.post_sales_order` | `write` | Post a sales order — ship and/or invoice (`PostSalesOrder`) |
| `erp_bc.sales.get_sales_order` | `read` | Look up an order by No.; returns customer, status, dates, and total incl. VAT |
| `erp_bc.inventory.lookup_item` | `read` | **Preferred.** Resolve an item by No. or partial description in one call (`ResolveItem`) |
| `erp_bc.inventory.find_items` | `read` | Search items whose description contains a substring; returns all matches unless `top` is set (no inventory by default) |
| `erp_bc.inventory.get_item` | `read` | Look up an item by No.; returns description, unit price, on-hand inventory |
| `erp_bc.inventory.net_inventory` | `read` | LS Central net (available) store inventory; group by detail/item/store/category (`GetNetInventory`) |
| `erp_bc.inventory.find_stores` | `read` | List LS Central stores with name, location code, and location name (`FindStores`) |
| `erp_bc.inventory.find_locations` | `read` | List BC warehouse locations with code and name (`FindLocations`) |
| `erp_bc.finance.list_open_customer_entries` | `read` | List a customer's open ledger entries with overdue flags and aging detail |
| `erp_bc.finance.lookup_customer` | `read` | **Preferred.** Resolve a customer by No. or partial name in one call (`ResolveCustomer`) |
| `erp_bc.finance.find_customers` | `read` | Search customers by name (finance-scoped wrapper) |
| `erp_bc.finance.get_customer` | `read` | Look up a customer by No.; balance, credit limit, blocked |
| `erp_bc.finance.list_customers_with_overdue` | `read` | List customers with overdue open receivables |
| `erp_bc.finance.get_customer_aging` | `read` | Aging-bucket summary for one customer's open receivables |
| `erp_bc.finance.lookup_vendor` | `read` | **Preferred.** Resolve a vendor by No. or partial name in one call (`ResolveVendor`) |
| `erp_bc.finance.find_vendors` | `read` | Search vendors by name (finance-scoped wrapper) |
| `erp_bc.finance.get_vendor` | `read` | Look up a vendor by No.; balance owed, blocked |
| `erp_bc.finance.list_open_vendor_entries` | `read` | List a vendor's open ledger entries with overdue flags |
| `erp_bc.finance.list_vendors_with_overdue` | `read` | List vendors with overdue open payables |
| `erp_bc.finance.get_sales_invoice` | `read` | Look up a posted sales invoice by No.; header + lines |
| `erp_bc.finance.find_sales_invoices` | `read` | Search posted sales invoices by customer, no., or date |
| `erp_bc.finance.get_purchase_invoice` | `read` | Look up a posted purchase invoice by No.; header + lines |
| `erp_bc.finance.find_purchase_invoices` | `read` | Search posted purchase invoices by vendor, no., or date |
| `erp_bc.finance.find_gl_accounts` | `read` | Search G/L accounts by number or name |
| `erp_bc.finance.get_gl_account` | `read` | Look up a G/L account by No.; balance, type, blocked |
| `erp_bc.finance.list_gl_entries` | `read` | List G/L entries for an account, optionally by date range |
| `erp_bc.finance.list_bank_accounts` | `read` | List bank accounts with balances |
| `erp_bc.finance.get_bank_account` | `read` | Look up a bank account by No.; balance and currency |
| `erp_bc.purchasing.lookup_vendor` | `read` | **Preferred.** Resolve a vendor by No. or partial name in one call (`ResolveVendor`) |
| `erp_bc.purchasing.find_vendors` | `read` | Search vendors whose name contains a substring; returns all matches unless `top` is set (no balance by default) |
| `erp_bc.purchasing.get_vendor` | `read` | Look up a vendor by No.; returns name, contact, balance owed, blocked status |
| `erp_bc.purchasing.create_purchase_order` | `write` | Create a purchase order header for a vendor; returns the generated order number |
| `erp_bc.purchasing.add_purchase_order_line` | `write` | Add an item line (item No. + quantity) to an existing purchase order |
| `erp_bc.purchasing.post_purchase_order` | `write` | Post a purchase order — receive and/or invoice (`PostPurchaseOrder`) |
| `erp_bc.purchasing.get_purchase_order` | `read` | Look up a purchase order by No.; returns vendor, status, dates, and total incl. VAT |
| `erp_bc.inventory.create_transfer_order` | `write` | Create a transfer order header (from/to/in-transit locations); returns the generated number |
| `erp_bc.inventory.add_transfer_order_line` | `write` | Add an item line (item No. + quantity) to an existing transfer order |
| `erp_bc.inventory.get_transfer_order` | `read` | Look up a transfer order by No.; returns header (locations, status, dates) and all lines |
| `erp_bc.inventory.find_transfer_orders` | `read` | List transfer orders, optionally filtered by source/destination location |
| `erp_bc.inventory.post_transfer_order` | `write` | Post a transfer order's shipment and/or receipt (Ship \| Receive \| ShipAndReceive) |
| `erp_bc.data.count_records` | `read` | Count records of any supported entity, with optional filters; `allCompanies` returns a per-company breakdown + total |
| `erp_bc.data.sum_records` | `read` | Sum a numeric field (sales totals, not row counts); presets: `posSales`, `posNetSales`, `customerOrderSales`, `salesInvoices`, `bcSalesOrders`; batch `items[]` returns `grandTotal` |
| `erp_bc.data.query_records` | `read` | List/filter records of any supported entity, returning chosen or default fields (ad-hoc queries) |
| `erp_bc.data.list_companies` | `read` | List the Business Central companies available on the server |
| `erp_bc.data.run_sql` | `read` | Run a single validated read-only `SELECT` directly against the BC SQL Server (ad-hoc joins/`GROUP BY`/window functions); no row cap, so use `TOP`. Optional — enabled by `BC_SQL_*`. See *Direct read-only SQL* below |
| `erp_bc.retail.get_pos_transaction` | `read` | Look up a POS receipt by receipt number or store + terminal + transaction no. (`GetPosTransaction`) |
| `erp_bc.retail.find_pos_transactions` | `read` | Browse POS receipts; filter by store, customer, receipt, **mobile number**, **customer order ID**, date range; paginate with `skip` (`FindPosTransactions`) |
| `erp_bc.retail.get_customer_order` | `read` | Look up an LS Central customer order by document ID or external ID |
| `erp_bc.retail.find_customer_orders` | `read` | Search customer orders by store, customer, mobile phone, status, dates |
| `erp_bc.retail.sum_sales` | `read` | Retail-scoped alias for POS/customer-order sales totals (`Sum` presets) |
| `erp_bc.retail.count_transactions` | `read` | Retail-scoped alias for counting POS transaction rows (`Count`) |

> **Finance extended tools** (`get_customer_aging`, `find_sales_invoices`, G/L, bank, vendor ledger, etc.)
> call gateway operations that are **not yet implemented** in the ASG AI Gateway `Dispatch` table.
> Until those handlers exist in AL, use `query_records`, `count_records`, or `sum_records` on the
> whitelisted entities documented in the Data agent (e.g. `Sales Invoice Header`, `Cust. Ledger Entry`).

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) (for `dotnet run`)
- A Vested AI connector token and hub address (see the platform docs)
- An on-prem Business Central instance with:
  - **API services enabled** on the BC Server instance (`ODataServicesEnabled` /
    `ApiServicesEnabled` — both on by default).
  - **NavUserPassword** credential type (basic auth) configured.
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
| `BC_USERNAME` | yes | BC user name |
| `BC_PASSWORD` | yes | The user's **Web Service Access Key** (recommended) or password |
| `BC_TIMEOUT_SECONDS` | no | Per-request HTTP timeout in seconds (default `30`) |
| `BC_SQL_CONNECTION` | no | Full read-only SQL Server connection string. Enables `erp_bc.data.run_sql`. Use this **or** the discrete `BC_SQL_*` variables below. |
| `BC_SQL_SERVER` | no | SQL Server host/instance (e.g. `bc-sql\\BC`). With `BC_SQL_DATABASE/USERNAME/PASSWORD`, the connector builds a read-only (`ApplicationIntent=ReadOnly`, `Encrypt=true`) connection string. |
| `BC_SQL_DATABASE` | no | BC database name. Required when `BC_SQL_SERVER` is set. |
| `BC_SQL_USERNAME` | no | **Read-only** SQL login (granted `SELECT`, denied `INSERT/UPDATE/DELETE/EXEC`). Required when `BC_SQL_SERVER` is set. |
| `BC_SQL_PASSWORD` | no | Password for the read-only SQL login. Required when `BC_SQL_SERVER` is set. |
| `BC_SQL_TRUST_CERT` | no | `false` to require a CA-signed server cert; default trusts a self-signed cert (typical on-prem). |
| `BC_SQL_TIMEOUT_SECONDS` | no | SQL connect + command timeout in seconds (default `60`). |
| `BC_SQL_ALLOWED_SCHEMAS` | no | Comma-separated schema allow-list (e.g. `ai`). When set, `run_sql` only permits tables/views in those schemas. Empty = rely on the login's grants. |

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
3. Reads run under `READ UNCOMMITTED` so analytics never block live BC users. There is no row cap —
   the model is instructed to use `TOP`; the command timeout is the backstop against runaway scans.

> ⚠️ **Not yet scoped per caller.** `run_sql` currently returns **unscoped** data (it bypasses the
> gateway's `departments` filter). Until per-caller ERP-permission scoping is added (see the `TODO`
> in `BcSqlClient.RunSelectAsync`), point it at non-sensitive views/tables, or set
> `BC_SQL_ALLOWED_SCHEMAS` to a curated read-only view schema.

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
The `Authorization: Basic` header carries `base64(BC_USERNAME:BC_PASSWORD)`.

### Caller identity

Every `ToolCallRequest` carries the calling user's ERP identity in `ToolContext`
(`EmployeeNo`, `ErpIdentifier`, `ErpDepartmentIdentifiers`). These are
**authenticated by the hub, not model-supplied** — they are therefore **not** part
of any tool's `Args` schema. `BcClient.ExecuteAsync` injects them from `ctx` at call
time (via `BcCallerContext`) into the dedicated `callerContext` action field, kept
separate from `payload` so the model can never reach or spoof them.

- **Missing identity** (`EmployeeNo` and `ErpIdentifier` both empty):
  - **Read tools** proceed **unscoped** — the identity is still forwarded for audit.
  - **Write/sensitive tools** (`create_*`, `add_*_line`, `post_transfer_order`) are
    **rejected** up front via `BcCallerContext.Require` — a data-changing action must
    be attributable to a known user. An empty department list is always allowed.
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
├── Agents.cs             # [Agent] + [Instruction] declarations (5 agents)
├── Tools.cs              # Thirty-five [Tool] ToolHandler<,> implementations (each dispatches one operation)
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

**Args:** `descriptionContains` (string, optional — omit to browse all), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `items[]` (each: `itemNo`, `description`, `unitPrice`, `inventory`).
**Behaviour:** when `descriptionContains` is blank the filter is skipped and the first
page of items (by description) is returned; returns an empty list (not an error) when
nothing matches.

#### `get_item` (sensitivity: `read`)

Looks up an item by number. Operation `GetItem`.

**Args:** `itemNo` (string).
**Result:** `itemNo`, `description`, `baseUnitOfMeasure`, `unitPrice`, `inventory`.
**Error:** `ToolValidationException` when no item matches, or on backend failure.

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

**Args:** `transferFromCode` (string), `transferToCode` (string), `inTransitCode` (string), `postingDate`/`shipmentDate`/`receiptDate` (string `yyyy-MM-dd`, optional), `externalDocumentNo` (string, optional).
**Result:** `transferOrderNo`, `transferFromCode`, `transferFromName`, `transferToCode`, `transferToName`, `inTransitCode`, `status`, `postingDate`, `shipmentDate`, `receiptDate`, `externalDocumentNo`.
**Error:** `ToolValidationException` on blank from/to/in-transit codes, or backend failure.

#### `add_transfer_order_line` (sensitivity: `write`)

Adds an item line to an existing transfer order. Operation `AddTransferOrderLine`
(inserts a `Transfer Line` and validates `Item No.`, optional `Variant Code`, and
`Quantity` in AL; locations come from the header).

**Args:** `transferOrderNo` (string), `itemNo` (string), `quantity` (decimal > 0), `variantCode` (string, optional).
**Result:** `transferOrderNo`, `lineNo`, `itemNo`, `variantCode`, `description`, `quantity`, `unitOfMeasureCode`, `qtyToShip`, `qtyToReceive`, `outstandingQuantity`.
**Error:** `ToolValidationException` on blank args, non-positive quantity, or backend failure.

#### `get_transfer_order` (sensitivity: `read`)

Looks up a transfer order header and its lines by number. Operation `GetTransferOrder`.

**Args:** `transferOrderNo` (string).
**Result:** header fields (as in `create_transfer_order`) plus `lineCount` and `lines[]` (each: `lineNo`, `itemNo`, `variantCode`, `description`, `quantity`, `unitOfMeasureCode`, `qtyToShip`, `qtyShipped`, `qtyToReceive`, `qtyReceived`, `outstandingQuantity`).
**Error:** `ToolValidationException` when no order matches, or on backend failure.

#### `find_transfer_orders` (sensitivity: `read`)

Lists transfer orders, optionally filtered by location. Operation `FindTransferOrders`
(applies `Transfer-from Code` / `Transfer-to Code` ranges, optionally capped at `top` — in AL).

**Args:** `transferFromCode` (string, optional), `transferToCode` (string, optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `count`, `transferOrders[]` (each: `transferOrderNo`, `transferFromCode`, `transferToCode`, `inTransitCode`, `status`, `postingDate`, `shipmentDate`, `receiptDate`, `externalDocumentNo`).
**Behaviour:** when both location filters are blank, the first page of orders is returned; empty list (not an error) when nothing matches.

#### `post_transfer_order` (sensitivity: `write`)

Posts a transfer order's shipment and/or receipt. Operation `PostTransferOrder`
(releases the order via `Release Transfer Document` when shipping an Open order, then
runs `TransferOrder-Post Shipment` / `TransferOrder-Post Receipt` in AL).

**Args:** `transferOrderNo` (string), `postType` (string: `Ship` | `Receive` | `ShipAndReceive`, default `ShipAndReceive`).
**Result:** `transferOrderNo`, `postType`, `shipped` (bool), `received` (bool), `completed` (bool — true when the fully-posted order was removed), `status` (present only when the order still exists).
**Error:** `ToolValidationException` on blank `transferOrderNo`, invalid `postType`, or a posting failure (e.g. insufficient inventory, closed posting period).

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

**Args:** `itemNo?`, `descriptionContains?`, `itemCategoryCode?`, `storeNo?`, `locationCode?`, `variantCode?`, `byVariant?`, `groupBy?` (`detail`/`item`/`store`/`category`/`total`), `mode?` (`auto`/`iterate`/`aggregate`), `top?`, `includeZero?`, `includeComponents?`.

**Result:** `groupBy`, `byVariant`, `path`, `count`, `hasMore`, `totalNetInventory`, `rows[]`.

#### `find_pos_transactions` (sensitivity: `read`)

Browse individual POS receipts (not for totals). Operation `FindPosTransactions`.

**Args:** `storeNo?`, `posTerminalNo?`, `customerNo?`, `receiptNoContains?`, `mobileNumber?`, `mobileNumberContains?`, `customerOrderId?`, `customerOrderIdContains?`, `fromDate?`, `toDate?`, `transactionType?` (default `Sales`), `top?` (default 50, max 500), `skip?`.

**Result:** `count`, `top`, `skip`, `hasMore`, `transactions[]{transactionNo,storeNo,posTerminalNo,receiptNo,mobileNumber,customerOrderId,...}`.

Requires at least one narrow filter: date range, store, receipt substring, customer, mobile number, or customer order ID.

#### `query_records` (sensitivity: `read`)

Lists matching rows. Operation `Query`.

**Args:** `entity` (string), `filters[]` (optional), `fields[]` (optional), `top` (int, optional — omit for all matches; positive value caps results).
**Result:** `entity`, `count`, `rows[]`.

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
  -e BC_USERNAME=<bc-user> \
  -e BC_PASSWORD=<web-service-access-key> \
  bc-connector:local
```

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
