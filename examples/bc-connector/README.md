# Business Central Connector — .NET example

A runnable Vested AI connector for **on-prem Dynamics 365 Business Central**,
demonstrating the core `VestedAI.ConnectorSdk` attribute API against a real ERP
backend through a **single custom API endpoint** (the *ASG AI Gateway*) with
NavUserPassword (basic auth).

The connector ships four agents and thirteen tools. Instead of one published page
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
| `erp_bc.inventory.create_transfer_order` | `write` | Create a transfer order header (from/to/in-transit locations); returns the generated number |
| `erp_bc.inventory.add_transfer_order_line` | `write` | Add an item line (item No. + quantity) to an existing transfer order |
| `erp_bc.inventory.get_transfer_order` | `read` | Look up a transfer order by No.; returns header (locations, status, dates) and all lines |
| `erp_bc.inventory.find_transfer_orders` | `read` | List transfer orders, optionally filtered by source/destination location |
| `erp_bc.inventory.post_transfer_order` | `write` | Post a transfer order's shipment and/or receipt (Ship \| Receive \| ShipAndReceive) |

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
| `BC_COMPANY` | yes | **Default** company name exactly as in BC (e.g. `ASG`). Used when a tool call omits `company`. |
| `BC_USERNAME` | yes | BC user name |
| `BC_PASSWORD` | yes | The user's **Web Service Access Key** (recommended) or password |
| `BC_TIMEOUT_SECONDS` | no | Per-request HTTP timeout in seconds (default `30`) |

`BcClient.Configure()` (called from `Program.cs`) validates the `BC_*` variables
at startup and throws with a clear message if any are missing — the process exits
before connecting to the hub.

### Companies (multi-company)

The connector serves multiple Business Central companies, one per country. Every
tool accepts an optional **`company`** argument (the exact BC company name); when
omitted, the default from `BC_COMPANY` (normally `ASG`) is used. The agents map a
country mentioned by the user to the matching company:

| `company` value | Country |
|---|---|
| `ASG` | Saudi Arabia (السعودية) — **default** |
| `ASG - KWT` | Kuwait (الكويت) |
| `ASG - OM` | Oman (عُمان) |
| `ASG - QAR` | Qatar (قطر) |
| `ASG - UAE` | UAE (الإمارات) |

`company` is a routing field: `BcClient` strips it from the args, uses it to select
the company in the URL, and never forwards it in the operation payload. The gateway
record id is resolved and cached **per company**. Each company must have the ASG
Customization app installed (its install/upgrade codeunit seeds the gateway record).

### How requests are formed

For each call, `BcClient` resolves the gateway record id for the target company
(cached per company for the process):

```
GET {BC_BASE_URL}/Company('{company}')/aiGateway?$top=1   → gateway record id
```

Every tool then `POST`s its operation to the gateway's bound action:

```
POST {BC_BASE_URL}/Company('{company}')/aiGateway({gatewayId})/NAV.executeOperation
Content-Type: application/json

{ "operation": "GetCustomer", "payload": "{\"customerNo\":\"C00010\"}" }
```

`payload` is the tool's `args` serialized (camelCase) as a JSON **string**. BC
returns the envelope as a string in OData's `value`:

```
{ "value": "{\"success\":true,\"data\":{ ... }}" }
```

`BcClient` parses `value`, checks `success`, throws `ToolValidationException` with
`error` on failure, and otherwise deserializes `data` into the tool's `Result`.
The `Authorization: Basic` header carries `base64(BC_USERNAME:BC_PASSWORD)`.

---

## Project layout

```
bc-connector/
├── BcConnector.csproj    # Exe project; references the SDK via ProjectReference
├── Program.cs            # BcClient.Configure() → ConnectorHost → RunFromEnvironmentAsync
├── Agents.cs             # [Agent] + [Instruction] declarations (4 agents)
├── Tools.cs              # Eighteen [Tool] ToolHandler<,> implementations (each dispatches one operation)
├── BcClient.cs           # Shared ASG AI Gateway client over ODataV4 (basic auth; per-company gateway-id resolution)
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

Searches customers by partial name. Operation `FindCustomers` (case-insensitive
`contains`, capped at `top`, ordered by name — applied in AL).

**Args:** `nameContains` (string, optional — omit to browse all), `top` (int, 1–50, default 10).
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
`contains`, capped at `top`, ordered by description — applied in AL).

**Args:** `descriptionContains` (string, optional — omit to browse all), `top` (int, 1–50, default 10).
**Result:** `count`, `items[]` (each: `itemNo`, `description`, `unitPrice`, `inventory`).
**Behaviour:** when `descriptionContains` is blank the filter is skipped and the first
page of items (by description) is returned; returns an empty list (not an error) when
nothing matches.

#### `get_item` (sensitivity: `read`)

Looks up an item by number. Operation `GetItem`.

**Args:** `itemNo` (string).
**Result:** `itemNo`, `description`, `baseUnitOfMeasure`, `unitPrice`, `inventory`.
**Error:** `ToolValidationException` when no item matches, or on backend failure.

### Finance — `erp_bc.finance`

#### `list_open_customer_entries` (sensitivity: `read`)

Lists a customer's open ledger entries. Operation `ListOpenCustomerEntries`
(filters open entries for the customer, capped at `top`). Each entry's `overdue`
flag is computed in AL against the BC work/system date (open **and** due date in
the past); `overdueOnly` filters to those.

**Args:** `customerNo` (string), `overdueOnly` (bool, default false), `top` (int, 1–100, default 50).
**Result:** `customerNo`, `count`, `totalRemainingLcy`, `entries[]` (each: `documentType`, `documentNo`, `postingDate`, `dueDate`, `remainingAmountLcy`, `overdue`).
**Behaviour:** returns an empty list (not an error) when the customer has no open entries.

### Purchasing — `erp_bc.purchasing`

The buy-side mirror of Sales: vendors instead of customers, purchase orders instead
of sales orders. Purchase lines carry a **direct unit cost** (what you pay), not a sales price.

#### `find_vendors` (sensitivity: `read`)

Searches vendors by partial name. Operation `FindVendors` (case-insensitive
`contains`, capped at `top`, ordered by name — applied in AL).

**Args:** `nameContains` (string, optional — omit to browse all), `top` (int, 1–50, default 10).
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
(applies `Transfer-from Code` / `Transfer-to Code` ranges, capped at `top` — in AL).

**Args:** `transferFromCode` (string, optional), `transferToCode` (string, optional), `top` (int, 1–50, default 10).
**Result:** `count`, `transferOrders[]` (each: `transferOrderNo`, `transferFromCode`, `transferToCode`, `inTransitCode`, `status`, `postingDate`, `shipmentDate`, `receiptDate`, `externalDocumentNo`).
**Behaviour:** when both location filters are blank, the first page of orders is returned; empty list (not an error) when nothing matches.

#### `post_transfer_order` (sensitivity: `write`)

Posts a transfer order's shipment and/or receipt. Operation `PostTransferOrder`
(releases the order via `Release Transfer Document` when shipping an Open order, then
runs `TransferOrder-Post Shipment` / `TransferOrder-Post Receipt` in AL).

**Args:** `transferOrderNo` (string), `postType` (string: `Ship` | `Receive` | `ShipAndReceive`, default `ShipAndReceive`).
**Result:** `transferOrderNo`, `postType`, `shipped` (bool), `received` (bool), `completed` (bool — true when the fully-posted order was removed), `status` (present only when the order still exists).
**Error:** `ToolValidationException` on blank `transferOrderNo`, invalid `postType`, or a posting failure (e.g. insufficient inventory, closed posting period).

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
