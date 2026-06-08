# Business Central Connector — .NET example

A runnable Vested AI connector for **on-prem Dynamics 365 Business Central**,
demonstrating the core `VestedAI.ConnectorSdk` attribute API against a real ERP
backend over OData V4 with NavUserPassword (basic auth).

The connector ships four agents and thirteen tools that read and write Business
Central data through its published OData V4 web services.

---

## What this example demonstrates

| Feature | Where |
|---|---|
| `[Agent]` + `[Instruction]` attributes | `Agents.cs` |
| `[Tool]` with `Sensitivity` | `Tools.cs` |
| POCO `Args` / `Result` with `[Description]` | `Tools.cs` |
| `ToolHandler<TArgs, TResult>` base class | `Tools.cs` |
| `ToolValidationException` for not-found / backend errors | `Tools.cs`, `BcClient.cs` |
| Multi-row OData search (`contains`, `$top`, `$orderby`) returning a list result | `Tools.cs` |
| Posting document lines via an OData navigation property | `BcClient.cs`, `Tools.cs` |
| Calling a real backend (BC OData V4, basic auth) | `BcClient.cs` |
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
| `erp_bc.finance` | BC Finance | Accounts-receivable look-ups: open and overdue customer ledger entries |
| `erp_bc.purchasing` | BC Purchasing | Vendor search/look-ups and purchase order creation (header + lines) |

### Tools

| Tool key | Sensitivity | Description |
|---|---|---|
| `erp_bc.sales.find_customers` | `read` | Search customers whose name contains a substring; returns up to N matches |
| `erp_bc.sales.get_customer` | `read` | Look up a customer by No.; returns name, contact, balance, credit limit, blocked status |
| `erp_bc.sales.create_sales_order` | `write` | Create a sales order header for a customer; returns the generated order number |
| `erp_bc.sales.add_sales_order_line` | `write` | Add an item line (item No. + quantity) to an existing sales order |
| `erp_bc.sales.get_sales_order` | `read` | Look up an order by No.; returns customer, status, dates, and total incl. VAT |
| `erp_bc.inventory.find_items` | `read` | Search items whose description contains a substring; returns up to N matches |
| `erp_bc.inventory.get_item` | `read` | Look up an item by No.; returns description, unit price, on-hand inventory |
| `erp_bc.finance.list_open_customer_entries` | `read` | List a customer's open ledger entries, flagging which are overdue |
| `erp_bc.purchasing.find_vendors` | `read` | Search vendors whose name contains a substring; returns up to N matches |
| `erp_bc.purchasing.get_vendor` | `read` | Look up a vendor by No.; returns name, contact, balance owed, blocked status |
| `erp_bc.purchasing.create_purchase_order` | `write` | Create a purchase order header for a vendor; returns the generated order number |
| `erp_bc.purchasing.add_purchase_order_line` | `write` | Add an item line (item No. + quantity) to an existing purchase order |
| `erp_bc.purchasing.get_purchase_order` | `read` | Look up a purchase order by No.; returns vendor, status, dates, and total incl. VAT |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) (for `dotnet run`)
- A Vested AI connector token and hub address (see the platform docs)
- An on-prem Business Central instance with:
  - **OData V4 web services enabled** on the BC Server instance.
  - **NavUserPassword** credential type (basic auth) configured.
  - The pages used here published as web services with these service names:
    `Customers`, `Vendors`, `Items`, `SalesOrder` and `PurchaseOrder` (each with its
    lines subpage reachable through the `SalesOrderSalesLines` /
    `PurchaseOrderPurchLines` navigation property), and `CustomerLedgerEntries`.
    Adjust the entity-set names and field names in `Tools.cs` if your published
    services differ.

---

## Configuration

Copy `.env.example` to `.env` and fill in the values:

| Variable | Required | Description |
|---|---|---|
| `VESTED_CONNECTOR_TOKEN` | yes | Connector JWT from the Vested AI platform |
| `VESTED_CONNECTOR_HUB` | yes | Hub gRPC endpoint, `host:port` |
| `LOG_LEVEL` | no | `Trace`…`Error` (default `Information`) |
| `BC_BASE_URL` | yes | OData V4 base URL: `http(s)://<host>:<port>/<serverinstance>/ODataV4` |
| `BC_COMPANY` | yes | Company name exactly as in BC (used in `Company('…')`) |
| `BC_USERNAME` | yes | BC user name |
| `BC_PASSWORD` | yes | The user's **Web Service Access Key** (recommended) or password |
| `BC_TIMEOUT_SECONDS` | no | Per-request HTTP timeout in seconds (default `30`) |

`BcClient.Configure()` (called from `Program.cs`) validates the `BC_*` variables
at startup and throws with a clear message if any are missing — the process exits
before connecting to the hub.

