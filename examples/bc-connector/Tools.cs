using System.ComponentModel;
using VestedAI.ConnectorSdk.Errors;
using VestedAI.ConnectorSdk.Tool;

namespace BcConnector;

// ---------------------------------------------------------------------------
// Each tool validates its inputs, then dispatches a single named operation to
// the ASG AI Gateway custom API (BcClient.ExecuteAsync). The gateway owns the
// field names and business rules in AL; the tool simply deserializes the
// returned "data" object into its strongly-typed Result. The Args/Result POCOs
// (with [Description]) still define the JSON schema shown to the model — only
// the wire call changed from per-entity OData to the single gateway endpoint.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// bc.sales.get_customer
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up a Business Central customer by customer number and returns the
/// key fields a sales rep cares about.
/// </summary>
[Tool(
    Key         = "erp_bc.sales.get_customer",
    Description = "Look up a Business Central customer by their customer number (No.). Returns name, contact details, balance, credit limit, and whether the account is blocked.",
    Sensitivity = "read")]
public class GetCustomer : ToolHandler<GetCustomer.Args, GetCustomer.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Business Central customer number, e.g. C00010 or 10000.")]
        public string CustomerNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Customer number (No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string Name { get; set; } = "";

        [Description("Primary email address on the customer card.")]
        public string Email { get; set; } = "";

        [Description("Primary phone number on the customer card.")]
        public string PhoneNo { get; set; } = "";

        [Description("Outstanding balance in the company's local currency (LCY).")]
        public decimal BalanceLcy { get; set; }

        [Description("Credit limit in the company's local currency (LCY). 0 means no limit set.")]
        public decimal CreditLimitLcy { get; set; }

        [Description("Whether the customer is blocked (empty means active; otherwise the block type such as Ship, Invoice, or All).")]
        public string Blocked { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.CustomerNo))
            throw new ToolValidationException(
                "erp_bc.sales.get_customer", "customerNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetCustomer", args, "erp_bc.sales.get_customer", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.sales.lookup_customer
// ---------------------------------------------------------------------------

/// <summary>
/// Resolves a customer by number or name in a single gateway call — faster than
/// separate <see cref="FindCustomers"/> + <see cref="GetCustomer"/> round-trips.
/// </summary>
[Tool(
    Key         = "erp_bc.sales.lookup_customer",
    Description = BcToolDescriptions.LookupCustomer,
    Sensitivity = "read")]
public class LookupCustomer : ToolHandler<LookupCustomer.Args, LookupCustomer.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Customer number when known, e.g. C00010. Provide this OR nameContains.")]
        public string CustomerNo { get; set; } = "";

        [Description("Partial customer name to search when the number is unknown. Provide this OR customerNo.")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Candidate
    {
        [Description("Customer number (No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string Name { get; set; } = "";

        [Description("City from the customer's address.")]
        public string City { get; set; } = "";

        [Description("Whether the customer is blocked (empty means active).")]
        public string Blocked { get; set; } = "";
    }

    public class Result
    {
        [Description("True when a single customer was identified and full details are in this object.")]
        public bool Resolved { get; set; }

        [Description("Number of candidates when resolved is false.")]
        public int Count { get; set; }

        [Description("Customer number (No.) when resolved.")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name when resolved.")]
        public string Name { get; set; } = "";

        [Description("Primary email when resolved.")]
        public string Email { get; set; } = "";

        [Description("Primary phone when resolved.")]
        public string PhoneNo { get; set; } = "";

        [Description("Outstanding balance in LCY when resolved.")]
        public decimal BalanceLcy { get; set; }

        [Description("Credit limit in LCY when resolved.")]
        public decimal CreditLimitLcy { get; set; }

        [Description("Blocked status when resolved.")]
        public string Blocked { get; set; } = "";

        [Description("Short candidate list when resolved is false. Ask the user to pick one.")]
        public List<Candidate> Customers { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.CustomerNo) && string.IsNullOrWhiteSpace(args.NameContains))
            throw new ToolValidationException(
                "erp_bc.sales.lookup_customer", "customerNo or nameContains is required.");

        return await BcClient.ExecuteAsync<Result>(
            "ResolveCustomer", args, "erp_bc.sales.lookup_customer", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.get_item
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up a Business Central item by item number and returns price and
/// on-hand inventory.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.get_item",
    Description = "Look up a Business Central item by its item number (No.). Returns description, base unit of measure, unit price, and quantity on hand (Inventory).",
    Sensitivity = "read")]
public class GetItem : ToolHandler<GetItem.Args, GetItem.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Business Central item number, e.g. 1000 or ITEM-001.")]
        public string ItemNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Item number (No.).")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Base unit of measure, e.g. PCS or BOX.")]
        public string BaseUnitOfMeasure { get; set; } = "";

        [Description("Unit price in the company's local currency.")]
        public decimal UnitPrice { get; set; }

        [Description("Quantity currently on hand across all locations.")]
        public decimal Inventory { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.ItemNo))
            throw new ToolValidationException(
                "erp_bc.inventory.get_item", "itemNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetItem", args, "erp_bc.inventory.get_item", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.lookup_item
// ---------------------------------------------------------------------------

/// <summary>
/// Resolves an item by number or description in a single gateway call.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.lookup_item",
    Description = BcToolDescriptions.LookupItem,
    Sensitivity = "read")]
