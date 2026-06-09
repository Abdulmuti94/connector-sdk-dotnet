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
            "GetCustomer", args, "erp_bc.sales.get_customer");
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
            "GetItem", args, "erp_bc.inventory.get_item");
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

        return await BcClient.ExecuteAsync<Result>(
            "CreateSalesOrder", args, "erp_bc.sales.create_sales_order");
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
    Description = "Search Business Central customers whose name contains the given text. Use when you have a customer's name but not their number (No.). Omit nameContains to browse the first page of customers. Returns up to a configurable number of matches.",
    Sensitivity = "read")]
public class FindCustomers : ToolHandler<FindCustomers.Args, FindCustomers.Result>
{
    public class Args
    {
        [Description("Optional. Text to search for within the customer name (case-insensitive substring). Leave empty to list customers without filtering.")]
        public string NameContains { get; set; } = "";

        [Description("Maximum number of matches to return (1-50). Defaults to 10.")]
        public int Top { get; set; } = 10;
    }

    public class Match
    {
        [Description("Customer number (No.).")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer display name.")]
        public string Name { get; set; } = "";

        [Description("City from the customer's address.")]
        public string City { get; set; } = "";

        [Description("Outstanding balance in the company's local currency (LCY).")]
        public decimal BalanceLcy { get; set; }

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
            "FindCustomers", args, "erp_bc.sales.find_customers");
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
            "GetSalesOrder", args, "erp_bc.sales.get_sales_order");
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

        return await BcClient.ExecuteAsync<Result>(
            "AddSalesOrderLine", args, "erp_bc.sales.add_sales_order_line");
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
    Description = "Search Business Central items whose description contains the given text. Use when you have a product name but not its item number (No.). Omit descriptionContains to browse the first page of items. Returns up to a configurable number of matches.",
    Sensitivity = "read")]
public class FindItems : ToolHandler<FindItems.Args, FindItems.Result>
{
    public class Args
    {
        [Description("Optional. Text to search for within the item description (case-insensitive substring). Leave empty to list items without filtering.")]
        public string DescriptionContains { get; set; } = "";

        [Description("Maximum number of matches to return (1-50). Defaults to 10.")]
        public int Top { get; set; } = 10;
    }

    public class Match
    {
        [Description("Item number (No.).")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Unit price in the company's local currency.")]
        public decimal UnitPrice { get; set; }

        [Description("Quantity currently on hand across all locations.")]
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
            "FindItems", args, "erp_bc.inventory.find_items");
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
    Description = "List open (unpaid) customer ledger entries for a customer. Each entry includes its document number, posting and due dates, remaining amount (LCY), and whether it is overdue. Use to answer questions about a customer's outstanding or overdue balance.",
    Sensitivity = "read")]
public class ListOpenCustomerEntries
    : ToolHandler<ListOpenCustomerEntries.Args, ListOpenCustomerEntries.Result>
{
    public class Args
    {
        [Description("Customer number (No.) whose open ledger entries to list, e.g. C00010.")]
        public string CustomerNo { get; set; } = "";

        [Description("When true, return only entries already past their due date. Defaults to false (all open entries).")]
        public bool OverdueOnly { get; set; }

        [Description("Maximum number of entries to return (1-100). Defaults to 50.")]
        public int Top { get; set; } = 50;
    }

    public class Entry
    {
        [Description("Document type, e.g. Invoice, Credit Memo, or Payment.")]
        public string DocumentType { get; set; } = "";

        [Description("Document number of the ledger entry.")]
        public string DocumentNo { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Due date (yyyy-MM-dd); empty if not set.")]
        public string DueDate { get; set; } = "";

        [Description("Remaining (unpaid) amount in the company's local currency (LCY).")]
        public decimal RemainingAmountLcy { get; set; }

        [Description("True when the entry is open and its due date is in the past.")]
        public bool Overdue { get; set; }
    }

    public class Result
    {
        [Description("Customer number the entries belong to.")]
        public string CustomerNo { get; set; } = "";

        [Description("Number of entries returned.")]
        public int Count { get; set; }

        [Description("Total remaining amount across the returned entries, in LCY.")]
        public decimal TotalRemainingLcy { get; set; }

        [Description("The open ledger entries.")]
        public List<Entry> Entries { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.CustomerNo))
            throw new ToolValidationException(
                "erp_bc.finance.list_open_customer_entries", "customerNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "ListOpenCustomerEntries", args, "erp_bc.finance.list_open_customer_entries");
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
    Description = "Search Business Central vendors whose name contains the given text. Use when you have a vendor's name but not their number (No.). Omit nameContains to browse the first page of vendors. Returns up to a configurable number of matches.",
    Sensitivity = "read")]
public class FindVendors : ToolHandler<FindVendors.Args, FindVendors.Result>
{
    public class Args
    {
        [Description("Optional. Text to search for within the vendor name (case-insensitive substring). Leave empty to list vendors without filtering.")]
        public string NameContains { get; set; } = "";

        [Description("Maximum number of matches to return (1-50). Defaults to 10.")]
        public int Top { get; set; } = 10;
    }

    public class Match
    {
        [Description("Vendor number (No.).")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string Name { get; set; } = "";

        [Description("City from the vendor's address.")]
        public string City { get; set; } = "";

        [Description("Outstanding balance owed to the vendor, in the company's local currency (LCY).")]
        public decimal BalanceLcy { get; set; }

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
            "FindVendors", args, "erp_bc.purchasing.find_vendors");
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
            "GetVendor", args, "erp_bc.purchasing.get_vendor");
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

        return await BcClient.ExecuteAsync<Result>(
            "CreatePurchaseOrder", args, "erp_bc.purchasing.create_purchase_order");
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

        return await BcClient.ExecuteAsync<Result>(
            "AddPurchaseOrderLine", args, "erp_bc.purchasing.add_purchase_order_line");
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
            "GetPurchaseOrder", args, "erp_bc.purchasing.get_purchase_order");
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

        return await BcClient.ExecuteAsync<Result>(
            "CreateTransferOrder", args, "erp_bc.inventory.create_transfer_order");
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

        return await BcClient.ExecuteAsync<Result>(
            "AddTransferOrderLine", args, "erp_bc.inventory.add_transfer_order_line");
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
            "GetTransferOrder", args, "erp_bc.inventory.get_transfer_order");
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
        [Description("Optional source location code to filter by. Leave empty to include all.")]
        public string TransferFromCode { get; set; } = "";

        [Description("Optional destination location code to filter by. Leave empty to include all.")]
        public string TransferToCode { get; set; } = "";

        [Description("Maximum number of matches to return (1-50). Defaults to 10.")]
        public int Top { get; set; } = 10;
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
            "FindTransferOrders", args, "erp_bc.inventory.find_transfer_orders");
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

        return await BcClient.ExecuteAsync<Result>(
            "PostTransferOrder", args, "erp_bc.inventory.post_transfer_order");
    }
}