### How requests are formed

Each tool call hits:

```
{BC_BASE_URL}/Company('{BC_COMPANY}')/{EntitySet}?{ODataQuery}
```

for reads (`GET`), or `POST`s a JSON body to the same path (without the query)
to create records. The `Authorization: Basic` header carries
`base64(BC_USERNAME:BC_PASSWORD)`.

---

## Project layout

```
bc-connector/
├── BcConnector.csproj    # Exe project; references the SDK via ProjectReference
├── Program.cs            # BcClient.Configure() → ConnectorHost → RunFromEnvironmentAsync
├── Agents.cs             # [Agent] + [Instruction] declarations (4 agents)
├── Tools.cs              # Thirteen [Tool] ToolHandler<,> implementations
├── BcClient.cs           # Shared BC OData V4 client (basic auth; query / create / nested-create helpers)
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
agents and thirteen tools, then enters the steady-state receive loop. Press Ctrl-C
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

Searches customers by partial name via
`GET Customers?$filter=contains(Name,'<text>')&$top=<n>&$orderby=Name`.

**Args:** `nameContains` (string), `top` (int, 1–50, default 10).
**Result:** `count`, `customers[]` (each: `customerNo`, `name`, `city`, `balanceLcy`, `blocked`).
**Behaviour:** returns an empty list (not an error) when nothing matches; throws
`ToolValidationException` when `nameContains` is blank or on backend failure.

#### `get_customer` (sensitivity: `read`)

Looks up a customer by number via `GET Customers?$filter=No eq '<no>'`.

**Args:** `customerNo` (string).
**Result:** `customerNo`, `name`, `email`, `phoneNo`, `balanceLcy`, `creditLimitLcy`, `blocked`.
**Error:** `ToolValidationException` when no customer matches, or on auth / network / HTTP failures.

#### `create_sales_order` (sensitivity: `write`)

Creates a sales order header via `POST SalesOrder` with `Sell_to_Customer_No`
(and optional `External_Document_No`). The order has no lines until you add them.

**Args:** `customerNo` (string), `externalDocumentNo` (string, optional).
**Result:** `orderNo`, `customerNo`, `customerName`.

#### `add_sales_order_line` (sensitivity: `write`)

Adds an item line to an existing order via
`POST SalesOrder(Document_Type='Order',No='<orderNo>')/SalesOrderSalesLines`
with `Type='Item'`, `No`, `Quantity` (and optional `Location_Code`).

**Args:** `orderNo` (string), `itemNo` (string), `quantity` (decimal > 0), `locationCode` (string, optional).
**Result:** `orderNo`, `lineNo`, `itemNo`, `description`, `quantity`, `unitPrice`, `lineAmount`.
**Error:** `ToolValidationException` on blank args, non-positive quantity, or backend failure
(e.g. unknown order or item).

#### `get_sales_order` (sensitivity: `read`)

Looks up an order header by number via `GET SalesOrder?$filter=No eq '<orderNo>'`.

**Args:** `orderNo` (string).
**Result:** `orderNo`, `customerNo`, `customerName`, `status`, `orderDate`, `externalDocumentNo`, `currencyCode`, `amountIncludingVat`.
**Error:** `ToolValidationException` when no order matches, or on backend failure.

### Inventory — `erp_bc.inventory`

#### `find_items` (sensitivity: `read`)

Searches items by partial description via
`GET Items?$filter=contains(Description,'<text>')&$top=<n>&$orderby=Description`.

**Args:** `descriptionContains` (string), `top` (int, 1–50, default 10).
**Result:** `count`, `items[]` (each: `itemNo`, `description`, `unitPrice`, `inventory`).
**Behaviour:** returns an empty list (not an error) when nothing matches.

#### `get_item` (sensitivity: `read`)

Looks up an item by number via `GET Items?$filter=No eq '<no>'`.

**Args:** `itemNo` (string).
**Result:** `itemNo`, `description`, `baseUnitOfMeasure`, `unitPrice`, `inventory`.
**Error:** `ToolValidationException` when no item matches, or on backend failure.

### Finance — `erp_bc.finance`

#### `list_open_customer_entries` (sensitivity: `read`)

Lists a customer's open ledger entries via
`GET CustomerLedgerEntries?$filter=Customer_No eq '<no>' and Open eq true&$top=<n>&$orderby=Due_Date`.
Each entry's `overdue` flag is computed connector-side (open **and** `Due_Date` in the past).

**Args:** `customerNo` (string), `overdueOnly` (bool, default false), `top` (int, 1–100, default 50).
**Result:** `customerNo`, `count`, `totalRemainingLcy`, `entries[]` (each: `documentType`, `documentNo`, `postingDate`, `dueDate`, `remainingAmountLcy`, `overdue`).
**Behaviour:** returns an empty list (not an error) when the customer has no open entries.

### Purchasing — `erp_bc.purchasing`

The buy-side mirror of Sales: vendors instead of customers, purchase orders instead
of sales orders. Purchase lines carry a **direct unit cost** (what you pay), not a sales price.

#### `find_vendors` (sensitivity: `read`)

Searches vendors by partial name via
`GET Vendors?$filter=contains(Name,'<text>')&$top=<n>&$orderby=Name`.

**Args:** `nameContains` (string), `top` (int, 1–50, default 10).
**Result:** `count`, `vendors[]` (each: `vendorNo`, `name`, `city`, `balanceLcy`, `blocked`).
**Behaviour:** returns an empty list (not an error) when nothing matches.

#### `get_vendor` (sensitivity: `read`)

Looks up a vendor by number via `GET Vendors?$filter=No eq '<no>'`.

**Args:** `vendorNo` (string).
**Result:** `vendorNo`, `name`, `email`, `phoneNo`, `balanceLcy`, `blocked`.
**Error:** `ToolValidationException` when no vendor matches, or on backend failure.

#### `create_purchase_order` (sensitivity: `write`)

Creates a purchase order header via `POST PurchaseOrder` with `Buy_from_Vendor_No`
(and optional `Vendor_Invoice_No`). The order has no lines until you add them.

**Args:** `vendorNo` (string), `vendorInvoiceNo` (string, optional).
**Result:** `orderNo`, `vendorNo`, `vendorName`.

#### `add_purchase_order_line` (sensitivity: `write`)

Adds an item line to an existing order via
`POST PurchaseOrder(Document_Type='Order',No='<orderNo>')/PurchaseOrderPurchLines`
with `Type='Item'`, `No`, `Quantity` (and optional `Location_Code`).

**Args:** `orderNo` (string), `itemNo` (string), `quantity` (decimal > 0), `locationCode` (string, optional).
**Result:** `orderNo`, `lineNo`, `itemNo`, `description`, `quantity`, `directUnitCost`, `lineAmount`.
**Error:** `ToolValidationException` on blank args, non-positive quantity, or backend failure
(e.g. unknown order or item).

#### `get_purchase_order` (sensitivity: `read`)

Looks up an order header by number via `GET PurchaseOrder?$filter=No eq '<orderNo>'`.

**Args:** `orderNo` (string).
**Result:** `orderNo`, `vendorNo`, `vendorName`, `status`, `orderDate`, `vendorInvoiceNo`, `currencyCode`, `amountIncludingVat`.
**Error:** `ToolValidationException` when no order matches, or on backend failure.

> Field names (`Sell_to_Customer_No`, `Buy_from_Vendor_No`, `Direct_Unit_Cost`,
> `Balance_LCY`, `Remaining_Amt_LCY`, …) follow BC's OData convention of collapsing
> runs of spaces and punctuation in a field caption to a single underscore. They match
> the **standard** Customer / Vendor / Item / Sales Order / Purchase Order / Customer
> Ledger Entries pages; verify them against your published web services if you have
> customised those pages.

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
  -e BC_BASE_URL=http://bc-host:7048/BC/ODataV4 \
  -e BC_COMPANY='CRONUS International Ltd.' \
  -e BC_USERNAME=<bc-user> \
  -e BC_PASSWORD=<web-service-access-key> \
  bc-connector:local
```