public class LookupItem : ToolHandler<LookupItem.Args, LookupItem.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Item number when known. Provide this OR descriptionContains.")]
        public string ItemNo { get; set; } = "";

        [Description("Partial description when the number is unknown. Provide this OR itemNo.")]
        public string DescriptionContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Candidate
    {
        [Description("Item number (No.).")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Unit price in LCY.")]
        public decimal UnitPrice { get; set; }
    }

    public class Result
    {
        [Description("True when a single item was identified and full details are in this object.")]
        public bool Resolved { get; set; }

        [Description("Number of candidates when resolved is false.")]
        public int Count { get; set; }

        [Description("Item number when resolved.")]
        public string ItemNo { get; set; } = "";

        [Description("Description when resolved.")]
        public string Description { get; set; } = "";

        [Description("Base unit of measure when resolved.")]
        public string BaseUnitOfMeasure { get; set; } = "";

        [Description("Unit price when resolved.")]
        public decimal UnitPrice { get; set; }

        [Description("On-hand inventory when resolved.")]
        public decimal Inventory { get; set; }

        [Description("Short candidate list when resolved is false.")]
        public List<Candidate> Items { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.ItemNo) && string.IsNullOrWhiteSpace(args.DescriptionContains))
            throw new ToolValidationException(
                "erp_bc.inventory.lookup_item", "itemNo or descriptionContains is required.");

        return await BcClient.ExecuteAsync<Result>(
            "ResolveItem", args, "erp_bc.inventory.lookup_item", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.sales.create_sales_order
// ---------------------------------------------------------------------------

/// <summary>
/// Creates a new sales order header for a customer in Business Central.
/// </summary>
[Tool(
    Key         = "erp_bc.sales.create_sales_order",
    Description = "Create a new sales order header in Business Central for a given customer. Returns the generated order number. Lines are added separately.",
    Sensitivity = "write")]
public class CreateSalesOrder : ToolHandler<CreateSalesOrder.Args, CreateSalesOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Customer number (No.) the order is sold to, e.g. C00010.")]
        public string CustomerNo { get; set; } = "";

        [Description("Optional external document number (e.g. the customer's PO number).")]
        public string ExternalDocumentNo { get; set; } = "";
    }

    public class Result
    {
        [Description("The sales order number generated by Business Central.")]
        public string OrderNo { get; set; } = "";

        [Description("Customer number the order was created for.")]
        public string CustomerNo { get; set; } = "";

        [Description("Name of the customer the order was created for.")]
        public string CustomerName { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.CustomerNo))
            throw new ToolValidationException(
                "erp_bc.sales.create_sales_order", "customerNo is required.");

        BcCallerContext.Require(ctx, "erp_bc.sales.create_sales_order");
        return await BcClient.ExecuteAsync<Result>(
            "CreateSalesOrder", args, "erp_bc.sales.create_sales_order", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.sales.find_customers
// ---------------------------------------------------------------------------

/// <summary>
/// Searches customers by partial name. The companion to <see cref="GetCustomer"/>
/// for when the caller has a name but not a customer number.
/// </summary>
[Tool(
    Key         = "erp_bc.sales.find_customers",
    Description = "Search customers by partial name when you need a list of matches. Prefer lookup_customer for single-customer questions (one round-trip). Omit nameContains to browse the first page.",
    Sensitivity = "read")]
public class FindCustomers : ToolHandler<FindCustomers.Args, FindCustomers.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the customer name (case-insensitive substring). Leave empty to list customers without filtering.")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }

        [Description(BcToolDescriptions.IncludeBalance)]
        public bool IncludeBalance { get; set; }
    }

    public class Match
    {
        [Description("Customer number (No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string Name { get; set; } = "";

        [Description("City from the customer's address.")]
        public string City { get; set; } = "";

        [Description("Outstanding balance in LCY. Only set when includeBalance was true.")]
        public decimal BalanceLcy { get; set; }

        [Description("Balance due in LCY. Only set when includeBalance was true.")]
        public decimal BalanceDueLcy { get; set; }

        [Description("Whether the customer is blocked (empty means active).")]
        public string Blocked { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of customers returned.")]
        public int Count { get; set; }

        [Description("Matching customers, ordered by name. Empty when nothing matched.")]
        public List<Match> Customers { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        return await BcClient.ExecuteAsync<Result>(
            "FindCustomers", args, "erp_bc.sales.find_customers", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.sales.get_sales_order
// ---------------------------------------------------------------------------

/// <summary>
/// Reads a sales order header by order number so the caller can check its
/// status and total without re-creating it.
/// </summary>
[Tool(
    Key         = "erp_bc.sales.get_sales_order",
    Description = "Look up an existing Business Central sales order by its order number (No.). Returns the customer, status, order date, currency, and total amount including VAT.",
    Sensitivity = "read")]
public class GetSalesOrder : ToolHandler<GetSalesOrder.Args, GetSalesOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Sales order number (No.), e.g. S-ORD-101001.")]
        public string OrderNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Sales order number (No.).")]
        public string OrderNo { get; set; } = "";

        [Description("Customer number the order is sold to.")]
        public string CustomerNo { get; set; } = "";

        [Description("Name of the customer the order is sold to.")]
        public string CustomerName { get; set; } = "";

        [Description("Document status: Open, Released, Pending Approval, or Pending Prepayment.")]
        public string Status { get; set; } = "";

        [Description("Order date (yyyy-MM-dd); empty if not set.")]
        public string OrderDate { get; set; } = "";

        [Description("Customer's external document number (e.g. their PO number).")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("Order currency code; empty means the company's local currency (LCY).")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total order amount including VAT, in the order's currency.")]
        public decimal AmountIncludingVat { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OrderNo))
            throw new ToolValidationException(
                "erp_bc.sales.get_sales_order", "orderNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetSalesOrder", args, "erp_bc.sales.get_sales_order", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.sales.add_sales_order_line
// ---------------------------------------------------------------------------

/// <summary>
/// Adds an item line to an existing sales order. Completes the order workflow
/// started by <see cref="CreateSalesOrder"/> (which creates only the header).
/// </summary>
[Tool(
    Key         = "erp_bc.sales.add_sales_order_line",
    Description = "Add an item line to an existing Business Central sales order. Provide the order number, item number, and quantity. Create the order header first with create_sales_order. Returns the created line.",
    Sensitivity = "write")]
public class AddSalesOrderLine : ToolHandler<AddSalesOrderLine.Args, AddSalesOrderLine.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Sales order number (No.) to add the line to, e.g. S-ORD-101001.")]
        public string OrderNo { get; set; } = "";

        [Description("Item number (No.) to sell on this line.")]
        public string ItemNo { get; set; } = "";

        [Description("Quantity to order. Must be greater than zero.")]
        public decimal Quantity { get; set; }

        [Description("Optional location code to ship from (e.g. BLUE, MAIN). Leave empty for the order default.")]
        public string LocationCode { get; set; } = "";
    }

    public class Result
    {
        [Description("Order number the line belongs to.")]
        public string OrderNo { get; set; } = "";

        [Description("Line number assigned by Business Central.")]
        public int LineNo { get; set; }

        [Description("Item number on the line.")]
        public string ItemNo { get; set; } = "";

        [Description("Item description copied from the item card.")]
        public string Description { get; set; } = "";

        [Description("Quantity ordered.")]
        public decimal Quantity { get; set; }

        [Description("Unit price applied to the line, in the order's currency.")]
        public decimal UnitPrice { get; set; }

        [Description("Total line amount (quantity × unit price, less any line discount), excluding VAT.")]
        public decimal LineAmount { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OrderNo))
            throw new ToolValidationException(
                "erp_bc.sales.add_sales_order_line", "orderNo is required.");
        if (string.IsNullOrWhiteSpace(args.ItemNo))
            throw new ToolValidationException(
                "erp_bc.sales.add_sales_order_line", "itemNo is required.");
        if (args.Quantity <= 0)
            throw new ToolValidationException(
                "erp_bc.sales.add_sales_order_line", "quantity must be greater than zero.");

        BcCallerContext.Require(ctx, "erp_bc.sales.add_sales_order_line");
        return await BcClient.ExecuteAsync<Result>(
            "AddSalesOrderLine", args, "erp_bc.sales.add_sales_order_line", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.sales.post_sales_order
// ---------------------------------------------------------------------------

/// <summary>
/// Posts a sales order (ship and/or invoice). Releases the order first when Open.
/// </summary>
[Tool(
    Key         = "erp_bc.sales.post_sales_order",
    Description = "Post a Business Central sales order. postType 'Ship' creates a shipment, 'Invoice' posts the invoice, and 'ShipAndInvoice' (default) does both. Releases the order first when it is still Open.",
    Sensitivity = "write")]
public class PostSalesOrder : ToolHandler<PostSalesOrder.Args, PostSalesOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Sales order number (No.) to post.")]
        public string OrderNo { get; set; } = "";

        [Description("What to post: 'Ship', 'Invoice', or 'ShipAndInvoice'. Defaults to 'ShipAndInvoice'.")]
        public string PostType { get; set; } = "ShipAndInvoice";

        [Description("Optional posting date (yyyy-MM-dd). Uses BC work date when omitted.")]
        public string PostingDate { get; set; } = "";
    }

    public class Result
    {
        [Description("Sales order number that was posted.")]
        public string OrderNo { get; set; } = "";

        [Description("The post type that was applied.")]
        public string PostType { get; set; } = "";

        [Description("Whether a shipment was posted.")]
        public bool Shipped { get; set; }

        [Description("Whether an invoice was posted.")]
        public bool Invoiced { get; set; }

        [Description("True when the order was fully posted and removed.")]
        public bool Completed { get; set; }

        [Description("Order status after posting (present only when the order still exists).")]
        public string Status { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OrderNo))
            throw new ToolValidationException(
                "erp_bc.sales.post_sales_order", "orderNo is required.");

        BcCallerContext.Require(ctx, "erp_bc.sales.post_sales_order");
        return await BcClient.ExecuteAsync<Result>(
            "PostSalesOrder", args, "erp_bc.sales.post_sales_order", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.find_items
// ---------------------------------------------------------------------------

/// <summary>
/// Searches items by partial description. The companion to <see cref="GetItem"/>
/// for when the caller has a product name but not an item number.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.find_items",
    Description = "Search items by partial description when you need a list of matches. Prefer lookup_item for single-item questions (one round-trip). Omit descriptionContains to browse the first page.",
    Sensitivity = "read")]
public class FindItems : ToolHandler<FindItems.Args, FindItems.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the item description (case-insensitive substring). Leave empty to list items without filtering.")]
        public string DescriptionContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }

        [Description(BcToolDescriptions.IncludeInventory)]
        public bool IncludeInventory { get; set; }
    }

    public class Match
    {
        [Description("Item number (No.).")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Unit price in the company's local currency.")]
        public decimal UnitPrice { get; set; }

        [Description("On-hand quantity. Only set when includeInventory was true.")]
        public decimal Inventory { get; set; }
    }

    public class Result
    {
        [Description("Number of items returned.")]
        public int Count { get; set; }

        [Description("Matching items, ordered by description. Empty when nothing matched.")]
        public List<Match> Items { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        return await BcClient.ExecuteAsync<Result>(
            "FindItems", args, "erp_bc.inventory.find_items", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.net_inventory
// ---------------------------------------------------------------------------

/// <summary>
/// Net (available) store inventory from LS Central, reproducing the standard
/// "Inventory Lookup" Net Inventory:
///   phys inventory - unposted sales + posted sales + inventory adjustments
///   - posted inventory adjustments - click&amp;collect reservations.
/// Can be broken down per item × store, or rolled up by item, store, or category.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.net_inventory",
    Description = "Net (available) store inventory per LS Central's Inventory Lookup: physical inventory adjusted for unposted/posted POS sales, inventory adjustments, and click & collect reservations. Use groupBy to choose the breakdown — detail (per item per store), item, store, or category — and scope with itemNo/descriptionContains, itemCategoryCode, storeNo, or locationCode. Row-level breakdowns (detail/item/store) need at least one of those filters.",
    Sensitivity = "read")]
public class GetNetInventory : ToolHandler<GetNetInventory.Args, GetNetInventory.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Item number to scope to, e.g. CAT01-000001. Provide this OR descriptionContains, or scope by category/store instead.")]
        public string ItemNo { get; set; } = "";

        [Description("Partial item description; resolved to one item only when it matches exactly one. Use itemNo when known.")]
        public string DescriptionContains { get; set; } = "";

        [Description("Optional. Restrict to items in this item category code.")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("Optional. Restrict to a single LS Central store number.")]
        public string StoreNo { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. Restrict to a single location code (covers the store(s) on that location).")]
        public string LocationCode { get; set; } = "";

        [Description("Optional. Restrict to a single item variant code (implies a variant-level breakdown).")]
        public string VariantCode { get; set; } = "";

        [Description("When true, break results down per item variant. Defaults to false (item level).")]
        public bool ByVariant { get; set; }

        [Description("Breakdown: detail (per item per store, the default), item, store, category, or total.")]
        public string GroupBy { get; set; } = "";

        [Description("When true, include rows with no inventory activity. Defaults to false.")]
        public bool IncludeZero { get; set; }

        [Description("When true (default), include the six component totals on each row alongside netInventory.")]
        public bool IncludeComponents { get; set; } = true;

        [Description("Computation path: auto (default), iterate (row-by-row), or aggregate (SQL GROUP BY). Use aggregate for large category/store rollups.")]
        public string Mode { get; set; } = "";

        [Description("Optional. Maximum rows to return; defaults to 1000.")]
        public int? Top { get; set; }
    }

    public class Row
    {
        [Description("Item number (detail/item groupings).")]
        public string ItemNo { get; set; } = "";

        [Description("Variant code, when byVariant is set.")]
        public string VariantCode { get; set; } = "";

        [Description("Item description (detail/item groupings).")]
        public string Description { get; set; } = "";

        [Description("Item category code (detail/item groupings; also the key for the category grouping).")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("Item category description (category grouping).")]
        public string CategoryDescription { get; set; } = "";

        [Description("Store number (detail/store groupings).")]
        public string StoreNo { get; set; } = "";

        [Description("Location code (detail/store groupings).")]
        public string LocationCode { get; set; } = "";

        [Description("Location name (detail/store groupings).")]
        public string LocationName { get; set; } = "";

        [Description("Store name (detail/store groupings).")]
        public string StoreName { get; set; } = "";

        [Description("Physical on-hand inventory (Item Ledger Entry quantity by location).")]
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
    }

    public class Result
    {
        [Description("Grouping applied: detail, item, store, category, or total.")]
        public string GroupBy { get; set; } = "";

        [Description("Whether the breakdown is per item variant.")]
        public bool ByVariant { get; set; }

        [Description("Computation path used internally: iteration or aggregate (same numbers either way).")]
        public string Path { get; set; } = "";

        [Description("Number of rows returned.")]
        public int Count { get; set; }

        [Description("True when more qualifying rows exist beyond the returned page (raise top to see them).")]
        public bool HasMore { get; set; }

        [Description("Sum of netInventory across the qualifying rows.")]
        public decimal TotalNetInventory { get; set; }

        [Description("Result rows; the populated key fields depend on groupBy.")]
        public List<Row> Rows { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        var hasScope =
            !string.IsNullOrWhiteSpace(args.ItemNo) ||
            !string.IsNullOrWhiteSpace(args.DescriptionContains) ||
            !string.IsNullOrWhiteSpace(args.ItemCategoryCode) ||
            !string.IsNullOrWhiteSpace(args.StoreNo) ||
            !string.IsNullOrWhiteSpace(args.LocationCode);

        // Row-level breakdowns can return the whole catalogue × every store, so the
        // gateway requires a narrowing filter for them; category/total may run open.
        var groupBy = (args.GroupBy ?? "").Trim().ToLowerInvariant();
        var rowLevel = groupBy is "" or "detail" or "item" or "store" or "location"
            or "itemstore" or "item-store" or "byitem" or "bystore";
        if (!hasScope && rowLevel)
            throw new ToolValidationException(
                "erp_bc.inventory.net_inventory",
                "Provide itemNo, descriptionContains, itemCategoryCode, storeNo, or locationCode " +
                "(or use groupBy 'category' or 'total').");

        return await BcClient.ExecuteAsync<Result>(
            "GetNetInventory", args, "erp_bc.inventory.net_inventory", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.find_stores
// ---------------------------------------------------------------------------

/// <summary>
/// Lists LS Central stores with their names and locations — for resolving a store
/// name to its number, or enumerating the stores behind net_inventory.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.find_stores",
    Description = "List LS Central retail stores with their name, location code, and location name. Filter by storeNo, partial name (nameContains), or locationCode; call with no filters to browse the full list. Use this to resolve a store name to its store number before net_inventory or other store-scoped lookups. Stores are an LS Central concept (a retail outlet); for plain Business Central warehouses use find_locations instead.",
    Sensitivity = "read")]
public class FindStores : ToolHandler<FindStores.Args, FindStores.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Exact store number to return (e.g. S004). When set, other filters are ignored.")]
        public string StoreNo { get; set; } = "";

        [Description("Optional. Partial store name to search (case-insensitive substring).")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Restrict to stores posting to this location code.")]
        public string LocationCode { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Store
    {
        [Description("Store number (No.).")]
        public string StoreNo { get; set; } = "";

        [Description("Store name.")]
        public string Name { get; set; } = "";

        [Description("Location code the store posts inventory to.")]
        public string LocationCode { get; set; } = "";

        [Description("Location name for the store's location code.")]
        public string LocationName { get; set; } = "";

        [Description("City from the store address.")]
        public string City { get; set; } = "";

        [Description("Store currency code; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of stores returned.")]
        public int Count { get; set; }

        [Description("Matching stores.")]
        public List<Store> Stores { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        return await BcClient.ExecuteAsync<Result>(
            "FindStores", args, "erp_bc.inventory.find_stores", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.find_locations
// ---------------------------------------------------------------------------

/// <summary>
/// Lists Business Central warehouse locations with their names — for resolving a
/// location code to its name or enumerating available locations.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.find_locations",
    Description = "List Business Central warehouse locations with their code and name. Filter by exact code or partial name (nameContains); call with no filters to browse the full list. Use this to resolve a location code to its name, or to enumerate available warehouses. Locations are the standard Business Central warehouse concept; for LS Central retail outlets use find_stores instead.",
    Sensitivity = "read")]
public class FindLocations : ToolHandler<FindLocations.Args, FindLocations.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Exact location code to return, e.g. MAIN. When set, nameContains is ignored.")]
        public string Code { get; set; } = "";

        [Description("Optional. Partial location name to search (case-insensitive substring).")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Location
    {
        [Description("Location code.")]
        public string Code { get; set; } = "";

        [Description("Location name.")]
        public string Name { get; set; } = "";

        [Description("True when the location is used as an in-transit location for transfers.")]
        public bool UseAsInTransit { get; set; }
    }

    public class Result
    {
        [Description("Number of locations returned.")]
        public int Count { get; set; }

        [Description("Matching locations.")]
        public List<Location> Locations { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        return await BcClient.ExecuteAsync<Result>(
            "FindLocations", args, "erp_bc.inventory.find_locations", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.list_open_customer_entries
// ---------------------------------------------------------------------------

/// <summary>
/// Lists a customer's open (unpaid) ledger entries from Business Central and
/// flags which are overdue — the core of an accounts-receivable look-up.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.list_open_customer_entries",
    Description = "List open (unpaid) customer ledger entries for a customer (line detail). For the customer's total balance on the card, use get_customer or lookup_customer — entry totals may be 0 when country is wrong or department scoping hides rows. Pass country (e.g. AE for UAE customers). Returns customerBalanceLcy from the card when entries are empty or scoped.",
    Sensitivity = "read")]
public class ListOpenCustomerEntries
    : ToolHandler<ListOpenCustomerEntries.Args, ListOpenCustomerEntries.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Customer number (No.) whose open ledger entries to list, e.g. C00010.")]
        public string CustomerNo { get; set; } = "";

        [Description("When true, return only entries already past their due date. Defaults to false (all open entries).")]
        public bool OverdueOnly { get; set; }

        [Description("Optional. Filter to entries whose document number contains this text (case-insensitive).")]
        public string DocumentNoContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Entry
    {
        [Description("Ledger entry number.")]
        public int EntryNo { get; set; }

        [Description("Document type, e.g. Invoice, Credit Memo, or Payment.")]
        public string DocumentType { get; set; } = "";

        [Description("Document number of the ledger entry.")]
        public string DocumentNo { get; set; } = "";

        [Description("External document number, if any.")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Due date (yyyy-MM-dd); empty if not set.")]
        public string DueDate { get; set; } = "";

        [Description("Currency code of the entry; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Original amount in the company's local currency (LCY).")]
        public decimal OriginalAmountLcy { get; set; }

        [Description("Remaining (unpaid) amount in the company's local currency (LCY).")]
        public decimal RemainingAmountLcy { get; set; }

        [Description("True when the entry is open and its due date is in the past.")]
        public bool Overdue { get; set; }

        [Description("Days past the due date; 0 when not overdue.")]
        public int DaysOverdue { get; set; }
    }

    public class Result
    {
        [Description("Customer number the entries belong to.")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string CustomerName { get; set; } = "";

        [Description("Customer card Balance (LCY) — total across all open entries, not department-scoped.")]
        public decimal CustomerBalanceLcy { get; set; }

        [Description("Customer card Balance Due (LCY).")]
        public decimal CustomerBalanceDueLcy { get; set; }

        [Description("True when results are filtered to the caller's department(s); entry totals may be lower than customerBalanceLcy.")]
        public bool DepartmentScoped { get; set; }

        [Description("Number of entries returned.")]
        public int Count { get; set; }

        [Description("Total remaining amount across the returned entries, in LCY (may be 0 when department-scoped or wrong country).")]
        public decimal TotalRemainingLcy { get; set; }

        [Description("Total remaining amount on overdue entries only, in LCY.")]
        public decimal TotalOverdueLcy { get; set; }

        [Description("The open ledger entries.")]
        public List<Entry> Entries { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.CustomerNo))
            throw new ToolValidationException(
                "erp_bc.finance.list_open_customer_entries", "customerNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "ListOpenCustomerEntries", args, "erp_bc.finance.list_open_customer_entries", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.find_customers
// ---------------------------------------------------------------------------

/// <summary>
/// Finance-scoped customer search — dispatches the same gateway operation as
/// <see cref="FindCustomers"/> so the finance agent can resolve names to numbers.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.find_customers",
    Description = "Search customers by partial name when you need a list before AR queries. Prefer lookup_customer for single-customer questions (one round-trip).",
    Sensitivity = "read")]
public class FinanceFindCustomers : ToolHandler<FinanceFindCustomers.Args, FinanceFindCustomers.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the customer name (case-insensitive substring). Leave empty to list customers without filtering.")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }

        [Description(BcToolDescriptions.IncludeBalance)]
        public bool IncludeBalance { get; set; }
    }

    public class Match
    {
        [Description("Customer number (No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string Name { get; set; } = "";

        [Description("City from the customer's address.")]
        public string City { get; set; } = "";

        [Description("Outstanding balance in LCY. Only set when includeBalance was true.")]
        public decimal BalanceLcy { get; set; }

        [Description("Balance due in LCY. Only set when includeBalance was true.")]
        public decimal BalanceDueLcy { get; set; }

        [Description("Whether the customer is blocked (empty means active).")]
        public string Blocked { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of customers returned.")]
        public int Count { get; set; }

        [Description("Matching customers, ordered by name. Empty when nothing matched.")]
        public List<Match> Customers { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>("FindCustomers", args, "erp_bc.finance.find_customers", ctx);
}

// ---------------------------------------------------------------------------
// bc.finance.get_customer
// ---------------------------------------------------------------------------

/// <summary>
/// Finance-scoped customer look-up — balance and credit limit for AR checks.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.get_customer",
    Description = "Look up a Business Central customer by number (No.). Returns name, contact details, balance, credit limit, and blocked status. Use before listing ledger entries or aging.",
    Sensitivity = "read")]
public class FinanceGetCustomer : ToolHandler<FinanceGetCustomer.Args, FinanceGetCustomer.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Business Central customer number, e.g. C00010.")]
        public string CustomerNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Customer number (No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string Name { get; set; } = "";

        [Description("Primary email address on the customer card.")]
        public string Email { get; set; } = "";

        [Description("Primary phone number on the customer card.")]
        public string PhoneNo { get; set; } = "";

        [Description("Outstanding balance in the company's local currency (LCY).")]
        public decimal BalanceLcy { get; set; }

        [Description("Credit limit in the company's local currency (LCY). 0 means no limit set.")]
        public decimal CreditLimitLcy { get; set; }

        [Description("Whether the customer is blocked (empty means active; otherwise the block type such as Ship, Invoice, or All).")]
        public string Blocked { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.CustomerNo))
            throw new ToolValidationException("erp_bc.finance.get_customer", "customerNo is required.");

        return await BcClient.ExecuteAsync<Result>("GetCustomer", args, "erp_bc.finance.get_customer", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.lookup_customer
// ---------------------------------------------------------------------------

/// <summary>
/// Finance-scoped customer resolve — one round-trip for name-or-number lookups.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.lookup_customer",
    Description = BcToolDescriptions.LookupCustomer,
    Sensitivity = "read")]
public class FinanceLookupCustomer : ToolHandler<FinanceLookupCustomer.Args, FinanceLookupCustomer.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Customer number when known. Provide this OR nameContains.")]
        public string CustomerNo { get; set; } = "";

        [Description("Partial customer name when the number is unknown. Provide this OR customerNo.")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Candidate
    {
        [Description("Customer number (No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string Name { get; set; } = "";

        [Description("City from the customer's address.")]
        public string City { get; set; } = "";

        [Description("Whether the customer is blocked (empty means active).")]
        public string Blocked { get; set; } = "";
    }

    public class Result
    {
        [Description("True when a single customer was identified and full details are in this object.")]
        public bool Resolved { get; set; }

        [Description("Number of candidates when resolved is false.")]
        public int Count { get; set; }

        [Description("Customer number (No.) when resolved.")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name when resolved.")]
        public string Name { get; set; } = "";

        [Description("Primary email when resolved.")]
        public string Email { get; set; } = "";

        [Description("Primary phone when resolved.")]
        public string PhoneNo { get; set; } = "";

        [Description("Outstanding balance in LCY when resolved.")]
        public decimal BalanceLcy { get; set; }

        [Description("Credit limit in LCY when resolved.")]
        public decimal CreditLimitLcy { get; set; }

        [Description("Blocked status when resolved.")]
        public string Blocked { get; set; } = "";

        [Description("Short candidate list when resolved is false.")]
        public List<Candidate> Customers { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.CustomerNo) && string.IsNullOrWhiteSpace(args.NameContains))
            throw new ToolValidationException(
                "erp_bc.finance.lookup_customer", "customerNo or nameContains is required.");

        return await BcClient.ExecuteAsync<Result>(
            "ResolveCustomer", args, "erp_bc.finance.lookup_customer", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.list_customers_with_overdue
// ---------------------------------------------------------------------------

/// <summary>
/// Cross-customer report of accounts with overdue open receivables.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.list_customers_with_overdue",
    Description = "List customers who have overdue open ledger entries. Returns each customer's number, name, total overdue amount (LCY), and oldest due date. Use for collections and AR dashboards.",
    Sensitivity = "read")]
public class ListCustomersWithOverdue
    : ToolHandler<ListCustomersWithOverdue.Args, ListCustomersWithOverdue.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class CustomerOverdue
    {
        [Description("Customer number (No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string Name { get; set; } = "";

        [Description("Total overdue remaining amount in LCY.")]
        public decimal TotalOverdueLcy { get; set; }

        [Description("Oldest due date among overdue entries (yyyy-MM-dd).")]
        public string OldestDueDate { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of customers returned.")]
        public int Count { get; set; }

        [Description("Customers with overdue open entries, ordered by oldest due date.")]
        public List<CustomerOverdue> Customers { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>(
            "ListCustomersWithOverdue", args, "erp_bc.finance.list_customers_with_overdue", ctx);
}

// ---------------------------------------------------------------------------
// bc.finance.get_customer_aging
// ---------------------------------------------------------------------------

/// <summary>
/// Aging-bucket summary for one customer's open receivables.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.get_customer_aging",
    Description = "Get an aging summary for a customer's open receivables: Current, 1–30, 31–60, 61–90, and 90+ days past due (all in LCY). Use for bucket summaries; use list_open_customer_entries for line detail.",
    Sensitivity = "read")]
public class GetCustomerAging : ToolHandler<GetCustomerAging.Args, GetCustomerAging.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Customer number (No.) to age, e.g. C00010.")]
        public string CustomerNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Customer number (No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string CustomerName { get; set; } = "";

        [Description("Open amount not yet due, in LCY.")]
        public decimal CurrentLcy { get; set; }

        [Description("Open amount 1–30 days past due, in LCY.")]
        public decimal Days1To30Lcy { get; set; }

        [Description("Open amount 31–60 days past due, in LCY.")]
        public decimal Days31To60Lcy { get; set; }

        [Description("Open amount 61–90 days past due, in LCY.")]
        public decimal Days61To90Lcy { get; set; }

        [Description("Open amount more than 90 days past due, in LCY.")]
        public decimal Over90DaysLcy { get; set; }

        [Description("Total open amount across all buckets, in LCY.")]
        public decimal TotalLcy { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.CustomerNo))
            throw new ToolValidationException("erp_bc.finance.get_customer_aging", "customerNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetCustomerAging", args, "erp_bc.finance.get_customer_aging", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.find_vendors
// ---------------------------------------------------------------------------

/// <summary>
/// Finance-scoped vendor search for accounts-payable workflows.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.find_vendors",
    Description = "Search vendors by partial name when you need a list before AP queries. Prefer lookup_vendor for single-vendor questions (one round-trip).",
    Sensitivity = "read")]
public class FinanceFindVendors : ToolHandler<FinanceFindVendors.Args, FinanceFindVendors.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the vendor name (case-insensitive substring). Leave empty to list vendors without filtering.")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }

        [Description(BcToolDescriptions.IncludeBalance)]
        public bool IncludeBalance { get; set; }
    }

    public class Match
    {
        [Description("Vendor number (No.).")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string Name { get; set; } = "";

        [Description("City from the vendor's address.")]
        public string City { get; set; } = "";

        [Description("Balance owed in LCY. Only set when includeBalance was true.")]
        public decimal BalanceLcy { get; set; }

        [Description("Balance due in LCY. Only set when includeBalance was true.")]
        public decimal BalanceDueLcy { get; set; }

        [Description("Whether the vendor is blocked (empty means active).")]
        public string Blocked { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of vendors returned.")]
        public int Count { get; set; }

        [Description("Matching vendors, ordered by name. Empty when nothing matched.")]
        public List<Match> Vendors { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>("FindVendors", args, "erp_bc.finance.find_vendors", ctx);
}

// ---------------------------------------------------------------------------
// bc.finance.get_vendor
// ---------------------------------------------------------------------------

/// <summary>
/// Finance-scoped vendor look-up for accounts-payable balance checks.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.get_vendor",
    Description = "Look up a Business Central vendor by number (No.). Returns name, contact details, balance owed, and blocked status.",
    Sensitivity = "read")]
public class FinanceGetVendor : ToolHandler<FinanceGetVendor.Args, FinanceGetVendor.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Business Central vendor number, e.g. V00010.")]
        public string VendorNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Vendor number (No.).")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string Name { get; set; } = "";

        [Description("Primary email address on the vendor card.")]
        public string Email { get; set; } = "";

        [Description("Primary phone number on the vendor card.")]
        public string PhoneNo { get; set; } = "";

        [Description("Outstanding balance owed to the vendor, in LCY.")]
        public decimal BalanceLcy { get; set; }

        [Description("Whether the vendor is blocked (empty means active; otherwise the block type such as Payment or All).")]
        public string Blocked { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.VendorNo))
            throw new ToolValidationException("erp_bc.finance.get_vendor", "vendorNo is required.");

        return await BcClient.ExecuteAsync<Result>("GetVendor", args, "erp_bc.finance.get_vendor", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.lookup_vendor
// ---------------------------------------------------------------------------

/// <summary>
/// Finance-scoped vendor resolve — one round-trip for name-or-number lookups.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.lookup_vendor",
    Description = BcToolDescriptions.LookupVendor,
    Sensitivity = "read")]
public class FinanceLookupVendor : ToolHandler<FinanceLookupVendor.Args, FinanceLookupVendor.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Vendor number when known. Provide this OR nameContains.")]
        public string VendorNo { get; set; } = "";

        [Description("Partial vendor name when the number is unknown. Provide this OR vendorNo.")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Candidate
    {
        [Description("Vendor number (No.).")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string Name { get; set; } = "";

        [Description("City from the vendor's address.")]
        public string City { get; set; } = "";

        [Description("Whether the vendor is blocked (empty means active).")]
        public string Blocked { get; set; } = "";
    }

    public class Result
    {
        [Description("True when a single vendor was identified and full details are in this object.")]
        public bool Resolved { get; set; }

        [Description("Number of candidates when resolved is false.")]
        public int Count { get; set; }

        [Description("Vendor number when resolved.")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name when resolved.")]
        public string Name { get; set; } = "";

        [Description("Primary email when resolved.")]
        public string Email { get; set; } = "";

        [Description("Primary phone when resolved.")]
        public string PhoneNo { get; set; } = "";

        [Description("Balance owed in LCY when resolved.")]
        public decimal BalanceLcy { get; set; }

        [Description("Blocked status when resolved.")]
        public string Blocked { get; set; } = "";

        [Description("Short candidate list when resolved is false.")]
        public List<Candidate> Vendors { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.VendorNo) && string.IsNullOrWhiteSpace(args.NameContains))
            throw new ToolValidationException(
                "erp_bc.finance.lookup_vendor", "vendorNo or nameContains is required.");

        return await BcClient.ExecuteAsync<Result>(
            "ResolveVendor", args, "erp_bc.finance.lookup_vendor", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.list_open_vendor_entries
// ---------------------------------------------------------------------------

/// <summary>
/// Lists a vendor's open (unpaid) ledger entries — the AP mirror of
/// <see cref="ListOpenCustomerEntries"/>.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.list_open_vendor_entries",
    Description = "List open (unpaid) vendor ledger entries for a vendor. Each entry includes document number, dates, remaining amount (LCY), days overdue, and whether it is overdue.",
    Sensitivity = "read")]
public class ListOpenVendorEntries
    : ToolHandler<ListOpenVendorEntries.Args, ListOpenVendorEntries.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Vendor number (No.) whose open ledger entries to list.")]
        public string VendorNo { get; set; } = "";

        [Description("When true, return only entries already past their due date.")]
        public bool OverdueOnly { get; set; }

        [Description("Optional. Filter to entries whose document number contains this text (case-insensitive).")]
        public string DocumentNoContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Entry
    {
        [Description("Ledger entry number.")]
        public int EntryNo { get; set; }

        [Description("Document type, e.g. Invoice, Credit Memo, or Payment.")]
        public string DocumentType { get; set; } = "";

        [Description("Document number of the ledger entry.")]
        public string DocumentNo { get; set; } = "";

        [Description("External document number, if any.")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Due date (yyyy-MM-dd); empty if not set.")]
        public string DueDate { get; set; } = "";

        [Description("Currency code of the entry; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Original amount in LCY.")]
        public decimal OriginalAmountLcy { get; set; }

        [Description("Remaining (unpaid) amount in LCY.")]
        public decimal RemainingAmountLcy { get; set; }

        [Description("True when the entry is open and its due date is in the past.")]
        public bool Overdue { get; set; }

        [Description("Days past the due date; 0 when not overdue.")]
        public int DaysOverdue { get; set; }
    }

    public class Result
    {
        [Description("Vendor number the entries belong to.")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string VendorName { get; set; } = "";

        [Description("Number of entries returned.")]
        public int Count { get; set; }

        [Description("Total remaining amount across the returned entries, in LCY.")]
        public decimal TotalRemainingLcy { get; set; }

        [Description("Total remaining amount on overdue entries only, in LCY.")]
        public decimal TotalOverdueLcy { get; set; }

        [Description("The open ledger entries.")]
        public List<Entry> Entries { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.VendorNo))
            throw new ToolValidationException(
                "erp_bc.finance.list_open_vendor_entries", "vendorNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "ListOpenVendorEntries", args, "erp_bc.finance.list_open_vendor_entries", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.list_vendors_with_overdue
// ---------------------------------------------------------------------------

/// <summary>
/// Cross-vendor report of accounts with overdue open payables.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.list_vendors_with_overdue",
    Description = "List vendors who have overdue open ledger entries. Returns each vendor's number, name, total overdue amount (LCY), and oldest due date.",
    Sensitivity = "read")]
public class ListVendorsWithOverdue
    : ToolHandler<ListVendorsWithOverdue.Args, ListVendorsWithOverdue.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class VendorOverdue
    {
        [Description("Vendor number (No.).")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string Name { get; set; } = "";

        [Description("Total overdue remaining amount in LCY.")]
        public decimal TotalOverdueLcy { get; set; }

        [Description("Oldest due date among overdue entries (yyyy-MM-dd).")]
        public string OldestDueDate { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of vendors returned.")]
        public int Count { get; set; }

        [Description("Vendors with overdue open entries, ordered by oldest due date.")]
        public List<VendorOverdue> Vendors { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>(
            "ListVendorsWithOverdue", args, "erp_bc.finance.list_vendors_with_overdue", ctx);
}

// ---------------------------------------------------------------------------
// bc.finance.get_sales_invoice
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up a posted sales invoice by number.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.get_sales_invoice",
    Description = "Look up a posted Business Central sales invoice by its number (No.). Returns customer, dates, amounts including VAT, remaining unpaid amount, and all lines.",
    Sensitivity = "read")]
public class GetSalesInvoice : ToolHandler<GetSalesInvoice.Args, GetSalesInvoice.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Posted sales invoice number (No.).")]
        public string InvoiceNo { get; set; } = "";
    }

    public class Line
    {
        [Description("Line number.")]
        public int LineNo { get; set; }

        [Description("Type of line: Item, Resource, G/L Account, etc.")]
        public string Type { get; set; } = "";

        [Description("Number on the line (item No., account No., etc.).")]
        public string No { get; set; } = "";

        [Description("Line description.")]
        public string Description { get; set; } = "";

        [Description("Quantity.")]
        public decimal Quantity { get; set; }

        [Description("Unit price.")]
        public decimal UnitPrice { get; set; }

        [Description("Line amount excluding VAT.")]
        public decimal LineAmount { get; set; }

        [Description("Line amount including VAT.")]
        public decimal AmountIncludingVat { get; set; }
    }

    public class Result
    {
        [Description("Invoice number (No.).")]
        public string InvoiceNo { get; set; } = "";

        [Description("Customer number.")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer name.")]
        public string CustomerName { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Document date (yyyy-MM-dd).")]
        public string DocumentDate { get; set; } = "";

        [Description("Due date (yyyy-MM-dd).")]
        public string DueDate { get; set; } = "";

        [Description("External document number.")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("Currency code; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total amount including VAT.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Remaining unpaid amount in LCY (0 when fully paid).")]
        public decimal RemainingAmountLcy { get; set; }

        [Description("Invoice lines.")]
        public List<Line> Lines { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.InvoiceNo))
            throw new ToolValidationException("erp_bc.finance.get_sales_invoice", "invoiceNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetSalesInvoice", args, "erp_bc.finance.get_sales_invoice", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.find_sales_invoices
// ---------------------------------------------------------------------------

/// <summary>
/// Searches posted sales invoices with optional filters.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.find_sales_invoices",
    Description = "Search posted sales invoices. Filter by customer number, invoice number substring, and/or posting date range. Returns up to a configurable number of matches.",
    Sensitivity = "read")]
public class FindSalesInvoices : ToolHandler<FindSalesInvoices.Args, FindSalesInvoices.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Customer number (No.) to filter by.")]
        public string CustomerNo { get; set; } = "";

        [Description("Optional. Filter invoices whose number contains this text.")]
        public string InvoiceNoContains { get; set; } = "";

        [Description("Optional. Earliest posting date (yyyy-MM-dd), inclusive.")]
        public string FromDate { get; set; } = "";

        [Description("Optional. Latest posting date (yyyy-MM-dd), inclusive.")]
        public string ToDate { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Match
    {
        [Description("Invoice number (No.).")]
        public string InvoiceNo { get; set; } = "";

        [Description("Customer number.")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer name.")]
        public string CustomerName { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Due date (yyyy-MM-dd).")]
        public string DueDate { get; set; } = "";

        [Description("Total amount including VAT.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Remaining unpaid amount in LCY.")]
        public decimal RemainingAmountLcy { get; set; }
    }

    public class Result
    {
        [Description("Number of invoices returned.")]
        public int Count { get; set; }

        [Description("Matching posted sales invoices.")]
        public List<Match> Invoices { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>(
            "FindSalesInvoices", args, "erp_bc.finance.find_sales_invoices", ctx);
}

// ---------------------------------------------------------------------------
// bc.finance.get_purchase_invoice
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up a posted purchase invoice by number.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.get_purchase_invoice",
    Description = "Look up a posted Business Central purchase invoice by its number (No.). Returns vendor, dates, amounts including VAT, remaining unpaid amount, and all lines.",
    Sensitivity = "read")]
public class GetPurchaseInvoice : ToolHandler<GetPurchaseInvoice.Args, GetPurchaseInvoice.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Posted purchase invoice number (No.).")]
        public string InvoiceNo { get; set; } = "";
    }

    public class Line
    {
        [Description("Line number.")]
        public int LineNo { get; set; }

        [Description("Type of line: Item, Resource, G/L Account, etc.")]
        public string Type { get; set; } = "";

        [Description("Number on the line.")]
        public string No { get; set; } = "";

        [Description("Line description.")]
        public string Description { get; set; } = "";

        [Description("Quantity.")]
        public decimal Quantity { get; set; }

        [Description("Direct unit cost.")]
        public decimal DirectUnitCost { get; set; }

        [Description("Line amount excluding VAT.")]
        public decimal LineAmount { get; set; }

        [Description("Line amount including VAT.")]
        public decimal AmountIncludingVat { get; set; }
    }

    public class Result
    {
        [Description("Invoice number (No.).")]
        public string InvoiceNo { get; set; } = "";

        [Description("Vendor number.")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor name.")]
        public string VendorName { get; set; } = "";

        [Description("Vendor invoice number, if recorded.")]
        public string VendorInvoiceNo { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Document date (yyyy-MM-dd).")]
        public string DocumentDate { get; set; } = "";

        [Description("Due date (yyyy-MM-dd).")]
        public string DueDate { get; set; } = "";

        [Description("Currency code; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total amount including VAT.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Remaining unpaid amount in LCY (0 when fully paid).")]
        public decimal RemainingAmountLcy { get; set; }

        [Description("Invoice lines.")]
        public List<Line> Lines { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.InvoiceNo))
            throw new ToolValidationException("erp_bc.finance.get_purchase_invoice", "invoiceNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetPurchaseInvoice", args, "erp_bc.finance.get_purchase_invoice", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.find_purchase_invoices
// ---------------------------------------------------------------------------

/// <summary>
/// Searches posted purchase invoices with optional filters.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.find_purchase_invoices",
    Description = "Search posted purchase invoices. Filter by vendor number, invoice number substring, and/or posting date range. Returns up to a configurable number of matches.",
    Sensitivity = "read")]
public class FindPurchaseInvoices : ToolHandler<FindPurchaseInvoices.Args, FindPurchaseInvoices.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Vendor number (No.) to filter by.")]
        public string VendorNo { get; set; } = "";

        [Description("Optional. Filter invoices whose number contains this text.")]
        public string InvoiceNoContains { get; set; } = "";

        [Description("Optional. Earliest posting date (yyyy-MM-dd), inclusive.")]
        public string FromDate { get; set; } = "";

        [Description("Optional. Latest posting date (yyyy-MM-dd), inclusive.")]
        public string ToDate { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Match
    {
        [Description("Invoice number (No.).")]
        public string InvoiceNo { get; set; } = "";

        [Description("Vendor number.")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor name.")]
        public string VendorName { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Due date (yyyy-MM-dd).")]
        public string DueDate { get; set; } = "";

        [Description("Total amount including VAT.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Remaining unpaid amount in LCY.")]
        public decimal RemainingAmountLcy { get; set; }
    }

    public class Result
    {
        [Description("Number of invoices returned.")]
        public int Count { get; set; }

        [Description("Matching posted purchase invoices.")]
        public List<Match> Invoices { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>(
            "FindPurchaseInvoices", args, "erp_bc.finance.find_purchase_invoices", ctx);
}

// ---------------------------------------------------------------------------
// bc.finance.find_gl_accounts
// ---------------------------------------------------------------------------

/// <summary>
/// Searches the chart of accounts by number or name.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.find_gl_accounts",
    Description = "Search G/L accounts whose number or name contains the given text. Omit noOrNameContains to browse the first page. Returns up to a configurable number of matches.",
    Sensitivity = "read")]
public class FindGlAccounts : ToolHandler<FindGlAccounts.Args, FindGlAccounts.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the account number or name (case-insensitive).")]
        public string NoOrNameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Match
    {
        [Description("G/L account number (No.).")]
        public string GlAccountNo { get; set; } = "";

        [Description("Account name.")]
        public string Name { get; set; } = "";

        [Description("Account type, e.g. Posting, Heading, Total.")]
        public string AccountType { get; set; } = "";

        [Description("Account balance in LCY.")]
        public decimal BalanceLcy { get; set; }

        [Description("Whether the account is blocked.")]
        public bool Blocked { get; set; }
    }

    public class Result
    {
        [Description("Number of accounts returned.")]
        public int Count { get; set; }

        [Description("Matching G/L accounts.")]
        public List<Match> Accounts { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>(
            "FindGlAccounts", args, "erp_bc.finance.find_gl_accounts", ctx);
}

// ---------------------------------------------------------------------------
// bc.finance.get_gl_account
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up a single G/L account by number.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.get_gl_account",
    Description = "Look up a G/L account by its number (No.). Returns name, account type, balance (LCY), and whether it is blocked.",
    Sensitivity = "read")]
public class GetGlAccount : ToolHandler<GetGlAccount.Args, GetGlAccount.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("G/L account number (No.), e.g. 1000.")]
        public string GlAccountNo { get; set; } = "";
    }

    public class Result
    {
        [Description("G/L account number (No.).")]
        public string GlAccountNo { get; set; } = "";

        [Description("Account name.")]
        public string Name { get; set; } = "";

        [Description("Account type, e.g. Posting, Heading, Total.")]
        public string AccountType { get; set; } = "";

        [Description("Account balance in LCY.")]
        public decimal BalanceLcy { get; set; }

        [Description("Whether the account is blocked.")]
        public bool Blocked { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.GlAccountNo))
            throw new ToolValidationException("erp_bc.finance.get_gl_account", "glAccountNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetGlAccount", args, "erp_bc.finance.get_gl_account", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.list_gl_entries
// ---------------------------------------------------------------------------

/// <summary>
/// Lists G/L entries for an account, optionally filtered by date.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.list_gl_entries",
    Description = "List G/L entries for a G/L account. Optionally filter by posting date range. Returns document number, description, amount, and debit/credit indicator.",
    Sensitivity = "read")]
public class ListGlEntries : ToolHandler<ListGlEntries.Args, ListGlEntries.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("G/L account number (No.) to list entries for.")]
        public string GlAccountNo { get; set; } = "";

        [Description("Optional. Earliest posting date (yyyy-MM-dd), inclusive.")]
        public string FromDate { get; set; } = "";

        [Description("Optional. Latest posting date (yyyy-MM-dd), inclusive.")]
        public string ToDate { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Entry
    {
        [Description("G/L entry number.")]
        public int EntryNo { get; set; }

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Document type.")]
        public string DocumentType { get; set; } = "";

        [Description("Document number.")]
        public string DocumentNo { get; set; } = "";

        [Description("Entry description.")]
        public string Description { get; set; } = "";

        [Description("Amount in LCY (always positive).")]
        public decimal AmountLcy { get; set; }

        [Description("Debit or Credit.")]
        public string DebitCredit { get; set; } = "";
    }

    public class Result
    {
        [Description("G/L account number.")]
        public string GlAccountNo { get; set; } = "";

        [Description("Account name.")]
        public string GlAccountName { get; set; } = "";

        [Description("Number of entries returned.")]
        public int Count { get; set; }

        [Description("G/L entries, most recent first.")]
        public List<Entry> Entries { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.GlAccountNo))
            throw new ToolValidationException("erp_bc.finance.list_gl_entries", "glAccountNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "ListGlEntries", args, "erp_bc.finance.list_gl_entries", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.list_bank_accounts
// ---------------------------------------------------------------------------

/// <summary>
/// Lists bank accounts with their balances.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.list_bank_accounts",
    Description = "List Business Central bank accounts with their current balance (LCY) and currency. Returns up to a configurable number of accounts.",
    Sensitivity = "read")]
public class ListBankAccounts : ToolHandler<ListBankAccounts.Args, ListBankAccounts.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class BankAccount
    {
        [Description("Bank account number (No.).")]
        public string BankAccountNo { get; set; } = "";

        [Description("Bank account name.")]
        public string Name { get; set; } = "";

        [Description("Currency code; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Balance in LCY.")]
        public decimal BalanceLcy { get; set; }

        [Description("Whether the bank account is blocked.")]
        public bool Blocked { get; set; }
    }

    public class Result
    {
        [Description("Number of bank accounts returned.")]
        public int Count { get; set; }

        [Description("Bank accounts.")]
        public List<BankAccount> BankAccounts { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>(
            "ListBankAccounts", args, "erp_bc.finance.list_bank_accounts", ctx);
}

// ---------------------------------------------------------------------------
// bc.finance.get_bank_account
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up a single bank account by number.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.get_bank_account",
    Description = "Look up a Business Central bank account by its number (No.). Returns name, currency, balance (LCY), and whether it is blocked.",
    Sensitivity = "read")]
public class GetBankAccount : ToolHandler<GetBankAccount.Args, GetBankAccount.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Bank account number (No.).")]
        public string BankAccountNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Bank account number (No.).")]
        public string BankAccountNo { get; set; } = "";

        [Description("Bank account name.")]
        public string Name { get; set; } = "";

        [Description("Currency code; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Balance in LCY.")]
        public decimal BalanceLcy { get; set; }

        [Description("Whether the bank account is blocked.")]
        public bool Blocked { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.BankAccountNo))
            throw new ToolValidationException("erp_bc.finance.get_bank_account", "bankAccountNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetBankAccount", args, "erp_bc.finance.get_bank_account", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.purchasing.find_vendors
// ---------------------------------------------------------------------------

/// <summary>
/// Searches vendors by partial name — the buy-side mirror of
/// <see cref="FindCustomers"/>.
/// </summary>
[Tool(
    Key         = "erp_bc.purchasing.find_vendors",
    Description = "Search vendors by partial name when you need a list of matches. Prefer lookup_vendor for single-vendor questions (one round-trip).",
    Sensitivity = "read")]
public class FindVendors : ToolHandler<FindVendors.Args, FindVendors.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the vendor name (case-insensitive substring). Leave empty to list vendors without filtering.")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }

        [Description(BcToolDescriptions.IncludeBalance)]
        public bool IncludeBalance { get; set; }
    }

    public class Match
    {
        [Description("Vendor number (No.).")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string Name { get; set; } = "";

        [Description("City from the vendor's address.")]
        public string City { get; set; } = "";

        [Description("Balance owed in LCY. Only set when includeBalance was true.")]
        public decimal BalanceLcy { get; set; }

        [Description("Balance due in LCY. Only set when includeBalance was true.")]
        public decimal BalanceDueLcy { get; set; }

        [Description("Whether the vendor is blocked (empty means active).")]
        public string Blocked { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of vendors returned.")]
        public int Count { get; set; }

        [Description("Matching vendors, ordered by name. Empty when nothing matched.")]
        public List<Match> Vendors { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        return await BcClient.ExecuteAsync<Result>(
            "FindVendors", args, "erp_bc.purchasing.find_vendors", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.purchasing.get_vendor
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up a vendor by number — the buy-side mirror of <see cref="GetCustomer"/>.
/// Vendors carry a balance but no credit limit.
/// </summary>
[Tool(
    Key         = "erp_bc.purchasing.get_vendor",
    Description = "Look up a Business Central vendor by their vendor number (No.). Returns name, contact details, balance owed, and whether the account is blocked.",
    Sensitivity = "read")]
public class GetVendor : ToolHandler<GetVendor.Args, GetVendor.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Business Central vendor number, e.g. V00010 or 10000.")]
        public string VendorNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Vendor number (No.).")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string Name { get; set; } = "";

        [Description("Primary email address on the vendor card.")]
        public string Email { get; set; } = "";

        [Description("Primary phone number on the vendor card.")]
        public string PhoneNo { get; set; } = "";

        [Description("Outstanding balance owed to the vendor, in the company's local currency (LCY).")]
        public decimal BalanceLcy { get; set; }

        [Description("Whether the vendor is blocked (empty means active; otherwise the block type such as Payment or All).")]
        public string Blocked { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.VendorNo))
            throw new ToolValidationException(
                "erp_bc.purchasing.get_vendor", "vendorNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetVendor", args, "erp_bc.purchasing.get_vendor", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.purchasing.lookup_vendor
// ---------------------------------------------------------------------------

/// <summary>
/// Resolves a vendor by number or name in a single gateway call.
/// </summary>
[Tool(
    Key         = "erp_bc.purchasing.lookup_vendor",
    Description = BcToolDescriptions.LookupVendor,
    Sensitivity = "read")]
public class LookupVendor : ToolHandler<LookupVendor.Args, LookupVendor.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Vendor number when known. Provide this OR nameContains.")]
        public string VendorNo { get; set; } = "";

        [Description("Partial vendor name when the number is unknown. Provide this OR vendorNo.")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Candidate
    {
        [Description("Vendor number (No.).")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string Name { get; set; } = "";

        [Description("City from the vendor's address.")]
        public string City { get; set; } = "";

        [Description("Whether the vendor is blocked (empty means active).")]
        public string Blocked { get; set; } = "";
    }

    public class Result
    {
        [Description("True when a single vendor was identified and full details are in this object.")]
        public bool Resolved { get; set; }

        [Description("Number of candidates when resolved is false.")]
        public int Count { get; set; }

        [Description("Vendor number when resolved.")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name when resolved.")]
        public string Name { get; set; } = "";

        [Description("Primary email when resolved.")]
        public string Email { get; set; } = "";

        [Description("Primary phone when resolved.")]
        public string PhoneNo { get; set; } = "";

        [Description("Balance owed in LCY when resolved.")]
        public decimal BalanceLcy { get; set; }

        [Description("Blocked status when resolved.")]
        public string Blocked { get; set; } = "";

        [Description("Short candidate list when resolved is false.")]
        public List<Candidate> Vendors { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.VendorNo) && string.IsNullOrWhiteSpace(args.NameContains))
            throw new ToolValidationException(
                "erp_bc.purchasing.lookup_vendor", "vendorNo or nameContains is required.");

        return await BcClient.ExecuteAsync<Result>(
            "ResolveVendor", args, "erp_bc.purchasing.lookup_vendor", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.purchasing.create_purchase_order
// ---------------------------------------------------------------------------

/// <summary>
/// Creates a new purchase order header for a vendor — the buy-side mirror of
/// <see cref="CreateSalesOrder"/>.
/// </summary>
[Tool(
    Key         = "erp_bc.purchasing.create_purchase_order",
    Description = "Create a new purchase order header in Business Central for a given vendor. Returns the generated order number. Lines are added separately with add_purchase_order_line.",
    Sensitivity = "write")]
public class CreatePurchaseOrder : ToolHandler<CreatePurchaseOrder.Args, CreatePurchaseOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Vendor number (No.) the order is bought from, e.g. V00010.")]
        public string VendorNo { get; set; } = "";

        [Description("Optional vendor invoice number to record on the order, if known.")]
        public string VendorInvoiceNo { get; set; } = "";
    }

    public class Result
    {
        [Description("The purchase order number generated by Business Central.")]
        public string OrderNo { get; set; } = "";

        [Description("Vendor number the order was created for.")]
        public string VendorNo { get; set; } = "";

        [Description("Name of the vendor the order was created for.")]
        public string VendorName { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.VendorNo))
            throw new ToolValidationException(
                "erp_bc.purchasing.create_purchase_order", "vendorNo is required.");

        BcCallerContext.Require(ctx, "erp_bc.purchasing.create_purchase_order");
        return await BcClient.ExecuteAsync<Result>(
            "CreatePurchaseOrder", args, "erp_bc.purchasing.create_purchase_order", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.purchasing.add_purchase_order_line
// ---------------------------------------------------------------------------

/// <summary>
/// Adds an item line to an existing purchase order — the buy-side mirror of
/// <see cref="AddSalesOrderLine"/>. Purchase lines carry a direct unit cost
/// rather than a sales price.
/// </summary>
[Tool(
    Key         = "erp_bc.purchasing.add_purchase_order_line",
    Description = "Add an item line to an existing Business Central purchase order. Provide the order number, item number, and quantity. Create the order header first with create_purchase_order. Returns the created line.",
    Sensitivity = "write")]
public class AddPurchaseOrderLine : ToolHandler<AddPurchaseOrderLine.Args, AddPurchaseOrderLine.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Purchase order number (No.) to add the line to, e.g. P-ORD-101001.")]
        public string OrderNo { get; set; } = "";

        [Description("Item number (No.) to buy on this line.")]
        public string ItemNo { get; set; } = "";

        [Description("Quantity to order. Must be greater than zero.")]
        public decimal Quantity { get; set; }

        [Description("Optional location code to receive into (e.g. BLUE, MAIN). Leave empty for the order default.")]
        public string LocationCode { get; set; } = "";
    }

    public class Result
    {
        [Description("Order number the line belongs to.")]
        public string OrderNo { get; set; } = "";

        [Description("Line number assigned by Business Central.")]
        public int LineNo { get; set; }

        [Description("Item number on the line.")]
        public string ItemNo { get; set; } = "";

        [Description("Item description copied from the item card.")]
        public string Description { get; set; } = "";

        [Description("Quantity ordered.")]
        public decimal Quantity { get; set; }

        [Description("Direct unit cost applied to the line, in the order's currency.")]
        public decimal DirectUnitCost { get; set; }

        [Description("Total line amount (quantity × direct unit cost, less any line discount), excluding VAT.")]
        public decimal LineAmount { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OrderNo))
            throw new ToolValidationException(
                "erp_bc.purchasing.add_purchase_order_line", "orderNo is required.");
        if (string.IsNullOrWhiteSpace(args.ItemNo))
            throw new ToolValidationException(
                "erp_bc.purchasing.add_purchase_order_line", "itemNo is required.");
        if (args.Quantity <= 0)
            throw new ToolValidationException(
                "erp_bc.purchasing.add_purchase_order_line", "quantity must be greater than zero.");

        BcCallerContext.Require(ctx, "erp_bc.purchasing.add_purchase_order_line");
        return await BcClient.ExecuteAsync<Result>(
            "AddPurchaseOrderLine", args, "erp_bc.purchasing.add_purchase_order_line", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.purchasing.get_purchase_order
// ---------------------------------------------------------------------------

/// <summary>
/// Reads a purchase order header by order number — the buy-side mirror of
/// <see cref="GetSalesOrder"/>.
/// </summary>
[Tool(
    Key         = "erp_bc.purchasing.get_purchase_order",
    Description = "Look up an existing Business Central purchase order by its order number (No.). Returns the vendor, status, order date, currency, and total amount including VAT.",
    Sensitivity = "read")]
public class GetPurchaseOrder : ToolHandler<GetPurchaseOrder.Args, GetPurchaseOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Purchase order number (No.), e.g. P-ORD-101001.")]
        public string OrderNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Purchase order number (No.).")]
        public string OrderNo { get; set; } = "";

        [Description("Vendor number the order is bought from.")]
        public string VendorNo { get; set; } = "";

        [Description("Name of the vendor the order is bought from.")]
        public string VendorName { get; set; } = "";

        [Description("Document status: Open, Released, Pending Approval, or Pending Prepayment.")]
        public string Status { get; set; } = "";

        [Description("Order date (yyyy-MM-dd); empty if not set.")]
        public string OrderDate { get; set; } = "";

        [Description("Vendor invoice number recorded on the order, if any.")]
        public string VendorInvoiceNo { get; set; } = "";

        [Description("Order currency code; empty means the company's local currency (LCY).")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total order amount including VAT, in the order's currency.")]
        public decimal AmountIncludingVat { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OrderNo))
            throw new ToolValidationException(
                "erp_bc.purchasing.get_purchase_order", "orderNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetPurchaseOrder", args, "erp_bc.purchasing.get_purchase_order", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.purchasing.post_purchase_order
// ---------------------------------------------------------------------------

/// <summary>
/// Posts a purchase order (receive and/or invoice). Releases the order first when Open.
/// </summary>
[Tool(
    Key         = "erp_bc.purchasing.post_purchase_order",
    Description = "Post a Business Central purchase order. postType 'Receive' posts a receipt, 'Invoice' posts the invoice, and 'ReceiveAndInvoice' (default) does both. Releases the order first when it is still Open.",
    Sensitivity = "write")]
public class PostPurchaseOrder : ToolHandler<PostPurchaseOrder.Args, PostPurchaseOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Purchase order number (No.) to post.")]
        public string OrderNo { get; set; } = "";

        [Description("What to post: 'Receive', 'Invoice', or 'ReceiveAndInvoice'. Defaults to 'ReceiveAndInvoice'.")]
        public string PostType { get; set; } = "ReceiveAndInvoice";

        [Description("Optional posting date (yyyy-MM-dd). Uses BC work date when omitted.")]
        public string PostingDate { get; set; } = "";
    }

    public class Result
    {
        [Description("Purchase order number that was posted.")]
        public string OrderNo { get; set; } = "";

        [Description("The post type that was applied.")]
        public string PostType { get; set; } = "";

        [Description("Whether a receipt was posted.")]
        public bool Received { get; set; }

        [Description("Whether an invoice was posted.")]
        public bool Invoiced { get; set; }

        [Description("True when the order was fully posted and removed.")]
        public bool Completed { get; set; }

        [Description("Order status after posting (present only when the order still exists).")]
        public string Status { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OrderNo))
            throw new ToolValidationException(
                "erp_bc.purchasing.post_purchase_order", "orderNo is required.");

        BcCallerContext.Require(ctx, "erp_bc.purchasing.post_purchase_order");
        return await BcClient.ExecuteAsync<Result>(
            "PostPurchaseOrder", args, "erp_bc.purchasing.post_purchase_order", ctx);
    }
}

// ===========================================================================
// Transfer Orders — move inventory between locations via an in-transit location.
// Workflow: create order -> add lines -> post shipment (stock to in-transit) ->
// post receipt (stock to destination). Read with get/find.
// ===========================================================================

// ---------------------------------------------------------------------------
// bc.inventory.create_transfer_order
// ---------------------------------------------------------------------------

/// <summary>
/// Creates a transfer order header that moves stock from one location to another
/// through an in-transit location. Add lines with
/// <see cref="AddTransferOrderLine"/>, then post with <see cref="PostTransferOrder"/>.
/// </summary>

[Tool(
    Key         = "erp_bc.inventory.create_transfer_order",
    Description = "Create a Business Central transfer order header to move inventory between two locations via an in-transit location. The order has no lines until you add them with add_transfer_order_line.",
    Sensitivity = "write")]
public class CreateTransferOrder : ToolHandler<CreateTransferOrder.Args, CreateTransferOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Source location code stock is transferred from (e.g. MAIN, BLUE).")]
        public string TransferFromCode { get; set; } = "";

        [Description("Destination location code stock is transferred to (e.g. EAST, RED).")]
        public string TransferToCode { get; set; } = "";

        [Description("In-transit location code that holds stock between shipment and receipt (e.g. OWN LOG, TRANSIT).")]
        public string InTransitCode { get; set; } = "";

        [Description("Optional posting date (yyyy-MM-dd). Defaults to the BC work date when omitted.")]
        public string PostingDate { get; set; } = "";

        [Description("Optional shipment date (yyyy-MM-dd).")]
        public string ShipmentDate { get; set; } = "";

        [Description("Optional receipt date (yyyy-MM-dd).")]
        public string ReceiptDate { get; set; } = "";

        [Description("Optional external document number for cross-referencing.")]
        public string ExternalDocumentNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Transfer order number (No.) assigned by Business Central.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Source location code.")]
        public string TransferFromCode { get; set; } = "";

        [Description("Source location name.")]
        public string TransferFromName { get; set; } = "";

        [Description("Destination location code.")]
        public string TransferToCode { get; set; } = "";

        [Description("Destination location name.")]
        public string TransferToName { get; set; } = "";

        [Description("In-transit location code.")]
        public string InTransitCode { get; set; } = "";

        [Description("Order status (Open or Released).")]
        public string Status { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd), or empty if unset.")]
        public string PostingDate { get; set; } = "";

        [Description("Shipment date (yyyy-MM-dd), or empty if unset.")]
        public string ShipmentDate { get; set; } = "";

        [Description("Receipt date (yyyy-MM-dd), or empty if unset.")]
        public string ReceiptDate { get; set; } = "";

        [Description("External document number.")]
        public string ExternalDocumentNo { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.TransferFromCode))
            throw new ToolValidationException(
                "erp_bc.inventory.create_transfer_order", "transferFromCode is required.");
        if (string.IsNullOrWhiteSpace(args.TransferToCode))
            throw new ToolValidationException(
                "erp_bc.inventory.create_transfer_order", "transferToCode is required.");
        if (string.IsNullOrWhiteSpace(args.InTransitCode))
            throw new ToolValidationException(
                "erp_bc.inventory.create_transfer_order", "inTransitCode is required.");

        BcCallerContext.Require(ctx, "erp_bc.inventory.create_transfer_order");
        return await BcClient.ExecuteAsync<Result>(
            "CreateTransferOrder", args, "erp_bc.inventory.create_transfer_order", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.add_transfer_order_line
// ---------------------------------------------------------------------------

/// <summary>
/// Adds an item line to an existing transfer order.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.add_transfer_order_line",
    Description = "Add an item line to an existing Business Central transfer order. The from/to locations come from the order header.",
    Sensitivity = "write")]
public class AddTransferOrderLine : ToolHandler<AddTransferOrderLine.Args, AddTransferOrderLine.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Transfer order number (No.) to add the line to.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Item number (No.) to transfer.")]
        public string ItemNo { get; set; } = "";

        [Description("Quantity to transfer. Must be greater than zero.")]
        public decimal Quantity { get; set; }

        [Description("Optional item variant code. Leave empty if the item has no variants.")]
        public string VariantCode { get; set; } = "";
    }

    public class Result
    {
        [Description("Transfer order number the line belongs to.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Line number assigned by Business Central.")]
        public int LineNo { get; set; }

        [Description("Item number on the line.")]
        public string ItemNo { get; set; } = "";

        [Description("Item variant code, if any.")]
        public string VariantCode { get; set; } = "";

        [Description("Item description copied from the item card.")]
        public string Description { get; set; } = "";

        [Description("Quantity to transfer.")]
        public decimal Quantity { get; set; }

        [Description("Unit of measure code.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Quantity scheduled to ship.")]
        public decimal QtyToShip { get; set; }

        [Description("Quantity scheduled to receive.")]
        public decimal QtyToReceive { get; set; }

        [Description("Quantity not yet fully shipped and received.")]
        public decimal OutstandingQuantity { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.TransferOrderNo))
            throw new ToolValidationException(
                "erp_bc.inventory.add_transfer_order_line", "transferOrderNo is required.");
        if (string.IsNullOrWhiteSpace(args.ItemNo))
            throw new ToolValidationException(
                "erp_bc.inventory.add_transfer_order_line", "itemNo is required.");
        if (args.Quantity <= 0)
            throw new ToolValidationException(
                "erp_bc.inventory.add_transfer_order_line", "quantity must be greater than zero.");

        BcCallerContext.Require(ctx, "erp_bc.inventory.add_transfer_order_line");
        return await BcClient.ExecuteAsync<Result>(
            "AddTransferOrderLine", args, "erp_bc.inventory.add_transfer_order_line", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.get_transfer_order
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up a transfer order header and its lines by number.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.get_transfer_order",
    Description = "Look up a Business Central transfer order by its number (No.). Returns the header (locations, status, dates) and all lines with shipped/received quantities.",
    Sensitivity = "read")]
public class GetTransferOrder : ToolHandler<GetTransferOrder.Args, GetTransferOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Transfer order number (No.), e.g. 1001.")]
        public string TransferOrderNo { get; set; } = "";
    }

    public class Line
    {
        [Description("Line number.")]
        public int LineNo { get; set; }

        [Description("Item number.")]
        public string ItemNo { get; set; } = "";

        [Description("Item variant code, if any.")]
        public string VariantCode { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Quantity to transfer.")]
        public decimal Quantity { get; set; }

        [Description("Unit of measure code.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Quantity scheduled to ship.")]
        public decimal QtyToShip { get; set; }

        [Description("Quantity already shipped.")]
        public decimal QtyShipped { get; set; }

        [Description("Quantity scheduled to receive.")]
        public decimal QtyToReceive { get; set; }

        [Description("Quantity already received.")]
        public decimal QtyReceived { get; set; }

        [Description("Quantity not yet fully shipped and received.")]
        public decimal OutstandingQuantity { get; set; }
    }

    public class Result
    {
        [Description("Transfer order number (No.).")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Source location code.")]
        public string TransferFromCode { get; set; } = "";

        [Description("Source location name.")]
        public string TransferFromName { get; set; } = "";

        [Description("Destination location code.")]
        public string TransferToCode { get; set; } = "";

        [Description("Destination location name.")]
        public string TransferToName { get; set; } = "";

        [Description("In-transit location code.")]
        public string InTransitCode { get; set; } = "";

        [Description("Order status (Open or Released).")]
        public string Status { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd), or empty if unset.")]
        public string PostingDate { get; set; } = "";

        [Description("Shipment date (yyyy-MM-dd), or empty if unset.")]
        public string ShipmentDate { get; set; } = "";

        [Description("Receipt date (yyyy-MM-dd), or empty if unset.")]
        public string ReceiptDate { get; set; } = "";

        [Description("External document number.")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("Number of lines on the order.")]
        public int LineCount { get; set; }

        [Description("Transfer order lines.")]
        public List<Line> Lines { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.TransferOrderNo))
            throw new ToolValidationException(
                "erp_bc.inventory.get_transfer_order", "transferOrderNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetTransferOrder", args, "erp_bc.inventory.get_transfer_order", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.find_transfer_orders
// ---------------------------------------------------------------------------

/// <summary>
/// Lists transfer orders, optionally filtered by source/destination location.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.find_transfer_orders",
    Description = "List Business Central transfer orders, optionally filtered by source and/or destination location. Omit both filters to browse the first page. Returns up to a configurable number of matches.",
    Sensitivity = "read")]
public class FindTransferOrders : ToolHandler<FindTransferOrders.Args, FindTransferOrders.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional source location code to filter by. Leave empty to include all.")]
        public string TransferFromCode { get; set; } = "";

        [Description("Optional destination location code to filter by. Leave empty to include all.")]
        public string TransferToCode { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Match
    {
        [Description("Transfer order number (No.).")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Source location code.")]
        public string TransferFromCode { get; set; } = "";

        [Description("Destination location code.")]
        public string TransferToCode { get; set; } = "";

        [Description("In-transit location code.")]
        public string InTransitCode { get; set; } = "";

        [Description("Order status (Open or Released).")]
        public string Status { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd), or empty if unset.")]
        public string PostingDate { get; set; } = "";

        [Description("Shipment date (yyyy-MM-dd), or empty if unset.")]
        public string ShipmentDate { get; set; } = "";

        [Description("Receipt date (yyyy-MM-dd), or empty if unset.")]
        public string ReceiptDate { get; set; } = "";

        [Description("External document number.")]
        public string ExternalDocumentNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of transfer orders returned.")]
        public int Count { get; set; }

        [Description("Matching transfer orders. Empty when nothing matched.")]
        public List<Match> TransferOrders { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        return await BcClient.ExecuteAsync<Result>(
            "FindTransferOrders", args, "erp_bc.inventory.find_transfer_orders", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.post_transfer_order
// ---------------------------------------------------------------------------

/// <summary>
/// Posts a transfer order's shipment and/or receipt. Releases the order first
/// when shipping an Open order.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.post_transfer_order",
    Description = "Post a Business Central transfer order. postType 'Ship' moves stock from the source into the in-transit location, 'Receive' moves it from in-transit to the destination, and 'ShipAndReceive' (default) does both. Shipping releases the order first when needed.",
    Sensitivity = "write")]
public class PostTransferOrder : ToolHandler<PostTransferOrder.Args, PostTransferOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Transfer order number (No.) to post.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("What to post: 'Ship', 'Receive', or 'ShipAndReceive'. Defaults to 'ShipAndReceive'.")]
        public string PostType { get; set; } = "ShipAndReceive";
    }

    public class Result
    {
        [Description("Transfer order number that was posted.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("The post type that was applied.")]
        public string PostType { get; set; } = "";

        [Description("Whether a shipment was posted.")]
        public bool Shipped { get; set; }

        [Description("Whether a receipt was posted.")]
        public bool Received { get; set; }

        [Description("True when the order was fully posted and removed (no remaining quantity).")]
        public bool Completed { get; set; }

        [Description("Order status after posting (present only when the order still exists).")]
        public string Status { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.TransferOrderNo))
            throw new ToolValidationException(
                "erp_bc.inventory.post_transfer_order", "transferOrderNo is required.");

        BcCallerContext.Require(ctx, "erp_bc.inventory.post_transfer_order");
        return await BcClient.ExecuteAsync<Result>(
            "PostTransferOrder", args, "erp_bc.inventory.post_transfer_order", ctx);
    }
}

// ===========================================================================
// LS Central retail — POS sales transactions
// ===========================================================================

// ---------------------------------------------------------------------------
// bc.retail.get_pos_transaction
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up an LS Central POS transaction by receipt number or by the
/// composite key store + terminal + transaction number.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_pos_transaction",
    Description = "Look up an LS Central POS transaction (LSC Transaction Header). Provide receiptNo, or storeNo + posTerminalNo + transactionNo. Returns header amounts, sales lines, and payment tenders.",
    Sensitivity = "read")]
public class GetPosTransaction : ToolHandler<GetPosTransaction.Args, GetPosTransaction.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description(BcToolDescriptions.StoreNo)]
        public string StoreNo { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. POS terminal number (POS Terminal No.), required with storeNo and transactionNo.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("Optional. Transaction number (Transaction No.), required with storeNo and posTerminalNo when receiptNo is omitted.")]
        public int? TransactionNo { get; set; }

        [Description("Optional. Receipt number (Receipt No.) — the usual user-facing identifier. Use alone or with storeNo.")]
        public string ReceiptNo { get; set; } = "";
    }

    public class SalesLine
    {
        [Description("Line number on the transaction.")]
        public int LineNo { get; set; }

        [Description("Item number sold.")]
        public string ItemNo { get; set; } = "";

        [Description("Item description from the item card.")]
        public string ItemDescription { get; set; } = "";

        [Description("Quantity sold.")]
        public decimal Quantity { get; set; }

        [Description("Unit of measure.")]
        public string UnitOfMeasure { get; set; } = "";

        [Description("Unit price.")]
        public decimal Price { get; set; }

        [Description("Net line amount.")]
        public decimal NetAmount { get; set; }

        [Description("VAT amount on the line.")]
        public decimal VatAmount { get; set; }

        [Description("Discount amount on the line.")]
        public decimal DiscountAmount { get; set; }
    }

    public class PaymentLine
    {
        [Description("Payment line number.")]
        public int LineNo { get; set; }

        [Description("Tender type code, e.g. CASH or CARD.")]
        public string TenderType { get; set; } = "";

        [Description("Amount tendered in LCY.")]
        public decimal AmountTendered { get; set; }

        [Description("Amount in foreign currency when applicable.")]
        public decimal AmountInCurrency { get; set; }

        [Description("Currency code; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";
    }

    public class Result
    {
        [Description("Transaction number (Transaction No.).")]
        public int TransactionNo { get; set; }

        [Description("Store number (Store No.).")]
        public string StoreNo { get; set; } = "";

        [Description("POS terminal number.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("Receipt number.")]
        public string ReceiptNo { get; set; } = "";

        [Description("Transaction type, e.g. Sales, Payment, Voided.")]
        public string TransactionType { get; set; } = "";

        [Description("Transaction date (yyyy-MM-dd).")]
        public string Date { get; set; } = "";

        [Description("Transaction time.")]
        public string Time { get; set; } = "";

        [Description("Staff ID on the transaction.")]
        public string StaffId { get; set; } = "";

        [Description("Customer number when assigned.")]
        public string CustomerNo { get; set; } = "";

        [Description("Mobile number on the POS transaction (EDM customization field on LSC Transaction Header).")]
        public string MobileNumber { get; set; } = "";

        [Description("Customer Order ID on the POS transaction (EDM customization field, e.g. CO24-000020487).")]
        public string CustomerOrderId { get; set; } = "";

        [Description("Net amount.")]
        public decimal NetAmount { get; set; }

        [Description("Gross amount.")]
        public decimal GrossAmount { get; set; }

        [Description("Cost amount.")]
        public decimal CostAmount { get; set; }

        [Description("Payment total.")]
        public decimal Payment { get; set; }

        [Description("Discount amount.")]
        public decimal DiscountAmount { get; set; }

        [Description("Total discount.")]
        public decimal TotalDiscount { get; set; }

        [Description("Number of items.")]
        public decimal NoOfItems { get; set; }

        [Description("Entry status: blank, Voided, Posted, or Training.")]
        public string EntryStatus { get; set; } = "";

        [Description("Statement code when assigned.")]
        public string StatementCode { get; set; } = "";

        [Description("Item sales lines on the transaction.")]
        public List<SalesLine> SalesLines { get; set; } = new();

        [Description("Payment/tender lines on the transaction.")]
        public List<PaymentLine> Payments { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        var hasKey = !string.IsNullOrWhiteSpace(args.StoreNo)
            && !string.IsNullOrWhiteSpace(args.PosTerminalNo)
            && args.TransactionNo is > 0;
        if (string.IsNullOrWhiteSpace(args.ReceiptNo) && !hasKey)
            throw new ToolValidationException(
                "erp_bc.retail.get_pos_transaction",
                "Provide receiptNo, or storeNo + posTerminalNo + transactionNo.");

        return await BcClient.ExecuteAsync<Result>(
            "GetPosTransaction", args, "erp_bc.retail.get_pos_transaction", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.find_pos_transactions
// ---------------------------------------------------------------------------

/// <summary>
/// Searches LS Central POS transactions, defaulting to Sales type.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.find_pos_transactions",
    Description = "Browse or look up INDIVIDUAL LS Central POS receipts (LSC Transaction Header), newest first. Defaults to Sales type; filter by store (storeNo or storeNameContains — e.g. 'مخرج 9' resolves to S004), terminal, customer, receipt substring, mobile number, customer order ID, and/or date range. Returns at most 500 receipts per page (page with skip). NOT for sales totals: a 'total POS sales for 2024/2025' question must use sum_sales (preset posSales / posNetSales) — find_pos_transactions returns only a capped sample of receipts, not the real total, and a wide date range overflows the context. Use count_transactions for receipt counts.",
    Sensitivity = "read",
    MaxResultBytes = 65_536)]
public class FindPosTransactions : ToolHandler<FindPosTransactions.Args, FindPosTransactions.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description(BcToolDescriptions.StoreNo)]
        public string StoreNo { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. POS terminal number to filter by.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("Optional. Customer number to filter by.")]
        public string CustomerNo { get; set; } = "";

        [Description("Optional. Filter receipts whose number contains this text (case-insensitive).")]
        public string ReceiptNoContains { get; set; } = "";

        [Description("Optional. Exact mobile number on the POS transaction (EDM Mobile Number field).")]
        public string MobileNumber { get; set; } = "";

        [Description("Optional. Filter transactions whose mobile number contains this text (case-insensitive).")]
        public string MobileNumberContains { get; set; } = "";

        [Description("Optional. Exact Customer Order ID on the POS transaction (EDM field, e.g. CO24-000020487).")]
        public string CustomerOrderId { get; set; } = "";

        [Description("Optional. Filter transactions whose Customer Order ID contains this text (case-insensitive).")]
        public string CustomerOrderIdContains { get; set; } = "";

        [Description("Optional. Earliest transaction date (yyyy-MM-dd), inclusive.")]
        public string FromDate { get; set; } = "";

        [Description("Optional. Latest transaction date (yyyy-MM-dd), inclusive.")]
        public string ToDate { get; set; } = "";

        [Description("Optional. Transaction type; defaults to Sales. Other values: Payment, Voided, Logon, Logoff, Tender Decl., etc.")]
        public string TransactionType { get; set; } = "";

        [Description("Optional. Page size (default 50, max 500). Results are newest first; page with skip.")]
        public int? Top { get; set; }

        [Description("Optional. Number of matching rows to skip before returning results (for pagination).")]
        public int? Skip { get; set; }
    }

    public class Match
    {
        [Description("Transaction number.")]
        public int TransactionNo { get; set; }

        [Description("Store number.")]
        public string StoreNo { get; set; } = "";

        [Description("POS terminal number.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("Receipt number.")]
        public string ReceiptNo { get; set; } = "";

        [Description("Transaction type.")]
        public string TransactionType { get; set; } = "";

        [Description("Transaction date (yyyy-MM-dd).")]
        public string Date { get; set; } = "";

        [Description("Transaction time.")]
        public string Time { get; set; } = "";

        [Description("Staff ID.")]
        public string StaffId { get; set; } = "";

        [Description("Customer number when assigned.")]
        public string CustomerNo { get; set; } = "";

        [Description("Mobile number on the POS transaction (EDM customization field on LSC Transaction Header).")]
        public string MobileNumber { get; set; } = "";

        [Description("Customer Order ID on the POS transaction (EDM customization field, e.g. CO24-000020487).")]
        public string CustomerOrderId { get; set; } = "";

        [Description("Net amount.")]
        public decimal NetAmount { get; set; }

        [Description("Gross amount.")]
        public decimal GrossAmount { get; set; }

        [Description("Payment total.")]
        public decimal Payment { get; set; }

        [Description("Discount amount.")]
        public decimal DiscountAmount { get; set; }

        [Description("Entry status.")]
        public string EntryStatus { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of transactions returned.")]
        public int Count { get; set; }

        [Description("Page size applied by the gateway.")]
        public int Top { get; set; }

        [Description("Rows skipped before this page.")]
        public int Skip { get; set; }

        [Description("True when more matching transactions exist after this page.")]
        public bool HasMore { get; set; }

        [Description("Matching POS transactions.")]
        public List<Match> Transactions { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        // Hard-cap the page size. This tool browses individual receipts; a mis-routed
        // "total POS sales for a year" query (which belongs to sum_sales) must never be
        // able to pull enough rows to overflow the agent's context window.
        const int MaxTop = 500;
        args.Top = args.Top is int t && t > 0 ? Math.Min(t, MaxTop) : 50;

        return await BcClient.ExecuteAsync<Result>(
            "FindPosTransactions", args, "erp_bc.retail.find_pos_transactions", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_customer_order
// ---------------------------------------------------------------------------

/// <summary>
/// Looks up an LS Central customer order (click &amp; collect / ship-from-store)
/// by document ID or external ID.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_customer_order",
    Description = "Look up an LS Central customer order (LSC Customer Order Header). Provide documentId or externalId. Returns header, processing status, contact/ship-to details, and all lines.",
    Sensitivity = "read")]
public class GetCustomerOrder : ToolHandler<GetCustomerOrder.Args, GetCustomerOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Customer order document ID (Document ID), e.g. CO26-000003883.")]
        public string DocumentId { get; set; } = "";

        [Description("Optional external ID when document ID is unknown, e.g. SA26010418162.")]
        public string ExternalId { get; set; } = "";
    }

    public class Line
    {
        [Description("Line number.")]
        public int LineNo { get; set; }

        [Description("Line status, e.g. To Pick, To Collect, Collected.")]
        public string Status { get; set; } = "";

        [Description("Line type: Item, Payment, Shipping, etc.")]
        public string LineType { get; set; } = "";

        [Description("Item or account number on the line.")]
        public string Number { get; set; } = "";

        [Description("Variant code when applicable.")]
        public string VariantCode { get; set; } = "";

        [Description("Item description.")]
        public string ItemDescription { get; set; } = "";

        [Description("Quantity ordered.")]
        public decimal Quantity { get; set; }

        [Description("Unit of measure code.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Unit price.")]
        public decimal Price { get; set; }

        [Description("Net line amount.")]
        public decimal NetAmount { get; set; }

        [Description("VAT amount.")]
        public decimal VatAmount { get; set; }

        [Description("Gross line amount.")]
        public decimal Amount { get; set; }

        [Description("Discount amount.")]
        public decimal DiscountAmount { get; set; }

        [Description("Whether this is a ship-to-customer order line.")]
        public bool ShipOrder { get; set; }
    }

    public class Result
    {
        [Description("Document ID.")]
        public string DocumentId { get; set; } = "";

        [Description("External ID.")]
        public string ExternalId { get; set; } = "";

        [Description("Store where the order was created (Created at Store).")]
        public string CreatedAtStore { get; set; } = "";

        [Description("Created date/time (yyyy-MM-dd HH:mm:ss).")]
        public string Created { get; set; } = "";

        [Description("Processing status, e.g. Ready.")]
        public string ProcessingStatus { get; set; } = "";

        [Description("Customer number when assigned.")]
        public string CustomerNo { get; set; } = "";

        [Description("Member card number when assigned.")]
        public string MemberCardNo { get; set; } = "";

        [Description("Customer name.")]
        public string Name { get; set; } = "";

        [Description("Email address.")]
        public string Email { get; set; } = "";

        [Description("Mobile phone number.")]
        public string MobilePhoneNo { get; set; } = "";

        [Description("Bill-to address.")]
        public string Address { get; set; } = "";

        [Description("Bill-to city.")]
        public string City { get; set; } = "";

        [Description("Bill-to post code.")]
        public string PostCode { get; set; } = "";

        [Description("Bill-to country/region code.")]
        public string CountryRegionCode { get; set; } = "";

        [Description("Ship-to name.")]
        public string ShipToName { get; set; } = "";

        [Description("Ship-to city.")]
        public string ShipToCity { get; set; } = "";

        [Description("Ship-to phone number.")]
        public string ShipToPhoneNo { get; set; } = "";

        [Description("Requested delivery date (yyyy-MM-dd).")]
        public string RequestedDeliveryDate { get; set; } = "";

        [Description("Customer order source type.")]
        public string CoSourceType { get; set; } = "";

        [Description("Lines still to pick.")]
        public int LinesToPick { get; set; }

        [Description("Lines ready to collect.")]
        public int LinesToCollect { get; set; }

        [Description("Lines already collected.")]
        public int LinesCollected { get; set; }

        [Description("Rejected lines.")]
        public int LinesRejected { get; set; }

        [Description("Lines in shortage.")]
        public int LinesShortage { get; set; }

        [Description("All order lines.")]
        public List<Line> Lines { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.DocumentId) && string.IsNullOrWhiteSpace(args.ExternalId))
            throw new ToolValidationException(
                "erp_bc.retail.get_customer_order", "documentId or externalId is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetCustomerOrder", args, "erp_bc.retail.get_customer_order", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.find_customer_orders
// ---------------------------------------------------------------------------

/// <summary>
/// Searches LS Central customer orders with optional filters.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.find_customer_orders",
    Description = "Search LS Central customer orders (LSC Customer Order Header). Filter by document ID, external ID, customer, store, mobile phone, processing status, and/or created date range.",
    Sensitivity = "read")]
public class FindCustomerOrders : ToolHandler<FindCustomerOrders.Args, FindCustomerOrders.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Filter document IDs containing this text.")]
        public string DocumentIdContains { get; set; } = "";

        [Description("Optional. Exact external ID match.")]
        public string ExternalId { get; set; } = "";

        [Description("Optional. Filter external IDs containing this text.")]
        public string ExternalIdContains { get; set; } = "";

        [Description("Optional. Customer number (Customer No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Optional. Store where the order was created (Created at Store). Pass store code or partial store name.")]
        public string CreatedAtStore { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. Filter mobile phone numbers containing this text.")]
        public string MobilePhoneNoContains { get; set; } = "";

        [Description("Optional. Processing status, e.g. Ready.")]
        public string ProcessingStatus { get; set; } = "";

        [Description("Optional. Earliest created date (yyyy-MM-dd), inclusive.")]
        public string FromCreatedDate { get; set; } = "";

        [Description("Optional. Latest created date (yyyy-MM-dd), inclusive.")]
        public string ToCreatedDate { get; set; } = "";

        [Description("Optional. Omit to return all matches; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Match
    {
        [Description("Document ID.")]
        public string DocumentId { get; set; } = "";

        [Description("External ID.")]
        public string ExternalId { get; set; } = "";

        [Description("Created at store.")]
        public string CreatedAtStore { get; set; } = "";

        [Description("Created date/time.")]
        public string Created { get; set; } = "";

        [Description("Processing status.")]
        public string ProcessingStatus { get; set; } = "";

        [Description("Customer number.")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer name.")]
        public string Name { get; set; } = "";

        [Description("Mobile phone number.")]
        public string MobilePhoneNo { get; set; } = "";

        [Description("Requested delivery date (yyyy-MM-dd).")]
        public string RequestedDeliveryDate { get; set; } = "";

        [Description("Lines to pick.")]
        public int LinesToPick { get; set; }

        [Description("Lines to collect.")]
        public int LinesToCollect { get; set; }

        [Description("Lines collected.")]
        public int LinesCollected { get; set; }
    }

    public class Result
    {
        [Description("Number of customer orders returned.")]
        public int Count { get; set; }

        [Description("Matching customer orders.")]
        public List<Match> CustomerOrders { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>(
            "FindCustomerOrders", args, "erp_bc.retail.find_customer_orders", ctx);
}

// ---------------------------------------------------------------------------
// bc.retail.find_stores
// ---------------------------------------------------------------------------

/// <summary>
/// Retail-scoped alias for listing LS Central stores — resolves branch names to codes.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.find_stores",
    Description = "List LS Central retail stores with name, store number (No.), and location code. Filter by storeNo, partial name (nameContains), or locationCode. Use to resolve a branch name (e.g. 'مخرج 9', 'Exit 9') to store code S004 and location L004 before store-scoped sales queries.",
    Sensitivity = "read")]
public class RetailFindStores : ToolHandler<FindStores.Args, FindStores.Result>
{
    public override async Task<FindStores.Result> HandleAsync(FindStores.Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<FindStores.Result>(
            "FindStores", args, "erp_bc.retail.find_stores", ctx);
}

// ---------------------------------------------------------------------------
// bc.retail.sum_sales
// ---------------------------------------------------------------------------

/// <summary>
/// Sums POS or customer-order sales amounts via the gateway Sum operation.
/// Retail-scoped alias so the retail agent can total sales without delegating to data.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.sum_sales",
    Description = "Sum POS or customer-order sales amounts (gateway Sum operation). Use for period totals like 'total POS sales in 2024' or 'sales for Exit 9 today' — do NOT use find_pos_transactions for totals. Presets: posSales (gross), posNetSales, customerOrderSales. Filter by Date range and Store No., or pass storeNameContains (e.g. 'مخرج 9') to scope to one branch. Pass items[] to sum several periods in one call and read grandTotal.",
    Sensitivity = "read")]
public class SumRetailSales : ToolHandler<SumRetailSales.Args, SumRecords.Result>
{
    public class Args : SumRecords.Args
    {
        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";
    }

    public override async Task<SumRecords.Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (args.Items.Count == 0 && string.IsNullOrWhiteSpace(args.Entity) && string.IsNullOrWhiteSpace(args.Preset))
            throw new ToolValidationException("erp_bc.retail.sum_sales", "preset or items is required.");

        return await BcClient.ExecuteAsync<SumRecords.Result>("Sum", args, "erp_bc.retail.sum_sales", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.count_transactions
// ---------------------------------------------------------------------------

/// <summary>
/// Counts LS Central transaction rows. Retail-scoped alias for count on POS entities.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.count_transactions",
    Description = "Count POS transaction rows (gateway Count operation). Use for 'how many receipts' — not for sales amount totals (use sum_sales). Default entity is LSC Transaction Header; pass filters such as Date range or Store No., or storeNameContains for a branch name.",
    Sensitivity = "read")]
public class CountRetailTransactions : ToolHandler<CountRetailTransactions.Args, CountRecords.Result>
{
    public class Args : CountRecords.Args
    {
        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";
    }

    public override async Task<CountRecords.Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Entity))
            args.Entity = "LSC Transaction Header";

        return await BcClient.ExecuteAsync<CountRecords.Result>("Count", args, "erp_bc.retail.count_transactions", ctx);
    }
}

// ===========================================================================
// Generic data access — count, query, list companies
// ===========================================================================

// ---------------------------------------------------------------------------
// bc.data.list_companies
// ---------------------------------------------------------------------------

[Tool(
    Key         = "erp_bc.data.list_companies",
    Description = "List the Business Central companies available on the server.",
    Sensitivity = "read")]
public class ListCompanies : ToolHandler<ListCompanies.Args, ListCompanies.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";
    }

    public class CompanyRow
    {
        [Description("Company name used in ChangeCompany and OData URLs.")]
        public string Name { get; set; } = "";

        [Description("Display name shown in the BC client.")]
        public string DisplayName { get; set; } = "";

        [Description("Whether this is an evaluation company.")]
        public bool EvaluationCompany { get; set; }
    }

    public class Result
    {
        [Description("Number of companies returned.")]
        public int Count { get; set; }

        [Description("Companies on the server.")]
        public List<CompanyRow> Companies { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>("ListCompanies", args, "erp_bc.data.list_companies", ctx);
}

// ---------------------------------------------------------------------------
// bc.data.count_records
// ---------------------------------------------------------------------------

[Tool(
    Key         = "erp_bc.data.count_records",
    Description = "Count rows of a supported Business Central / LS Central entity, optionally filtered. Set allCompanies=true for a per-company breakdown and grand total.",
    Sensitivity = "read")]
public class CountRecords : ToolHandler<CountRecords.Args, CountRecords.Result>
{
    public class Filter
    {
        [Description("Business Central field name, e.g. \"No.\", \"Store No.\", \"Date\".")]
        public string Field { get; set; } = "";

        [Description("Filter operator: =, <>, >, <, >=, <=, contains, range. Defaults to =.")]
        public string Operator { get; set; } = "";

        [Description("Filter value (for range use BC syntax, e.g. 2024-01-01..2024-12-31).")]
        public string Value { get; set; } = "";
    }

    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Entity name, e.g. Customer, LSC Transaction Header, LSC Trans. Sales Entry.")]
        public string Entity { get; set; } = "";

        [Description("Optional filters applied before counting.")]
        public List<Filter> Filters { get; set; } = new();

        [Description("When true, count in every company and return a breakdown plus total.")]
        public bool AllCompanies { get; set; }
    }

    public class CompanyCount
    {
        [Description("Company name.")]
        public string Company { get; set; } = "";

        [Description("Company display name.")]
        public string DisplayName { get; set; } = "";

        [Description("Whether the company was accessible.")]
        public bool Accessible { get; set; }

        [Description("Row count when accessible.")]
        public int Count { get; set; }

        [Description("Error message when not accessible.")]
        public string Error { get; set; } = "";
    }

    public class Result
    {
        [Description("Entity that was counted.")]
        public string Entity { get; set; } = "";

        [Description("Row count in the selected company (single-company mode).")]
        public int Count { get; set; }

        [Description("Grand total across accessible companies (allCompanies mode).")]
        public int Total { get; set; }

        [Description("Number of companies that were counted successfully.")]
        public int AccessibleCompanies { get; set; }

        [Description("Per-company breakdown when allCompanies=true.")]
        public List<CompanyCount> ByCompany { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Entity))
            throw new ToolValidationException("erp_bc.data.count_records", "entity is required.");

        return await BcClient.ExecuteAsync<Result>("Count", args, "erp_bc.data.count_records", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.data.sum_records
// ---------------------------------------------------------------------------

[Tool(
    Key         = "erp_bc.data.sum_records",
    Description = "Sum a numeric field on a supported entity (totals, not row counts). Use preset for common cases: posSales, posNetSales, customerOrderSales, salesInvoices, bcSalesOrders. Pass items[] to sum several sources in one call and get grandTotal.",
    Sensitivity = "read")]
public class SumRecords : ToolHandler<SumRecords.Args, SumRecords.Result>
{
    public class Filter
    {
        [Description("Business Central field name, e.g. \"Date\", \"Posting Date\", \"Store No.\".")]
        public string Field { get; set; } = "";

        [Description("Filter operator: =, <>, >, <, >=, <=, contains, range. Defaults to =.")]
        public string Operator { get; set; } = "";

        [Description("Filter value (for range use BC syntax, e.g. 2024-01-01..2024-12-31).")]
        public string Value { get; set; } = "";
    }

    public class SumItem
    {
        [Description("Optional label echoed in the response, e.g. posSales.")]
        public string Alias { get; set; } = "";

        [Description("Optional preset: posSales, posNetSales, customerOrderSales, salesInvoices, bcSalesOrders.")]
        public string Preset { get; set; } = "";

        [Description("Entity name when preset is omitted, e.g. LSC Transaction Header.")]
        public string Entity { get; set; } = "";

        [Description("Decimal field to sum; omitted uses the preset/entity default.")]
        public string Field { get; set; } = "";

        [Description("Optional filters applied before summing.")]
        public List<Filter> Filters { get; set; } = new();
    }

    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional preset for a single sum request.")]
        public string Preset { get; set; } = "";

        [Description("Entity name when preset is omitted.")]
        public string Entity { get; set; } = "";

        [Description("Decimal field to sum; omitted uses the preset/entity default.")]
        public string Field { get; set; } = "";

        [Description("Optional filters applied before summing.")]
        public List<Filter> Filters { get; set; } = new();

        [Description("When true (default), include matching row count alongside total.")]
        public bool IncludeCount { get; set; } = true;

        [Description("Batch mode: sum several entities/presets in one call.")]
        public List<SumItem> Items { get; set; } = new();
    }

    public class ItemResult
    {
        [Description("Optional alias from the request.")]
        public string Alias { get; set; } = "";

        [Description("Entity that was summed.")]
        public string Entity { get; set; } = "";

        [Description("Field that was summed.")]
        public string Field { get; set; } = "";

        [Description("Sum total.")]
        public decimal Total { get; set; }

        [Description("Matching row count when includeCount=true.")]
        public int Count { get; set; }
    }

    public class Result
    {
        [Description("Single-sum mode: entity summed.")]
        public string Entity { get; set; } = "";

        [Description("Single-sum mode: field summed.")]
        public string Field { get; set; } = "";

        [Description("Single-sum mode: sum total.")]
        public decimal Total { get; set; }

        [Description("Single-sum mode: matching row count.")]
        public int Count { get; set; }

        [Description("Batch mode: per-item results.")]
        public List<ItemResult> Items { get; set; } = new();

        [Description("Batch mode: sum of all item totals.")]
        public decimal GrandTotal { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (args.Items.Count == 0 && string.IsNullOrWhiteSpace(args.Entity) && string.IsNullOrWhiteSpace(args.Preset))
            throw new ToolValidationException("erp_bc.data.sum_records", "entity, preset, or items is required.");

        return await BcClient.ExecuteAsync<Result>("Sum", args, "erp_bc.data.sum_records", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.data.query_records
// ---------------------------------------------------------------------------

[Tool(
    Key         = "erp_bc.data.query_records",
    Description = "List/filter rows of a supported Business Central / LS Central entity. Provide filters and optional field names to return.",
    Sensitivity = "read")]
public class QueryRecords : ToolHandler<QueryRecords.Args, QueryRecords.Result>
{
    public class Filter
    {
        [Description("Business Central field name.")]
        public string Field { get; set; } = "";

        [Description("Filter operator: =, <>, >, <, >=, <=, contains, range. Defaults to =.")]
        public string Operator { get; set; } = "";

        [Description("Filter value.")]
        public string Value { get; set; } = "";
    }

    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Entity name, e.g. Item, LSC Transaction Header, LSC Trans. Sales Entry.")]
        public string Entity { get; set; } = "";

        [Description("Optional filters.")]
        public List<Filter> Filters { get; set; } = new();

        [Description("Optional BC field names to return. Omit for a default field set.")]
        public List<string> Fields { get; set; } = new();

        [Description("Optional. Omit to return all rows; set a positive number to cap results.")]
        public int? Top { get; set; }
    }

    public class Result
    {
        [Description("Entity queried.")]
        public string Entity { get; set; } = "";

        [Description("Number of rows returned.")]
        public int Count { get; set; }

        // Value type is `object`, not JsonElement: some NJsonSchema versions render a
        // Dictionary<string,JsonElement> with an array-valued `additionalProperties`,
        // which fails draft-07 metaschema validation at tool registration ("got array,
        // want boolean or object"). `object` renders as the canonical "any" schema
        // ({ "additionalProperties": {} }) across versions; System.Text.Json still
        // deserializes each value into a JsonElement at runtime.
        [Description("Result rows as field-name → value maps.")]
        public List<Dictionary<string, object>> Rows { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Entity))
            throw new ToolValidationException("erp_bc.data.query_records", "entity is required.");

        return await BcClient.ExecuteAsync<Result>("Query", args, "erp_bc.data.query_records", ctx);
    }
}