> The container must be able to reach the on-prem BC server. If BC uses a
> self-signed TLS certificate, either trust it in the image or terminate TLS at a
> reverse proxy.

---

## Adapting / extending

1. **Add more tools:** follow the `[Tool]` + `ToolHandler<,>` pattern and call
   `BcClient.QueryAsync` / `FindOneAsync` / `CreateAsync`. The assembly scanner in
   `ConnectorHost.ScanAssembly` picks them up automatically.
2. **Add agents:** declare another `[Agent]`; remember tool keys must start with
   `<agentKey>.`.
3. **Use the standard API instead of OData pages:** swap the URL builder in
   `BcClient` to `…/api/v2.0/companies(<id>)/<entitySet>` and resolve the company
   GUID at startup. The basic-auth header is identical.
4. **Update `.env.example`** with any new variables.

---

## See also

- [connector-sdk-dotnet quickstart](https://github.com/vestedai/connector-sdk-dotnet/blob/main/docs/quickstart.md) — end-to-end quickstart for the .NET SDK
- [connector-sdk-dotnet API reference](https://github.com/vestedai/connector-sdk-dotnet/blob/main/docs/api.md) — full `[Agent]`, `[Tool]`, `ConnectorHost` API reference
- [connector-sdk-dotnet concepts](https://github.com/vestedai/connector-sdk-dotnet/blob/main/docs/concepts.md) — agents, tools, sensitivity, and the wire protocol
