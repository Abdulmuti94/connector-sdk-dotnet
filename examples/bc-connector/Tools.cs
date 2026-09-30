using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        [Description("Portion of the balance already past its due date, in LCY — relevant for credit checks before taking an order.")]
        public decimal BalanceDueLcy { get; set; }

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

        [Description("Portion of the balance already past its due date, in LCY, when resolved — relevant for credit checks.")]
        public decimal BalanceDueLcy { get; set; }

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
    Description = "Look up a Business Central item by item number (No.) OR by BARCODE (scanned/typed, e.g. 151-094000-03 or an EAN digit string). Returns description, unit price, quantity on hand, units of measure with conversion quantities (e.g. dozen = 12), the item's barcodes, and audit info. When resolved via barcode, matchedBarcode shows which unit/variant that barcode identifies — a barcode can be for a dozen rather than a piece, which changes price and quantity. An unknown itemNo automatically falls back to a barcode lookup.",
    Sensitivity = "read")]
public class GetItem : ToolHandler<GetItem.Args, GetItem.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Business Central item number, e.g. 1000 or ITEM-001. Provide this OR barcode. An unknown value is retried as a barcode automatically.")]
        public string ItemNo { get; set; } = "";

        [Description("Item barcode (LSC Barcodes 'Barcode No.'), e.g. from a scanner or package. Provide this OR itemNo.")]
        public string Barcode { get; set; } = "";
    }

    public class MatchedBarcodeInfo
    {
        [Description("The barcode that resolved to this item.")]
        public string BarcodeNo { get; set; } = "";

        [Description("Variant the barcode identifies, when variant-specific.")]
        public string VariantCode { get; set; } = "";

        [Description("Unit of measure the barcode identifies (the item's base unit when the barcode has none).")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("How many base units this barcode's unit contains (e.g. 12 for a dozen). Multiply by unitPrice per base unit where relevant.")]
        public decimal QtyPerUnitOfMeasure { get; set; }

        [Description("Description on the barcode record.")]
        public string Description { get; set; } = "";
    }

    public class UomRow
    {
        [Description("Unit of measure code, e.g. حبة, درزن, BOX.")]
        public string Code { get; set; } = "";

        [Description("How many base units one of this unit contains.")]
        public decimal QtyPerUnitOfMeasure { get; set; }
    }

    public class BarcodeRow
    {
        [Description("Barcode number.")]
        public string BarcodeNo { get; set; } = "";

        [Description("Variant the barcode identifies, when variant-specific.")]
        public string VariantCode { get; set; } = "";

        [Description("Unit of measure the barcode identifies; empty means the base unit.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Whether this is the barcode shown for the item in LS Central.")]
        public bool ShowForItem { get; set; }
    }

    public class Result
    {
        [Description("Item number (No.).")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Secondary description line, when the item has one.")]
        public string Description2 { get; set; } = "";

        [Description(BcToolDescriptions.ItemType)]
        public string Type { get; set; } = "";

        [Description("Base unit of measure, e.g. PCS or BOX.")]
        public string BaseUnitOfMeasure { get; set; } = "";

        [Description("Unit of measure used by default on sales documents.")]
        public string SalesUnitOfMeasure { get; set; } = "";

        [Description("Unit of measure used by default on purchase documents.")]
        public string PurchUnitOfMeasure { get; set; } = "";

        [Description("Item category code — the department the item belongs to, e.g. CAT17. It also names the number series the item was numbered from.")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("LS Central retail product group the item is filed under, e.g. LOC777. Echo this back to create_item to place a new item in the same group.")]
        public string RetailProductGroupCode { get; set; } = "";

        [Description("Description of the retail product group.")]
        public string RetailProductGroupDescription { get; set; } = "";

        [Description("Unit price in the company's local currency, EXCLUDING VAT.")]
        public decimal UnitPrice { get; set; }

        [Description("Current unit cost in the company's local currency.")]
        public decimal UnitCost { get; set; }

        [Description("Cost of the most recent purchase, in the company's local currency.")]
        public decimal LastDirectCost { get; set; }

        [Description("Costing method: FIFO, LIFO, Specific, Average, or Standard.")]
        public string CostingMethod { get; set; } = "";

        [Description("Primary vendor number for the item.")]
        public string VendorNo { get; set; } = "";

        [Description("The vendor's own number for this item.")]
        public string VendorItemNo { get; set; } = "";

        [Description("GTIN (global trade item number), when set.")]
        public string Gtin { get; set; } = "";

        [Description("Gross weight per base unit.")]
        public decimal GrossWeight { get; set; }

        [Description("Net weight per base unit.")]
        public decimal NetWeight { get; set; }

        [Description("General product posting group — drives which G/L accounts postings hit.")]
        public string GenProdPostingGroup { get; set; } = "";

        [Description("VAT product posting group, e.g. VAT15.")]
        public string VatProdPostingGroup { get; set; } = "";

        [Description("Inventory posting group.")]
        public string InventoryPostingGroup { get; set; } = "";

        [Description("Quantity currently on hand across all locations, in base units.")]
        public decimal Inventory { get; set; }

        [Description("Quantity committed on open sales orders, in base units.")]
        public decimal QtyOnSalesOrder { get; set; }

        [Description("Quantity expected on open purchase orders, in base units.")]
        public decimal QtyOnPurchOrder { get; set; }

        [Description("Whether the item is blocked entirely — a blocked item cannot be used on any document.")]
        public bool Blocked { get; set; }

        [Description("Whether the item is blocked for SALES specifically. Items created by create_item start sales-blocked until a person releases them.")]
        public bool SalesBlocked { get; set; }

        [Description("Whether the item is blocked for PURCHASING specifically.")]
        public bool PurchasingBlocked { get; set; }

        [Description("Present when the item was resolved via a barcode — the unit/variant that barcode identifies.")]
        public MatchedBarcodeInfo? MatchedBarcode { get; set; }

        [Description("The item's unit-of-measure conversions (e.g. dozen = 12 base units).")]
        public List<UomRow> UnitsOfMeasure { get; set; } = new();

        [Description("Total number of barcodes on the item.")]
        public int BarcodeCount { get; set; }

        [Description("True when the item has more than 50 barcodes and the list below is truncated.")]
        public bool BarcodesTruncated { get; set; }

        [Description("The item's barcodes (up to 50), each with its unit/variant.")]
        public List<BarcodeRow> Barcodes { get; set; } = new();

        [Description("Date the item was created (yyyy-MM-dd), from LS Central 'Date Created'.")]
        public string DateCreated { get; set; } = "";

        [Description("User who created the item ('Created by User').")]
        public string CreatedByUser { get; set; } = "";

        [Description("Date the item was last modified (yyyy-MM-dd), 'Last Date Modified'.")]
        public string LastDateModified { get; set; } = "";

        [Description("User who last modified the item ('Last Modified by User').")]
        public string LastModifiedByUser { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.ItemNo) && string.IsNullOrWhiteSpace(args.Barcode))
            throw new ToolValidationException(
                "erp_bc.inventory.get_item", "itemNo or barcode is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetItem", args, "erp_bc.inventory.get_item", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.get_item_bom
// ---------------------------------------------------------------------------

/// <summary>
/// Explodes an item into its bill-of-materials components. Model-agnostic: uses the
/// item's Production BOM when it has one, otherwise its Assembly BOM (BOM Component).
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.get_item_bom",
    Description = "Get the bill of materials (BOM) for an item — the components it is made of. Model-agnostic: returns the item's Production BOM if it has a 'Production BOM No.', otherwise its Assembly BOM (kit/set/basket components). By default returns direct components only; pass explode=true (or maxLevels>1) to expand sub-assemblies into a multi-level tree (each component may have its own nested 'components'). bomType is Assembly, Production, or None. Read-only.",
    Sensitivity = "read")]
public class GetItemBom : ToolHandler<GetItemBom.Args, GetItemBom.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Business Central item number (No.) whose BOM to explode.")]
        public string ItemNo { get; set; } = "";

        [Description("Optional. Expand the full multi-level BOM tree (sub-assemblies within sub-assemblies). Equivalent to a large maxLevels. Default false = direct components only.")]
        public bool Explode { get; set; }

        [Description("Optional recursion depth: 1 = direct components only (default), higher expands sub-assemblies. Capped at 20.")]
        public int? MaxLevels { get; set; }
    }

    public class Component
    {
        [Description("Component type: Item, Resource, or Production BOM.")]
        public string Type { get; set; } = "";

        [Description("Component item (or sub-BOM) number.")]
        public string No { get; set; } = "";

        [Description("Component description.")]
        public string Description { get; set; } = "";

        [Description("Quantity of this component per one unit of its immediate parent.")]
        public decimal QuantityPer { get; set; }

        [Description("Component unit of measure code.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Variant code when the component is variant-specific.")]
        public string VariantCode { get; set; } = "";

        [Description("BOM level: 1 = direct component of the requested item, 2 = component of a sub-assembly, etc.")]
        public int Level { get; set; }

        [Description("Nested sub-components, present when this component is itself a BOM item and maxLevels allowed deeper expansion.")]
        public List<Component> Components { get; set; } = new();
    }

    public class Result
    {
        [Description("Item number the BOM was exploded for.")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Base unit of measure of the item.")]
        public string BaseUnitOfMeasure { get; set; } = "";

        [Description("Which BOM model was used: Assembly, Production, or None (the item has no BOM — it is not manufactured/assembled).")]
        public string BomType { get; set; } = "";

        [Description("Production BOM number when bomType is Production; empty otherwise.")]
        public string ProductionBomNo { get; set; } = "";

        [Description("Recursion depth actually applied (1 = direct only).")]
        public int MaxLevels { get; set; }

        [Description("Total number of component nodes returned across all levels.")]
        public int ComponentCount { get; set; }

        [Description("True if the node cap was hit and the tree is incomplete.")]
        public bool Truncated { get; set; }

        [Description("The components, as a tree. Top-level entries are direct components (level 1); nested 'components' hold deeper levels when exploded.")]
        public List<Component> Components { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.ItemNo))
            throw new ToolValidationException(
                "erp_bc.inventory.get_item_bom", "itemNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetItemBom", args, "erp_bc.inventory.get_item_bom", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.where_used
// ---------------------------------------------------------------------------

/// <summary>
/// Direct (single-level) where-used: the parent items / BOMs that consume a component.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.where_used",
    Description = "Find where an item is used as a component — the DIRECT (single-level) parents that consume it. Returns assemblyParents (items whose Assembly BOM lists it, with quantity per) and productionParents (production BOMs that list it, each with the items that use that BOM). Use this for 'which products contain item X'. Read-only.",
    Sensitivity = "read")]
public class WhereUsed : ToolHandler<WhereUsed.Args, WhereUsed.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Business Central item number (No.) of the COMPONENT to trace upward.")]
        public string ItemNo { get; set; } = "";

        [Description("Optional. Max parents to return per kind (default 100, max 500).")]
        public int? Top { get; set; }
    }

    public class AssemblyParent
    {
        [Description("Parent item number whose assembly BOM lists the component.")]
        public string ParentItemNo { get; set; } = "";

        [Description("Parent item description.")]
        public string ParentDescription { get; set; } = "";

        [Description("Quantity of the component per one unit of this parent.")]
        public decimal QuantityPer { get; set; }

        [Description("Unit of measure code on the BOM line.")]
        public string UnitOfMeasureCode { get; set; } = "";
    }

    public class UsedByItem
    {
        [Description("Item number that uses this production BOM.")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";
    }

    public class ProductionParent
    {
        [Description("Production BOM number that lists the component.")]
        public string ProductionBomNo { get; set; } = "";

        [Description("BOM version code the line belongs to (empty for the base version).")]
        public string VersionCode { get; set; } = "";

        [Description("Quantity of the component per one unit produced.")]
        public decimal QuantityPer { get; set; }

        [Description("Unit of measure code on the BOM line.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Items that use this production BOM (i.e. have it as their Production BOM No.).")]
        public List<UsedByItem> UsedByItems { get; set; } = new();
    }

    public class Result
    {
        [Description("Component item number that was traced.")]
        public string ItemNo { get; set; } = "";

        [Description("Component description.")]
        public string Description { get; set; } = "";

        [Description("Always true: this is a direct (single-level) where-used, not a full upward explosion.")]
        public bool DirectOnly { get; set; }

        [Description("Number of assembly parents returned.")]
        public int AssemblyParentCount { get; set; }

        [Description("Number of production BOMs returned.")]
        public int ProductionBomCount { get; set; }

        [Description("Items whose Assembly BOM directly lists this component.")]
        public List<AssemblyParent> AssemblyParents { get; set; } = new();

        [Description("Production BOMs that directly list this component, each with the items that use that BOM.")]
        public List<ProductionParent> ProductionParents { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.ItemNo))
            throw new ToolValidationException(
                "erp_bc.inventory.where_used", "itemNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "WhereUsed", args, "erp_bc.inventory.where_used", ctx);
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

        [Description("Item number when known. Provide this OR barcode OR descriptionContains. An unknown value is retried as a barcode automatically.")]
        public string ItemNo { get; set; } = "";

        [Description("Item barcode (scanned/typed, e.g. an EAN digit string). A long digit string from the user is usually a barcode, not an item number.")]
        public string Barcode { get; set; } = "";

        [Description("Partial description when neither number nor barcode is known.")]
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

        [Description(BcToolDescriptions.ItemType)]
        public string Type { get; set; } = "";

        [Description("Item category code — the department the item belongs to, e.g. CAT17.")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("Unit price in LCY, excluding VAT.")]
        public decimal UnitPrice { get; set; }

        [Description("Whether the item is blocked.")]
        public bool Blocked { get; set; }
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

        [Description(BcToolDescriptions.ItemType)]
        public string Type { get; set; } = "";

        [Description("Base unit of measure when resolved.")]
        public string BaseUnitOfMeasure { get; set; } = "";

        [Description("Item category code when resolved — the department, e.g. CAT17.")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("LS Central retail product group when resolved, e.g. LOC777.")]
        public string RetailProductGroupCode { get; set; } = "";

        [Description("Unit price when resolved, excluding VAT.")]
        public decimal UnitPrice { get; set; }

        [Description("Unit cost when resolved.")]
        public decimal UnitCost { get; set; }

        [Description("Primary vendor number when resolved.")]
        public string VendorNo { get; set; } = "";

        [Description("On-hand inventory when resolved, in base units.")]
        public decimal Inventory { get; set; }

        [Description("Whether the item is blocked entirely.")]
        public bool Blocked { get; set; }

        [Description("Whether the item is blocked for sales specifically.")]
        public bool SalesBlocked { get; set; }

        [Description("Present when resolved via a barcode — the unit/variant that barcode identifies (e.g. a dozen, qtyPerUnitOfMeasure 12).")]
        public GetItem.MatchedBarcodeInfo? MatchedBarcode { get; set; }

        [Description("Unit-of-measure conversions when resolved (e.g. dozen = 12 base units).")]
        public List<GetItem.UomRow> UnitsOfMeasure { get; set; } = new();

        [Description("Short candidate list when resolved is false.")]
        public List<Candidate> Items { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.ItemNo) && string.IsNullOrWhiteSpace(args.Barcode) &&
            string.IsNullOrWhiteSpace(args.DescriptionContains))
            throw new ToolValidationException(
                "erp_bc.inventory.lookup_item", "itemNo, barcode, or descriptionContains is required.");

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
    Description = "Search customers by partial name (or browse all when nameContains is empty) — returns a DATASET: you see a sample of the rows plus a dataset_ref, and the full set can be exported or analyzed on demand; never page manually. Prefer lookup_customer for single-customer questions (one round-trip).",
    Sensitivity = "read")]
public class FindCustomers : PaginatedToolHandler<FindCustomers.Args, FindCustomers.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the customer name (case-insensitive substring). Leave empty to list customers without filtering.")]
        public string NameContains { get; set; } = "";

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

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Customers { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.sales.find_customers";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindCustomers", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindCustomers", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Customers,
            NextCursor = page.HasMore ? (offset + page.Customers.Count).ToString() : null,
            Total      = null,
        };
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
    Description = "Look up an existing Business Central sales order by its order number (No.). Returns the customer, status, order date, currency, totals, and ALL order lines (item, quantity, price, shipped/invoiced so far). Use this to answer 'what is on order X' and to check posting progress.",
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

        [Description("Quantity ordered.")]
        public decimal Quantity { get; set; }

        [Description("Unit of measure code.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Location the line ships from, when set.")]
        public string LocationCode { get; set; } = "";

        [Description("Unit price in the order's currency.")]
        public decimal UnitPrice { get; set; }

        [Description("Line discount percentage.")]
        public decimal LineDiscountPct { get; set; }

        [Description("Line amount excluding VAT, in the order's currency.")]
        public decimal LineAmount { get; set; }

        [Description("Line amount including VAT, in the order's currency.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Quantity not yet shipped.")]
        public decimal OutstandingQuantity { get; set; }

        [Description("Quantity already shipped.")]
        public decimal QuantityShipped { get; set; }

        [Description("Quantity already invoiced.")]
        public decimal QuantityInvoiced { get; set; }
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

        [Description("Total order amount excluding VAT, in the order's currency.")]
        public decimal Amount { get; set; }

        [Description("Total order amount including VAT, in the order's currency.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Number of order lines.")]
        public int LineCount { get; set; }

        [Description("The order lines.")]
        public List<Line> Lines { get; set; } = new();
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
// bc.sales.find_sales_orders
// ---------------------------------------------------------------------------

/// <summary>
/// Browses open (unposted) sales orders by customer, status, date range, or
/// number substring — newest first.
/// </summary>
[Tool(
    Key         = "erp_bc.sales.find_sales_orders",
    Description = "List UNPOSTED sales orders, newest first, as a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Filter by customerNo ('what open orders does customer X have'), status (Open, Released, Pending Approval, Pending Prepayment), order-date range, or order/external document number substring. Rows carry currencyCode + amountIncludingVat. Use get_sales_order for one order's lines; posted invoices are a different document (Finance agent).",
    Sensitivity = "read")]
public class FindSalesOrders : PaginatedToolHandler<FindSalesOrders.Args, FindSalesOrders.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Customer number (No.) to filter by.")]
        public string CustomerNo { get; set; } = "";

        [Description("Optional. Document status: Open, Released, Pending Approval, or Pending Prepayment.")]
        public string Status { get; set; } = "";

        [Description("Optional. Filter orders whose number contains this text.")]
        public string OrderNoContains { get; set; } = "";

        [Description("Optional. Filter orders whose external document number (customer PO) contains this text.")]
        public string ExternalDocumentNoContains { get; set; } = "";

        [Description("Optional. Earliest order date (yyyy-MM-dd), inclusive.")]
        public string FromOrderDate { get; set; } = "";

        [Description("Optional. Latest order date (yyyy-MM-dd), inclusive.")]
        public string ToOrderDate { get; set; } = "";
    }

    public class Match
    {
        [Description("Sales order number (No.).")]
        public string OrderNo { get; set; } = "";

        [Description("Customer number.")]
        public string CustomerNo { get; set; } = "";

        [Description("Customer name.")]
        public string CustomerName { get; set; } = "";

        [Description("Document status.")]
        public string Status { get; set; } = "";

        [Description("Order date (yyyy-MM-dd).")]
        public string OrderDate { get; set; } = "";

        [Description("Customer's external document number (their PO).")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("Header location code, when set.")]
        public string LocationCode { get; set; } = "";

        [Description("Order currency; empty means the company's local currency. Always state it with the amount.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total order amount including VAT, in the order's currency.")]
        public decimal AmountIncludingVat { get; set; }
    }

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Orders { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.sales.find_sales_orders";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindSalesOrders", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindSalesOrders", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Orders,
            NextCursor = page.HasMore ? (offset + page.Orders.Count).ToString() : null,
            Total      = null,
        };
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
    Sensitivity = "destructive")]
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

        [Description("Posted sales shipment number generated by this posting (present when a shipment was posted). Always report this to the user.")]
        public string PostedShipmentNo { get; set; } = "";

        [Description("Posted sales invoice number generated by this posting (present when an invoice was posted). Always report this to the user.")]
        public string PostedInvoiceNo { get; set; } = "";

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
    Description = "Search items by partial description (or browse all when descriptionContains is empty) — returns a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Prefer lookup_item for single-item questions (one round-trip).",
    Sensitivity = "read")]
public class FindItems : PaginatedToolHandler<FindItems.Args, FindItems.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the item description (case-insensitive substring). Leave empty to list items without filtering.")]
        public string DescriptionContains { get; set; } = "";

        [Description(BcToolDescriptions.IncludeInventory)]
        public bool IncludeInventory { get; set; }
    }

    public class Match
    {
        [Description("Item number (No.).")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description(BcToolDescriptions.ItemType)]
        public string Type { get; set; } = "";

        [Description("Base unit of measure.")]
        public string BaseUnitOfMeasure { get; set; } = "";

        [Description("Item category code — the department the item belongs to, e.g. CAT17.")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("Unit price in the company's local currency, EXCLUDING VAT.")]
        public decimal UnitPrice { get; set; }

        [Description("Current unit cost in the company's local currency.")]
        public decimal UnitCost { get; set; }

        [Description("On-hand quantity. Only set when includeInventory was true.")]
        public decimal Inventory { get; set; }

        [Description("Whether the item is blocked — a blocked item cannot be used on documents. Do not offer blocked items without saying so.")]
        public bool Blocked { get; set; }
    }

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Items { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.inventory.find_items";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindItems", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindItems", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Items,
            NextCursor = page.HasMore ? (offset + page.Items.Count).ToString() : null,
            Total      = null,
        };
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
/// Can be broken down per item × store, or rolled up by item, store, or category,
/// for one item (itemNo) or a batch of them (itemNos) in a single call.
/// Paginated dataset: the gateway walks its full result buffer every call (totals
/// and the qualifying-row count are exact) and emits only the cursor's skip/top
/// window, in the buffer's stable primary-key order.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.net_inventory",
    Description = "Net (available) store inventory per LS Central's Inventory Lookup: physical inventory adjusted for unposted/posted POS sales, inventory adjustments, and click & collect reservations. Returns a dataset — a sample of rows plus a dataset_ref; the full set can be exported/analyzed on demand, so never page manually. Use groupBy to choose the breakdown — detail (per item per store), item, store, or category — and scope with itemNo (one item), itemNos (an explicit list, up to 500 items in ONE call — use this to compare several items instead of calling the tool once per item), descriptionContains or itemCategoryCode (FILTERS, not lists: they cover ANY number of matching items, so use one of them rather than a long itemNos list whenever the question is 'all items that ...'), storeNo (one store), storeNos (up to 50 stores in ONE call — use this to compare branches instead of calling the tool once per store), or locationCode. Row-level breakdowns (detail/item/store) need at least one of those filters. For a single grand total use groupBy 'total' (one row). Only Type = Inventory items are included — Service and Non-Inventory items have no real on-hand quantity and are excluded, even when scoped to one of them by itemNo (returns no rows). groupBy 'total' collapses the ENTIRE scope into ONE row — it never breaks down per store, so several stores with groupBy 'total' come back as one combined figure; per-branch rows need groupBy 'store'. COST: a call costs roughly (stores in scope) x (items in scope). An all-items total for ONE branch already takes ~20s, so the same question over many branches cannot be served by one call — narrow the ITEM scope (itemCategoryCode, itemNos, descriptionContains) instead. The ONE exception is a branch rollup with no item scope ('stock per branch', 'total for every branch'): that is served from a precomputed snapshot and returns in milliseconds, with source='snapshot' and asOf on every row \u2014 state that time in the answer. Never split any of this into one call per store. CHOOSING THE SHAPE, which matters more than any other choice here: scope by STORE and filter the items afterwards, never the reverse. 'Stock of these 20,000 items at store S004' is ONE call \u2014 storeNo='S004' with groupBy 'detail', which returns every item at that store \u2014 and then the item list is applied to the returned dataset, NOT sent to BC. Caps per call are 500 itemNos and 50 storeNos; needing more than that is the signal to switch to a filter (itemCategoryCode, descriptionContains) or to a store scope, never to a second call. A detail-grain request whose upper bound (items x stores) cannot be delivered as one dataset is refused up front with the alternatives named \u2014 read that message and pick one rather than retrying.",
    Sensitivity = "read",
    // Measured: an all-items aggregate for a single store runs 15-30s (conversation
    // 3897 / run 01M342RN1HXCRYVXRB59V7R70Q: 92 such calls, 29m01s, five of them lost
    // on the 30s ceiling and retried). The ceiling, not the work, was what failed
    // those calls. NOTE this is only half the limit: BcClient's own HttpClient timeout
    // (BC_TIMEOUT_SECONDS) bounds the same call from the connector side, so its default
    // moved to 120 with this — a deployment that pins BC_TIMEOUT_SECONDS lower still
    // cuts these calls off at that value, whatever this says.
    DefaultDeadlineMs = 120_000,
    // Shared across every agent, for the same reason run_sql is (BcSqlTools.cs).
    // "Do we have stock?" is not an inventory-agent question — it is the question
    // sales asks before promising an order, purchasing asks before reordering,
    // retail asks about a branch, and service asks about a spare part. Binding by
    // key namespace meant only erp_bc.inventory could ask it, and the alternative
    // was duplicate handler classes under five more namespaces.
    //
    // The key deliberately does NOT move. Renaming it to erp_bc.shared.* would
    // deprecate the existing Tool row and drop any grants or admin-set sensitivity
    // keyed on it; the namespace just stops implying exclusive ownership.
    //
    // Every agent prompt that gained it also gained BcSharedInstructions
    // .NetInventory, because a tool an agent has but was never told about is how
    // groupBy=detail-without-a-filter timeouts happen.
    Agents      = new[] { "*" })]
public class GetNetInventory : PaginatedToolHandler<GetNetInventory.Args, GetNetInventory.Row>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Single item number to scope to, e.g. CAT01-000001. For several items use itemNos instead of repeating the call.")]
        public string ItemNo { get; set; } = "";

        [Description("Item numbers to scope to, e.g. [\"CAT01-000001\", \"CAT01-000002\"] — at most 500 per call, covered in one request. Combines with itemNo. An unknown number fails the call rather than returning silently empty. For more items than that, or for 'every item that ...', use descriptionContains or itemCategoryCode instead — those are filters and are not capped.")]
        [JsonConverter(typeof(TolerantListConverter<string>))]
        public List<string> ItemNos { get; set; } = new();

        [Description("Partial item description. A FILTER, not a list: it covers every Inventory-type item whose description matches, with no cap on how many — use it for 'all items that ...' questions over a large catalogue. Errors only when nothing matches. Use itemNo/itemNos instead when the numbers are known.")]
        public string DescriptionContains { get; set; } = "";

        [Description("Optional. Restrict to items in this item category code. A FILTER, not a list — it covers the whole category however many items that is.")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("Single LS Central store number to scope to, e.g. S004. For several stores use storeNos instead of repeating the call.")]
        public string StoreNo { get; set; } = "";

        [Description("Store numbers to scope to, e.g. [\"S004\", \"S011\"] \u2014 at most 50 per call, covered in one request. Combines with storeNo. Use this to compare branches instead of calling the tool once per store. An unknown number fails the call rather than returning silently empty.")]
        [JsonConverter(typeof(TolerantListConverter<string>))]
        public List<string> StoreNos { get; set; } = new();

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

        [Description("Where the figure may come from. auto (default) serves wide branch rollups from the precomputed snapshot and everything else live. 'live' forces a fresh computation \u2014 slower, and for a branch-by-branch total over every item it will exceed the time limit. 'snapshot' demands the precomputed figure and fails rather than silently computing live. The snapshot only holds store and store x category totals, so any item, description or variant scope is answered live whatever this says. Every row states which it was, and a snapshot row carries asOf.")]
        public string Source { get; set; } = "";
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

        [Description("How this figure was produced: 'live' (computed from the ledger during this call) or 'snapshot' (read from the precomputed per-store snapshot).")]
        public string Source { get; set; } = "";

        [Description("For a snapshot row, when the figure was true \u2014 the moment its rebuild started. ALWAYS state it when reporting a snapshot figure (\"as of 03:00\"); stock has moved since. Empty on a live row, which is current as of now.")]
        public string AsOf { get; set; } = "";
    }

    // Wire shape of the gateway GetNetInventory response. `skip` is nullable to detect
    // a pre-paging AL build (see BcPaging.RequireSkipEcho); `totalRows` is the exact
    // qualifying-row count — the gateway always walks its full buffer — surfaced as the
    // dataset Total.
    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public long? TotalRows { get; set; }

        /// <summary>True once the gateway has stored this result set for cheap paging.</summary>
        public bool Materialized { get; set; }

        public List<Row> Rows { get; set; } = new();
    }

    public override async Task<DatasetPage<Row>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.inventory.net_inventory";

        var hasScope =
            !string.IsNullOrWhiteSpace(args.ItemNo) ||
            args.ItemNos.Any(n => !string.IsNullOrWhiteSpace(n)) ||
            !string.IsNullOrWhiteSpace(args.DescriptionContains) ||
            !string.IsNullOrWhiteSpace(args.ItemCategoryCode) ||
            !string.IsNullOrWhiteSpace(args.StoreNo) ||
            args.StoreNos.Any(n => !string.IsNullOrWhiteSpace(n)) ||
            !string.IsNullOrWhiteSpace(args.LocationCode);

        // Row-level breakdowns can return the whole catalogue × every store, so the
        // gateway requires a narrowing filter for them; category/total may run open.
        var groupBy = (args.GroupBy ?? "").Trim().ToLowerInvariant();
        var rowLevel = groupBy is "" or "detail" or "item" or "store" or "location"
            or "itemstore" or "item-store" or "byitem" or "bystore";
        if (!hasScope && rowLevel)
            throw new ToolValidationException(
                ToolKey,
                "Provide itemNo, itemNos, descriptionContains, itemCategoryCode, storeNo, storeNos, or " +
                "locationCode (or use groupBy 'category' or 'total').");

        // PageSize 0 is the model's own call, and the hub keeps only BcPaging.SampleRows
        // of whatever comes back. Asking for 200 had the gateway build and serialize 180
        // rows that were then thrown away on every such call.
        var pageSize = cursor.PageSize > 0 ? cursor.PageSize : BcPaging.SampleRows;

        // MATERIALISED PAGING. The gateway rebuilds its entire result buffer on every
        // call and emits one window of it, so a 30,000-row export used to recompute the
        // six component aggregates thirty times. Page 1 now carries a key: when the
        // result is bigger than a page the gateway stores it under that key, and every
        // later page is an indexed read. The key travels in the cursor, so a dataset
        // replay stays on the stored rows.
        var (cursorKey, offset) = BcPaging.ParseWindowCursor(cursor.Token, ToolKey);
        var materializeKey = cursorKey ?? Guid.NewGuid().ToString("N");

        var payload = BcPaging.WithPaging(args, pageSize, offset);
        payload["materializeKey"] = materializeKey;
        payload["maxRows"] = BcPaging.DatasetRowCeiling;

        var page = await BcClient.ExecuteAsync<GatewayPage>("GetNetInventory", payload, ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "GetNetInventory", ToolKey);

        var nextOffset = offset + page.Rows.Count;
        return new DatasetPage<Row>
        {
            Rows       = page.Rows,
            // Only carry the key once the gateway says it stored the rows; a small result
            // is cheaper to recompute than to write, and it reports materialized=false.
            NextCursor = page.HasMore
                ? (page.Materialized ? $"{materializeKey}|{nextOffset}" : nextOffset.ToString())
                : null,
            Total      = page.TotalRows,
        };
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.available_inventory
// ---------------------------------------------------------------------------

/// <summary>
/// Net inventory less the stock already committed to outbound transfer orders —
/// the figure the "Available Item By Location - ASG" report (report 52572 in
/// ASG--Customization) puts in front of a branch:
/// <code>AvailableQty := netInventory - (Σ Transfer Line.Quantity - Σ Quantity Shipped)</code>
/// over transfer lines leaving that location.
///
/// Deliberately NOT shared with every agent, unlike <see cref="GetNetInventory"/>.
/// "What is on hand" is a question any domain asks; "what may I commit" is a
/// stock-control decision, and answering it in the Sales or Retail agent would
/// invite promising away another branch's transfer.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.available_inventory",
    Description = BcToolDescriptions.AvailableInventory,
    Sensitivity = "read",
    // Same ceiling as net_inventory, and for the same reason: this does everything
    // net_inventory does plus a walk over the transfer lines in scope, so it is never
    // the faster of the two. See GetNetInventory's note.
    DefaultDeadlineMs = 120_000)]
public class GetAvailableInventory : PaginatedToolHandler<GetAvailableInventory.Args, GetAvailableInventory.Row>
{
    // Same inputs as net_inventory, down to the paging and grouping semantics, so
    // inheritance keeps the two from drifting. NJsonSchema flattens the hierarchy
    // (FlattenInheritanceHierarchy in the SDK's schema settings), so the generated
    // schema is one flat object exactly as if the properties were redeclared.
    public class Args : GetNetInventory.Args
    {
    }

    public class Row : GetNetInventory.Row
    {
        [Description("Quantity at this location already committed to OUTBOUND transfer orders and not yet shipped — physically on the shelf, but promised elsewhere. Subtracted from netInventory to give availableInventory.")]
        public decimal ToReservedQuantity { get; set; }

        [Description("The number to quote when asked what can be sent, sold or committed: netInventory minus toReservedQuantity. Can be negative when more is promised than is on hand — say so plainly rather than reporting zero.")]
        public decimal AvailableInventory { get; set; }

        [Description("Quantity on its way IN to this location on inbound transfer orders, not yet received. Reported for context only — it is NOT included in availableInventory, because it is not here yet.")]
        public decimal ToIncomingQuantity { get; set; }
    }

    // Adds the three availability totals to the net_inventory envelope. `skip` stays
    // nullable to detect a pre-paging AL build; `totalRows` is the exact qualifying
    // row count.
    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public long? TotalRows { get; set; }

        /// <summary>True once the gateway has stored this result set for cheap paging.</summary>
        public bool Materialized { get; set; }

        public List<Row> Rows { get; set; } = new();
    }

    public override async Task<DatasetPage<Row>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.inventory.available_inventory";

        var hasScope =
            !string.IsNullOrWhiteSpace(args.ItemNo) ||
            args.ItemNos.Any(n => !string.IsNullOrWhiteSpace(n)) ||
            !string.IsNullOrWhiteSpace(args.DescriptionContains) ||
            !string.IsNullOrWhiteSpace(args.ItemCategoryCode) ||
            !string.IsNullOrWhiteSpace(args.StoreNo) ||
            args.StoreNos.Any(n => !string.IsNullOrWhiteSpace(n)) ||
            !string.IsNullOrWhiteSpace(args.LocationCode);

        // Same rule as net_inventory: a row-level breakdown across the whole
        // catalogue × every store times out, so it needs a narrowing filter.
        var groupBy = (args.GroupBy ?? "").Trim().ToLowerInvariant();
        var rowLevel = groupBy is "" or "detail" or "item" or "store" or "location"
            or "itemstore" or "item-store" or "byitem" or "bystore";
        if (!hasScope && rowLevel)
            throw new ToolValidationException(
                ToolKey,
                "Provide itemNo, itemNos, descriptionContains, itemCategoryCode, storeNo, storeNos, or " +
                "locationCode (or use groupBy 'category' or 'total').");

        // PageSize 0 is the model's own call, and the hub keeps only BcPaging.SampleRows
        // of whatever comes back. Asking for 200 had the gateway build and serialize 180
        // rows that were then thrown away on every such call.
        var pageSize = cursor.PageSize > 0 ? cursor.PageSize : BcPaging.SampleRows;

        // MATERIALISED PAGING. The gateway rebuilds its entire result buffer on every
        // call and emits one window of it, so a 30,000-row export used to recompute the
        // six component aggregates thirty times. Page 1 now carries a key: when the
        // result is bigger than a page the gateway stores it under that key, and every
        // later page is an indexed read. The key travels in the cursor, so a dataset
        // replay stays on the stored rows.
        var (cursorKey, offset) = BcPaging.ParseWindowCursor(cursor.Token, ToolKey);
        var materializeKey = cursorKey ?? Guid.NewGuid().ToString("N");

        var payload = BcPaging.WithPaging(args, pageSize, offset);
        payload["materializeKey"] = materializeKey;
        payload["maxRows"] = BcPaging.DatasetRowCeiling;

        var page = await BcClient.ExecuteAsync<GatewayPage>("GetAvailableInventory", payload, ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "GetAvailableInventory", ToolKey);

        var nextOffset = offset + page.Rows.Count;
        return new DatasetPage<Row>
        {
            Rows       = page.Rows,
            // Only carry the key once the gateway says it stored the rows; a small result
            // is cheaper to recompute than to write, and it reports materialized=false.
            NextCursor = page.HasMore
                ? (page.Materialized ? $"{materializeKey}|{nextOffset}" : nextOffset.ToString())
                : null,
            Total      = page.TotalRows,
        };
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
    Description = "List Business Central warehouse locations with their code, name and whether they use BINS (binMandatory, binCount). Filter by exact code or partial name (nameContains); call with no filters to browse the full list. Use this to resolve a location code to its name, to enumerate available warehouses, or to check before a transfer whether the destination needs a bin on each line (then find_bins). Locations are the standard Business Central warehouse concept; for LS Central retail outlets use find_stores instead.",
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

        [Description("True when every transfer/receipt line at this location needs a bin (Bin Mandatory). Pass transferToBinCode / transferFromBinCode on transfer lines for such a location; find_bins lists its bins.")]
        public bool BinMandatory { get; set; }

        [Description("True for a directed put-away and pick warehouse — bins there are assigned by warehouse documents, not on transfer lines.")]
        public bool DirectedPutAwayAndPick { get; set; }

        [Description("Number of bins defined at the location.")]
        public int BinCount { get; set; }
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

        [Description("Posting description of the entry.")]
        public string Description { get; set; } = "";

        [Description("External document number, if any.")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Document date (yyyy-MM-dd).")]
        public string DocumentDate { get; set; } = "";

        [Description("Due date (yyyy-MM-dd); empty if not set.")]
        public string DueDate { get; set; } = "";

        [Description("Payment discount date (yyyy-MM-dd); empty if not set.")]
        public string PmtDiscountDate { get; set; } = "";

        [Description("Currency code of the entry; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Entry amount in the company's local currency (LCY).")]
        public decimal AmountLcy { get; set; }

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
    Description = "Search customers by partial name when you need a list before AR queries — returns a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Prefer lookup_customer for single-customer questions (one round-trip).",
    Sensitivity = "read")]
public class FinanceFindCustomers : PaginatedToolHandler<FinanceFindCustomers.Args, FinanceFindCustomers.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the customer name (case-insensitive substring). Leave empty to list customers without filtering.")]
        public string NameContains { get; set; } = "";

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

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Customers { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.finance.find_customers";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindCustomers", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindCustomers", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Customers,
            NextCursor = page.HasMore ? (offset + page.Customers.Count).ToString() : null,
            Total      = null,
        };
    }
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

        [Description("Portion of the balance already due (past its due date), in LCY.")]
        public decimal BalanceDueLcy { get; set; }

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

        [Description("Portion of the balance already due (past its due date), in LCY, when resolved.")]
        public decimal BalanceDueLcy { get; set; }

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
    Description = "Search vendors by partial name when you need a list before AP queries — returns a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Prefer lookup_vendor for single-vendor questions (one round-trip).",
    Sensitivity = "read")]
public class FinanceFindVendors : PaginatedToolHandler<FinanceFindVendors.Args, FinanceFindVendors.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the vendor name (case-insensitive substring). Leave empty to list vendors without filtering.")]
        public string NameContains { get; set; } = "";

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

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Vendors { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.finance.find_vendors";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindVendors", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindVendors", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Vendors,
            NextCursor = page.HasMore ? (offset + page.Vendors.Count).ToString() : null,
            Total      = null,
        };
    }
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

        [Description("Portion of the balance already due (past its due date), in LCY.")]
        public decimal BalanceDueLcy { get; set; }

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

        [Description("Portion of the balance already due (past its due date), in LCY, when resolved.")]
        public decimal BalanceDueLcy { get; set; }

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

        [Description("Posting description of the entry.")]
        public string Description { get; set; } = "";

        [Description("External document number, if any.")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Document date (yyyy-MM-dd).")]
        public string DocumentDate { get; set; } = "";

        [Description("Due date (yyyy-MM-dd); empty if not set.")]
        public string DueDate { get; set; } = "";

        [Description("Currency code of the entry; empty means LCY.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Entry amount in LCY.")]
        public decimal AmountLcy { get; set; }

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
// bc.finance.get_vendor_aging
// ---------------------------------------------------------------------------

/// <summary>
/// Aging-bucket summary for one vendor's open payables — the AP mirror of
/// <see cref="GetCustomerAging"/>. Amounts are reported positive (= owed).
/// </summary>
[Tool(
    Key         = "erp_bc.finance.get_vendor_aging",
    Description = "Get an aging summary for a vendor's open payables: Current, 1–30, 31–60, 61–90, and 90+ days past due (all in LCY, positive = amount we owe). Use for bucket summaries; use list_open_vendor_entries for line detail.",
    Sensitivity = "read")]
public class GetVendorAging : ToolHandler<GetVendorAging.Args, GetVendorAging.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Vendor number (No.) to age, e.g. V00010.")]
        public string VendorNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Vendor number (No.).")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor display name.")]
        public string VendorName { get; set; } = "";

        [Description("Open amount not yet due, in LCY (positive = owed).")]
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
        if (string.IsNullOrWhiteSpace(args.VendorNo))
            throw new ToolValidationException("erp_bc.finance.get_vendor_aging", "vendorNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetVendorAging", args, "erp_bc.finance.get_vendor_aging", ctx);
    }
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
    Description = "Search posted sales invoices. Filter by customer number, invoice number substring, posting date range, and/or unpaidOnly (invoices with a remaining balance). amountIncludingVat is in the invoice's currencyCode; remainingAmountLcy is in the company's local currency.",
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

        [Description("Optional. When true, return only invoices with a remaining unpaid balance. Default false (all invoices).")]
        public bool? UnpaidOnly { get; set; }

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

        [Description("Currency of amountIncludingVat; empty means the company's local currency. Always state it with the amount.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total amount including VAT, in currencyCode.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Remaining unpaid amount in LCY (0 when fully paid).")]
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
    Description = "Search posted purchase invoices. Filter by vendor number, invoice number substring, posting date range, and/or unpaidOnly (invoices with a remaining balance). amountIncludingVat is in the invoice's currencyCode; remainingAmountLcy is in the company's local currency.",
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

        [Description("Optional. When true, return only invoices with a remaining unpaid balance. Default false (all invoices).")]
        public bool? UnpaidOnly { get; set; }

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

        [Description("Currency of amountIncludingVat; empty means the company's local currency. Always state it with the amount.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total amount including VAT, in currencyCode.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Remaining unpaid amount in LCY (0 when fully paid).")]
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
    Description = "Search vendors by partial name (or browse all when nameContains is empty) — returns a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Prefer lookup_vendor for single-vendor questions (one round-trip).",
    Sensitivity = "read")]
public class FindVendors : PaginatedToolHandler<FindVendors.Args, FindVendors.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Text to search for within the vendor name (case-insensitive substring). Leave empty to list vendors without filtering.")]
        public string NameContains { get; set; } = "";

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

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Vendors { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.purchasing.find_vendors";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindVendors", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindVendors", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Vendors,
            NextCursor = page.HasMore ? (offset + page.Vendors.Count).ToString() : null,
            Total      = null,
        };
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

        [Description("Portion of the balance already past its due date, in LCY.")]
        public decimal BalanceDueLcy { get; set; }

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

        [Description("Portion of the balance already past its due date, in LCY, when resolved.")]
        public decimal BalanceDueLcy { get; set; }

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
    Description = "Look up an existing Business Central purchase order by its order number (No.). Returns the vendor, status, dates, currency, totals, and ALL order lines (item, quantity, cost, received/invoiced so far). Use this to answer 'what is on PO X' and to check receiving progress.",
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

        [Description("Quantity ordered.")]
        public decimal Quantity { get; set; }

        [Description("Unit of measure code.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Location the line is received into, when set.")]
        public string LocationCode { get; set; } = "";

        [Description("Direct unit cost in the order's currency.")]
        public decimal DirectUnitCost { get; set; }

        [Description("Line discount percentage.")]
        public decimal LineDiscountPct { get; set; }

        [Description("Line amount excluding VAT, in the order's currency.")]
        public decimal LineAmount { get; set; }

        [Description("Line amount including VAT, in the order's currency.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Quantity not yet received.")]
        public decimal OutstandingQuantity { get; set; }

        [Description("Quantity already received.")]
        public decimal QuantityReceived { get; set; }

        [Description("Quantity already invoiced.")]
        public decimal QuantityInvoiced { get; set; }
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

        [Description("Expected receipt date (yyyy-MM-dd); empty if not set.")]
        public string ExpectedReceiptDate { get; set; } = "";

        [Description("Vendor invoice number recorded on the order — required before posting an invoice.")]
        public string VendorInvoiceNo { get; set; } = "";

        [Description("Order currency code; empty means the company's local currency (LCY).")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total order amount excluding VAT, in the order's currency.")]
        public decimal Amount { get; set; }

        [Description("Total order amount including VAT, in the order's currency.")]
        public decimal AmountIncludingVat { get; set; }

        [Description("Number of order lines.")]
        public int LineCount { get; set; }

        [Description("The order lines.")]
        public List<Line> Lines { get; set; } = new();
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
// bc.purchasing.find_purchase_orders
// ---------------------------------------------------------------------------

/// <summary>
/// Browses open (unposted) purchase orders by vendor, status, date range, or
/// number substring — newest first. Buy-side mirror of <see cref="FindSalesOrders"/>.
/// </summary>
[Tool(
    Key         = "erp_bc.purchasing.find_purchase_orders",
    Description = "List UNPOSTED purchase orders, newest first, as a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Filter by vendorNo ('what open POs do we have with vendor X'), status (Open, Released, Pending Approval, Pending Prepayment), order-date or expected-receipt-date range ('POs arriving this week'), or order number substring. Rows carry currencyCode + amountIncludingVat. Use get_purchase_order for one order's lines; posted invoices are a different document (Finance agent).",
    Sensitivity = "read")]
public class FindPurchaseOrders : PaginatedToolHandler<FindPurchaseOrders.Args, FindPurchaseOrders.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Vendor number (No.) to filter by.")]
        public string VendorNo { get; set; } = "";

        [Description("Optional. Document status: Open, Released, Pending Approval, or Pending Prepayment.")]
        public string Status { get; set; } = "";

        [Description("Optional. Filter orders whose number contains this text.")]
        public string OrderNoContains { get; set; } = "";

        [Description("Optional. Earliest order date (yyyy-MM-dd), inclusive.")]
        public string FromOrderDate { get; set; } = "";

        [Description("Optional. Latest order date (yyyy-MM-dd), inclusive.")]
        public string ToOrderDate { get; set; } = "";

        [Description("Optional. Earliest expected receipt date (yyyy-MM-dd), inclusive — for 'POs arriving soon'.")]
        public string FromExpectedReceiptDate { get; set; } = "";

        [Description("Optional. Latest expected receipt date (yyyy-MM-dd), inclusive.")]
        public string ToExpectedReceiptDate { get; set; } = "";
    }

    public class Match
    {
        [Description("Purchase order number (No.).")]
        public string OrderNo { get; set; } = "";

        [Description("Vendor number.")]
        public string VendorNo { get; set; } = "";

        [Description("Vendor name.")]
        public string VendorName { get; set; } = "";

        [Description("Document status.")]
        public string Status { get; set; } = "";

        [Description("Order date (yyyy-MM-dd).")]
        public string OrderDate { get; set; } = "";

        [Description("Expected receipt date (yyyy-MM-dd).")]
        public string ExpectedReceiptDate { get; set; } = "";

        [Description("Vendor invoice number on the order, when recorded.")]
        public string VendorInvoiceNo { get; set; } = "";

        [Description("Header location code, when set.")]
        public string LocationCode { get; set; } = "";

        [Description("Order currency; empty means the company's local currency. Always state it with the amount.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total order amount including VAT, in the order's currency.")]
        public decimal AmountIncludingVat { get; set; }
    }

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Orders { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.purchasing.find_purchase_orders";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindPurchaseOrders", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindPurchaseOrders", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Orders,
            NextCursor = page.HasMore ? (offset + page.Orders.Count).ToString() : null,
            Total      = null,
        };
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
    Description = "Post a Business Central purchase order. postType 'Receive' posts a receipt, 'Invoice' posts the invoice, and 'ReceiveAndInvoice' (default) does both. Releases the order first when it is still Open. IMPORTANT: posting an invoice requires the vendor's invoice number on the order — pass vendorInvoiceNo here if it was not set at creation, or post with postType 'Receive' only.",
    Sensitivity = "destructive")]
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

        [Description("The vendor's invoice number — REQUIRED (here or already on the order) when posting an invoice. Ask the user for it rather than inventing one.")]
        public string VendorInvoiceNo { get; set; } = "";

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

        [Description("Posted purchase receipt number generated by this posting (present when goods were received). Always report this to the user.")]
        public string PostedReceiptNo { get; set; } = "";

        [Description("Posted purchase invoice number generated by this posting (present when an invoice was posted). Always report this to the user.")]
        public string PostedInvoiceNo { get; set; } = "";

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
    Description = "Create a Business Central transfer order header to move inventory between two locations via an in-transit location. The order has no lines until you add them with add_transfer_order_line. The header is stamped with the REGION/BRANCH dimension (Shortcut Dimension 1 Code, e.g. 1100-S068) of the SOURCE branch, taken from the source store's card, and with storeFrom / storeTo, which LS Central uses to post the shipment to the source branch and the receipt to the destination branch. Only when the source is not a store's location (MK-PLACE, HO …) must you pass shortcutDimension1Code — creation is refused without a region/branch. Tell the user which region/branch the order carries.",
    Sensitivity = "write")]
public class CreateTransferOrder : ToolHandler<CreateTransferOrder.Args, CreateTransferOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Source location code stock is transferred from (e.g. L002 main warehouse, L004, MDA01).")]
        public string TransferFromCode { get; set; } = "";

        [Description("Destination location code stock is transferred to (e.g. L004, L027, MK-PLACE).")]
        public string TransferToCode { get; set; } = "";

        [Description("In-transit location code that holds stock between shipment and receipt (e.g. TRANSIT).")]
        public string InTransitCode { get; set; } = "";

        [Description("Optional. " + BcToolDescriptions.TransferRegionBranch)]
        public string ShortcutDimension1Code { get; set; } = "";

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

        [Description("LS Central Store-from: the store whose location is the source, or empty when the source is not a store's location.")]
        public string StoreFrom { get; set; } = "";

        [Description("LS Central Store-to: the store whose location is the destination, or empty. LS posts the receipt to this store's region/branch.")]
        public string StoreTo { get; set; } = "";

        [Description("Region/branch on the order (Shortcut Dimension 1 Code), e.g. 1100-S068 — the source branch. Always tell the user this value.")]
        public string ShortcutDimension1Code { get; set; } = "";

        [Description("Name of that region/branch value (usually the branch name).")]
        public string ShortcutDimension1Name { get; set; } = "";

        [Description("Region/branch the RECEIPT will post to: the destination store's value when storeTo is set, otherwise the order's own.")]
        public string ReceiptShortcutDimension1Code { get; set; } = "";

        [Description("Where shortcutDimension1Code came from: 'sourceStore S002' (the source store's card), 'parentStore S068' (the store a sub-location such as L068-D belongs to) or 'caller' (the value you passed). Mention a parentStore origin to the user so they can correct it.")]
        public string ShortcutDimension1Source { get; set; } = "";
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
    Description = "Add an item line (item No. + quantity) to an existing transfer order. The order's locations come from the header. BINS are set here, per line: pass transferToBinCode when the destination location uses bins (e.g. MK-PLACE → TRENDYOL-MDA, NOON-FLEX, AMAZON-FBN) — a bin-mandatory destination cannot be received into without one, and the result's note says so if it is still missing. find_locations shows binMandatory; find_bins lists a location's bins.",
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

        [Description("Optional. Bin at the DESTINATION location the stock goes into (Transfer-To Bin Code), e.g. TRENDYOL-MDA at MK-PLACE. Required in practice when the destination is bin-mandatory (find_locations → binMandatory; find_bins lists its bins) — without it the order cannot be received. Must be an existing bin of the destination; an unknown bin is refused with the location's bins listed.")]
        public string TransferToBinCode { get; set; } = "";

        [Description("Optional. Bin at the SOURCE location the stock is taken from (Transfer-from Bin Code). Only for bin-mandatory sources; leave empty otherwise (most stores/warehouses here do not use bins).")]
        public string TransferFromBinCode { get; set; } = "";
    }

    public class Result
    {
        [Description("Transfer order number the line belongs to.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Line number assigned by Business Central.")]
        public int LineNo { get; set; }

        [Description("Source bin on the line, if any.")]
        public string TransferFromBinCode { get; set; } = "";

        [Description("Destination bin on the line, if any.")]
        public string TransferToBinCode { get; set; } = "";

        [Description("Warning when a bin-mandatory source or destination still has no bin on this line (the order could not be shipped/received as it stands) — relay it and fix with update_transfer_order_line — or when the order has no region/branch (fix with update_transfer_order, shortcutDimension1Code). Empty when all is well.")]
        public string Note { get; set; } = "";

        [Description("Region/branch on the line (Shortcut Dimension 1 Code), inherited from the order header.")]
        public string ShortcutDimension1Code { get; set; } = "";

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

        [Description("Source bin (Transfer-from Bin Code), when the source location uses bins.")]
        public string TransferFromBinCode { get; set; } = "";

        [Description("Destination bin (Transfer-To Bin Code), when the destination location uses bins. Empty on a bin-mandatory destination means the line cannot be received yet.")]
        public string TransferToBinCode { get; set; } = "";

        [Description("Region/branch on the line (Shortcut Dimension 1 Code), normally the header's.")]
        public string ShortcutDimension1Code { get; set; } = "";
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

        [Description("LS Central Store-from (the source store), or empty when the source is not a store's location.")]
        public string StoreFrom { get; set; } = "";

        [Description("LS Central Store-to (the destination store), or empty. LS posts the receipt to this store's region/branch.")]
        public string StoreTo { get; set; } = "";

        [Description("Region/branch on the order (Shortcut Dimension 1 Code), e.g. 1100-S068. Empty means the order was made without one; repair it with update_transfer_order (shortcutDimension1Code) before posting.")]
        public string ShortcutDimension1Code { get; set; } = "";

        [Description("Name of that region/branch value.")]
        public string ShortcutDimension1Name { get; set; } = "";

        [Description("Region/branch the RECEIPT will post to: the destination store's value when storeTo is set, otherwise the order's own.")]
        public string ReceiptShortcutDimension1Code { get; set; } = "";

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
    Description = "List Business Central transfer orders, newest first, optionally filtered by source and/or destination location — as a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Omit both filters to browse.",
    Sensitivity = "read")]
public class FindTransferOrders : PaginatedToolHandler<FindTransferOrders.Args, FindTransferOrders.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional source location code to filter by. Leave empty to include all.")]
        public string TransferFromCode { get; set; } = "";

        [Description("Optional destination location code to filter by. Leave empty to include all.")]
        public string TransferToCode { get; set; } = "";
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

        [Description("Region/branch (Shortcut Dimension 1 Code). Empty means the order has none yet.")]
        public string ShortcutDimension1Code { get; set; } = "";
    }

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> TransferOrders { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.inventory.find_transfer_orders";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindTransferOrders", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindTransferOrders", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.TransferOrders,
            NextCursor = page.HasMore ? (offset + page.TransferOrders.Count).ToString() : null,
            Total      = null,
        };
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.get_transfer_orders  (batch)
// ---------------------------------------------------------------------------

/// <summary>
/// Batch variant of <see cref="GetTransferOrder"/>: full detail (header + lines) for
/// several transfer orders in one gateway call, so the model never fans out one
/// get_transfer_order per order when a question needs line-level detail across many.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.get_transfer_orders",
    Description = "Batch version of get_transfer_order: fetch full detail (header + all lines with shipped/received quantities) for SEVERAL transfer orders in ONE call. Pass orders[] (max 100), each item with transferOrderNo — the numbers find_transfer_orders returns. Use this instead of calling get_transfer_order once per order. Numbers that cannot be resolved come back in notFound.",
    Sensitivity = "read",
    MaxResultBytes = 262_144)]
public class GetTransferOrdersBatch : ToolHandler<GetTransferOrdersBatch.Args, GetTransferOrdersBatch.Result>
{
    public class OrderRef
    {
        [Description("Transfer order number (No.), e.g. 1001.")]
        public string TransferOrderNo { get; set; } = "";
    }

    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Orders to fetch (1–100). Each item: transferOrderNo, as returned by find_transfer_orders.")]
        public List<OrderRef> Orders { get; set; } = new();
    }

    public class NotFoundRef
    {
        [Description("Transfer order number that was requested.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Why this entry could not be returned.")]
        public string Reason { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of orders requested.")]
        public int Requested { get; set; }

        [Description("Number of orders resolved and returned.")]
        public int Count { get; set; }

        [Description("Full detail for each resolved order (same shape as get_transfer_order).")]
        public List<GetTransferOrder.Result> TransferOrders { get; set; } = new();

        [Description("Requested orders that could not be resolved.")]
        public List<NotFoundRef> NotFound { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        const int MaxBatch = 100;
        if (args.Orders is null || args.Orders.Count == 0)
            throw new ToolValidationException(
                "erp_bc.inventory.get_transfer_orders",
                "Provide a non-empty orders[] array, each item with transferOrderNo.");
        if (args.Orders.Count > MaxBatch)
            throw new ToolValidationException(
                "erp_bc.inventory.get_transfer_orders",
                $"Too many orders requested ({args.Orders.Count}). Request at most {MaxBatch} per call.");

        return await BcClient.ExecuteAsync<Result>(
            "GetTransferOrders", args, "erp_bc.inventory.get_transfer_orders", ctx);
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
    Sensitivity = "destructive")]
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

        [Description("Posted transfer shipment number generated by this posting (present when a shipment was posted). Always report this to the user.")]
        public string PostedShipmentNo { get; set; } = "";

        [Description("Posted transfer receipt number generated by this posting (present when a receipt was posted). Always report this to the user.")]
        public string PostedReceiptNo { get; set; } = "";

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

// ---------------------------------------------------------------------------
// bc.inventory.delete_transfer_order
// ---------------------------------------------------------------------------

/// <summary>
/// Deletes a transfer order, refusing when Business Central would refuse — most
/// importantly when stock has shipped but not been received and is therefore
/// sitting in the in-transit location.
///
/// The gateway checks every rule up front so one call can explain all of them,
/// then still hands the delete to BC so nothing bypasses its own validation.
/// <c>dryRun</c> answers "could this be deleted" without touching anything.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.delete_transfer_order",
    Description = BcToolDescriptions.DeleteTransferOrder,
    Sensitivity = "destructive")]
public class DeleteTransferOrder : ToolHandler<DeleteTransferOrder.Args, DeleteTransferOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Transfer order number (No.) to delete, e.g. 1001.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Optional. When true, CHECK ONLY — report whether the order could be deleted and what is blocking it, changing nothing. Use this when the user is asking whether deletion is possible, or before deleting something you are unsure about. Default false, which really deletes.")]
        public bool DryRun { get; set; }

        [Description("Optional. Business Central only deletes transfer orders with status Open. Set true to reopen a Released order and delete it in one step. Default false, which refuses a Released order and tells you this flag exists. Reopening is undone with everything else if the delete then fails.")]
        public bool ReopenIfReleased { get; set; }
    }

    public class Blocker
    {
        [Description("Why this blocks deletion: ShippedNotReceived (stock is in transit), Reserved (the line is reserved), WarehouseActivity (an open warehouse document exists), or Released (the order is not Open).")]
        public string Reason { get; set; } = "";

        [Description("The transfer order line this concerns. 0 means the blocker is on the order itself, not a line.")]
        public int LineNo { get; set; }

        [Description("Item number on the blocking line.")]
        public string ItemNo { get; set; } = "";

        [Description("Item description on the blocking line.")]
        public string Description { get; set; } = "";

        [Description("Quantity on the line.")]
        public decimal Quantity { get; set; }

        [Description("Quantity already shipped out of the source location.")]
        public decimal QuantityShipped { get; set; }

        [Description("Quantity already received at the destination.")]
        public decimal QuantityReceived { get; set; }

        [Description("Quantity sitting in the in-transit location — shipped but not yet received. This is what makes the order undeletable; receive it first.")]
        public decimal InTransitQuantity { get; set; }

        [Description("Reserved inbound quantity in base units, when reason is Reserved.")]
        public decimal ReservedQtyInboundBase { get; set; }

        [Description("Reserved outbound quantity in base units, when reason is Reserved.")]
        public decimal ReservedQtyOutboundBase { get; set; }

        [Description("The warehouse document type that blocks it, when reason is WarehouseActivity.")]
        public string RelatedTable { get; set; } = "";

        [Description("A sentence explaining the blocker and what to do about it. Relay this to the user.")]
        public string Detail { get; set; } = "";
    }

    public class Result
    {
        [Description("The transfer order number.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Source location code.")]
        public string TransferFromCode { get; set; } = "";

        [Description("Destination location code.")]
        public string TransferToCode { get; set; } = "";

        [Description("Order status before the call: Open or Released.")]
        public string StatusBefore { get; set; } = "";

        [Description("How many lines the order had.")]
        public int LineCount { get; set; }

        [Description("True when this was a check only and nothing was changed.")]
        public bool DryRun { get; set; }

        [Description("Whether the order was actually deleted. On a dry run this is always false — never report a deletion on the strength of a dry run.")]
        public bool Deleted { get; set; }

        [Description("Whether the order could be deleted. On a dry run this is the answer to the question.")]
        public bool Deletable { get; set; }

        [Description("True when a Released order was reopened as part of the delete.")]
        public bool Reopened { get; set; }

        [Description("How many blocking issues were found.")]
        public int BlockerCount { get; set; }

        [Description("Everything preventing deletion, one entry per reason per line. Empty when the order is deletable.")]
        public List<Blocker> Blockers { get; set; } = new();

        [Description("Human-readable summary — either confirmation of the delete or the full list of blockers. Relay its meaning to the user.")]
        public string Note { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.inventory.delete_transfer_order";

        if (string.IsNullOrWhiteSpace(args.TransferOrderNo))
            throw new ToolValidationException(ToolKey, "transferOrderNo is required.");

        // A dry run changes nothing, but it still runs through the gateway's write
        // path, and the caller identity is what the request log records. Requiring
        // it for both keeps one rule for the whole tool.
        BcCallerContext.Require(ctx, ToolKey);
        return await BcClient.ExecuteAsync<Result>("DeleteTransferOrder", args, ToolKey, ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.update_transfer_order
// ---------------------------------------------------------------------------

/// <summary>
/// Corrects a transfer order header. Every field is nullable so the gateway can
/// tell "not supplied" from "set to empty" — the distinction an edit tool lives
/// or dies by.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.update_transfer_order",
    Description = BcToolDescriptions.UpdateTransferOrder,
    Sensitivity = "destructive")]
public class UpdateTransferOrder : ToolHandler<UpdateTransferOrder.Args, UpdateTransferOrder.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Transfer order number (No.) to correct.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Optional. New source location code. Refused once any line has shipped, and cascades to every line.")]
        public string? TransferFromCode { get; set; }

        [Description("Optional. New destination location code. Refused once any line has shipped, and cascades to every line.")]
        public string? TransferToCode { get; set; }

        [Description("Optional. New in-transit location code. Refused once any line has shipped.")]
        public string? InTransitCode { get; set; }

        [Description("Optional. New posting date (yyyy-MM-dd). Send empty to clear it.")]
        public string? PostingDate { get; set; }

        [Description("Optional. New shipment date (yyyy-MM-dd). Recalculates the receipt date through the transfer route and cascades to every line. Send empty to clear it.")]
        public string? ShipmentDate { get; set; }

        [Description("Optional. New receipt date (yyyy-MM-dd). Recalculates the shipment date through the transfer route and cascades to every line. Send empty to clear it.")]
        public string? ReceiptDate { get; set; }

        [Description("Optional. New external document number. This is the ONE field Business Central allows to change while the order is Released. Send empty to clear it.")]
        public string? ExternalDocumentNo { get; set; }

        [Description("Optional. New shipping agent code. Cascades to every line. Send empty to clear it.")]
        public string? ShippingAgentCode { get; set; }

        [Description("Optional. Set or repair the region/branch; cascades to every line. Cannot be cleared, and cannot change once any line has shipped. " + BcToolDescriptions.TransferRegionBranch)]
        public string? ShortcutDimension1Code { get; set; }
    }

    public class Line
    {
        [Description("Line number.")]
        public int LineNo { get; set; }

        [Description("Item number on the line.")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Region/branch on the line (Shortcut Dimension 1 Code) after the update.")]
        public string ShortcutDimension1Code { get; set; } = "";

        [Description("Quantity on the line.")]
        public decimal Quantity { get; set; }

        [Description("Unit of measure code.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Quantity already shipped.")]
        public decimal QtyShipped { get; set; }

        [Description("Quantity already received.")]
        public decimal QtyReceived { get; set; }

        [Description("Quantity still outstanding.")]
        public decimal OutstandingQuantity { get; set; }
    }

    public class Result
    {
        [Description("The transfer order number.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Source location code after the update.")]
        public string TransferFromCode { get; set; } = "";

        [Description("Source location name.")]
        public string TransferFromName { get; set; } = "";

        [Description("Destination location code after the update.")]
        public string TransferToCode { get; set; } = "";

        [Description("Destination location name.")]
        public string TransferToName { get; set; } = "";

        [Description("In-transit location code.")]
        public string InTransitCode { get; set; } = "";

        [Description("Order status: Open or Released.")]
        public string Status { get; set; } = "";

        [Description("Posting date after the update.")]
        public string PostingDate { get; set; } = "";

        [Description("Shipment date after the update — may differ from what you sent, because the transfer route recalculates it.")]
        public string ShipmentDate { get; set; } = "";

        [Description("Receipt date after the update — may differ from what you sent, because the transfer route recalculates it.")]
        public string ReceiptDate { get; set; } = "";

        [Description("External document number after the update.")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("LS Central Store-from after the update (the source store), or empty.")]
        public string StoreFrom { get; set; } = "";

        [Description("LS Central Store-to after the update (the destination store), or empty.")]
        public string StoreTo { get; set; } = "";

        [Description("Region/branch (Shortcut Dimension 1 Code) after the update — the source branch.")]
        public string ShortcutDimension1Code { get; set; } = "";

        [Description("Name of that region/branch value.")]
        public string ShortcutDimension1Name { get; set; } = "";

        [Description("Region/branch the RECEIPT will post to: the destination store's value when storeTo is set, otherwise the order's own.")]
        public string ReceiptShortcutDimension1Code { get; set; } = "";

        [Description("Number of lines on the order.")]
        public int LineCount { get; set; }

        [Description("All lines after the update — useful because a header change cascades into them.")]
        public List<Line> Lines { get; set; } = new();

        [Description("How many fields actually changed.")]
        public int ChangedFieldCount { get; set; }

        [Description("Which fields actually changed. A field you sent that already matched is not listed. storeFrom / storeTo appear when the LS Central store fields were brought in line with the locations.")]
        public List<string> ChangedFields { get; set; } = new();

        [Description("Human-readable summary, including whether the change cascaded to the lines. Relay its meaning to the user.")]
        public string Note { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.inventory.update_transfer_order";

        if (string.IsNullOrWhiteSpace(args.TransferOrderNo))
            throw new ToolValidationException(ToolKey, "transferOrderNo is required.");

        // Catching the no-op here saves a round-trip on the commonest mistake:
        // calling the tool with only the order number and expecting something.
        if (args.TransferFromCode is null && args.TransferToCode is null &&
            args.InTransitCode is null && args.PostingDate is null &&
            args.ShipmentDate is null && args.ReceiptDate is null &&
            args.ExternalDocumentNo is null && args.ShippingAgentCode is null &&
            args.ShortcutDimension1Code is null)
            throw new ToolValidationException(
                ToolKey,
                "Nothing to change: supply at least one field to update. Omitted fields are left as they are.");

        BcCallerContext.Require(ctx, ToolKey);
        return await BcClient.ExecuteAsync<Result>("UpdateTransferOrder", args, ToolKey, ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.update_transfer_order_line
// ---------------------------------------------------------------------------

/// <summary>
/// Corrects one transfer order line. The quantity floor is what makes this
/// interesting: BC refuses to shrink a line below what has already shipped.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.update_transfer_order_line",
    Description = BcToolDescriptions.UpdateTransferOrderLine,
    Sensitivity = "write")]
public class UpdateTransferOrderLine : ToolHandler<UpdateTransferOrderLine.Args, UpdateTransferOrderLine.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Transfer order number (No.) the line belongs to.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Line number to correct, as returned by get_transfer_order.")]
        public int LineNo { get; set; }

        [Description("Optional. New quantity. Must be greater than zero, and can never be less than the quantity already shipped on this line. To remove the line use delete_transfer_order_line.")]
        public decimal? Quantity { get; set; }

        [Description("Optional. New variant code. Refused once the line has shipped. Send empty to clear it.")]
        public string? VariantCode { get; set; }

        [Description("Optional. New shipment date for this line (yyyy-MM-dd). Send empty to clear it.")]
        public string? ShipmentDate { get; set; }

        [Description("Optional. New receipt date for this line (yyyy-MM-dd). Send empty to clear it.")]
        public string? ReceiptDate { get; set; }

        [Description("Optional. Destination bin (Transfer-To Bin Code), e.g. TRENDYOL-MDA. Must exist at the destination location; refused once the line has been received. Send empty to clear it.")]
        public string? TransferToBinCode { get; set; }

        [Description("Optional. Source bin (Transfer-from Bin Code). Must exist at the source location; refused once the line has shipped. Send empty to clear it.")]
        public string? TransferFromBinCode { get; set; }
    }

    public class Result
    {
        [Description("The transfer order number.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Line number.")]
        public int LineNo { get; set; }

        [Description("Item number on the line.")]
        public string ItemNo { get; set; } = "";

        [Description("Variant code.")]
        public string VariantCode { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Quantity after the update.")]
        public decimal Quantity { get; set; }

        [Description("Unit of measure code.")]
        public string UnitOfMeasureCode { get; set; } = "";

        [Description("Quantity still to ship.")]
        public decimal QtyToShip { get; set; }

        [Description("Quantity already shipped — the floor the quantity cannot go below.")]
        public decimal QtyShipped { get; set; }

        [Description("Quantity still to receive.")]
        public decimal QtyToReceive { get; set; }

        [Description("Quantity already received.")]
        public decimal QtyReceived { get; set; }

        [Description("Quantity still outstanding.")]
        public decimal OutstandingQuantity { get; set; }

        [Description("Shipment date after the update.")]
        public string ShipmentDate { get; set; } = "";

        [Description("Receipt date after the update.")]
        public string ReceiptDate { get; set; } = "";

        [Description("Source bin after the update.")]
        public string TransferFromBinCode { get; set; } = "";

        [Description("Destination bin after the update.")]
        public string TransferToBinCode { get; set; } = "";

        [Description("How many fields actually changed.")]
        public int ChangedFieldCount { get; set; }

        [Description("Which fields actually changed.")]
        public List<string> ChangedFields { get; set; } = new();

        [Description("Human-readable confirmation. Relay its meaning to the user.")]
        public string Note { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.inventory.update_transfer_order_line";

        if (string.IsNullOrWhiteSpace(args.TransferOrderNo))
            throw new ToolValidationException(ToolKey, "transferOrderNo is required.");
        if (args.LineNo <= 0)
            throw new ToolValidationException(
                ToolKey, "lineNo is required — get_transfer_order lists each line's lineNo.");
        if (args.Quantity is <= 0)
            throw new ToolValidationException(
                ToolKey,
                "quantity must be greater than zero. To remove the line entirely use delete_transfer_order_line.");
        if (args.Quantity is null && args.VariantCode is null &&
            args.ShipmentDate is null && args.ReceiptDate is null &&
            args.TransferToBinCode is null && args.TransferFromBinCode is null)
            throw new ToolValidationException(
                ToolKey,
                "Nothing to change: supply at least one of quantity, variantCode, shipmentDate, receiptDate, transferToBinCode or transferFromBinCode.");

        BcCallerContext.Require(ctx, ToolKey);
        return await BcClient.ExecuteAsync<Result>("UpdateTransferOrderLine", args, ToolKey, ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.inventory.delete_transfer_order_line
// ---------------------------------------------------------------------------

/// <summary>
/// Removes one line from a transfer order. Shares its rules with
/// <see cref="DeleteTransferOrder"/> — the gateway runs the same per-line
/// blocker checks — so a line with stock in transit is refused the same way.
/// </summary>
[Tool(
    Key         = "erp_bc.inventory.delete_transfer_order_line",
    Description = BcToolDescriptions.DeleteTransferOrderLine,
    Sensitivity = "write")]
public class DeleteTransferOrderLine : ToolHandler<DeleteTransferOrderLine.Args, DeleteTransferOrderLine.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Transfer order number (No.) the line belongs to.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("Line number to delete, as returned by get_transfer_order.")]
        public int LineNo { get; set; }

        [Description("Optional. When true, CHECK ONLY — report whether the line could be deleted and what is blocking it, changing nothing. Default false, which really deletes.")]
        public bool DryRun { get; set; }
    }

    public class Result
    {
        [Description("The transfer order number.")]
        public string TransferOrderNo { get; set; } = "";

        [Description("The line number.")]
        public int LineNo { get; set; }

        [Description("Item number that was on the line.")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Quantity that was on the line.")]
        public decimal Quantity { get; set; }

        [Description("True when this was a check only and nothing was changed.")]
        public bool DryRun { get; set; }

        [Description("Whether the line was actually deleted. Always false on a dry run — never report a deletion on the strength of one.")]
        public bool Deleted { get; set; }

        [Description("Whether the line could be deleted.")]
        public bool Deletable { get; set; }

        [Description("How many blocking issues were found.")]
        public int BlockerCount { get; set; }

        [Description("Everything preventing deletion — same reasons as delete_transfer_order (ShippedNotReceived, Reserved, WarehouseActivity), scoped to this line.")]
        public List<DeleteTransferOrder.Blocker> Blockers { get; set; } = new();

        [Description("How many lines the order has left. 0 means the order is now empty and cannot be posted until a line is added.")]
        public int RemainingLineCount { get; set; }

        [Description("Human-readable summary. Relay its meaning to the user — especially when the order is left with no lines.")]
        public string Note { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.inventory.delete_transfer_order_line";

        if (string.IsNullOrWhiteSpace(args.TransferOrderNo))
            throw new ToolValidationException(ToolKey, "transferOrderNo is required.");
        if (args.LineNo <= 0)
            throw new ToolValidationException(
                ToolKey, "lineNo is required — get_transfer_order lists each line's lineNo.");

        BcCallerContext.Require(ctx, ToolKey);
        return await BcClient.ExecuteAsync<Result>("DeleteTransferOrderLine", args, ToolKey, ctx);
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

        [Description("Tender type name/description, e.g. 'Card', 'Voucher', 'قسيمة استرجاع فقط'. ALWAYS show this with the tender, not the code alone.")]
        public string TenderDescription { get; set; } = "";

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

        [Description("Currency the amounts on this transaction are denominated in (the selling store's currency). Always report amounts with this code.")]
        public string CurrencyCode { get; set; } = "";

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

        [Description("RETURNS LINK: the ORIGINAL sale receipt this transaction's lines were pulled from ('Retrieved from Receipt No.' on the Transaction Register). Non-empty on a refund/exchange — this is the only field that ties a return back to the sale it reverses. Look it up with get_pos_transaction receiptNo to see the original sale.")]
        public string RetrievedFromReceiptNo { get; set; } = "";

        [Description("Refund receipt number when the POS issued a separate refund receipt.")]
        public string RefundReceiptNo { get; set; } = "";

        [Description("The POS's own return flag. NOT reliable on its own — some refunds (e.g. an exchange settled with a return voucher) leave it false. Judge a return from retrievedFromReceiptNo, a positive gross amount, or the tender.")]
        public bool SaleIsReturnSale { get; set; }

        [Description("True when the receipt mixes sale and refund lines.")]
        public bool TransIsMixedSaleRefund { get; set; }

        [Description("True when retrievedFromReceiptNo is set, i.e. this transaction is linked to an original sale receipt.")]
        public bool LinkedToOriginalReceipt { get; set; }

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
/// Searches LS Central POS transactions, defaulting to Sales type — as a paginated
/// dataset. The gateway pages server-side (top + skip over a deterministic
/// Date, Time, Store, Terminal, Transaction No. descending key), so the cursor is a
/// plain row offset; the platform samples page 1 for the LLM and replays the cursor
/// chain when the full set is materialized.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.find_pos_transactions",
    Description = "List INDIVIDUAL LS Central POS receipts (LSC Transaction Header), newest first, as a dataset — returns a sample plus a dataset_ref; the full set can be exported/analyzed on demand, so never page manually. Defaults to Sales type; filter by store (storeNo or storeNameContains — e.g. 'مخرج 9' resolves to S004), terminal, customer, receipt substring, mobile number, customer order ID, and/or date range. NOT for sales totals: a 'total POS sales for 2024/2025' question must use sum_sales (preset posSales / posNetSales) — the sample shown here is not the real total. Use count_transactions for receipt counts.",
    Sensitivity = "read")]
public class FindPosTransactions : PaginatedToolHandler<FindPosTransactions.Args, FindPosTransactions.Match>
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

        [Description("Optional. Exact ORIGINAL sale receipt number ('Retrieved from Receipt No.'). Use this to answer 'what was returned against receipt X' — it finds the refund/exchange transactions that came from that sale. Specific enough to be used without a date or store filter.")]
        public string RetrievedFromReceiptNo { get; set; } = "";

        [Description("Optional. Filter transactions whose 'Retrieved from Receipt No.' contains this text (case-insensitive).")]
        public string RetrievedFromReceiptNoContains { get; set; } = "";

        [Description("Optional. Earliest transaction date (yyyy-MM-dd), inclusive.")]
        public string FromDate { get; set; } = "";

        [Description("Optional. Latest transaction date (yyyy-MM-dd), inclusive.")]
        public string ToDate { get; set; } = "";

        [Description("Optional. Transaction type; defaults to Sales. Other values: Payment, Voided, Logon, Logoff, Tender Decl., etc.")]
        public string TransactionType { get; set; } = "";

        [Description("Optional. When true, return only RETURN/refund receipts (net positive gross amount — POS sales are stored negative). Combine with store and/or date filters.")]
        public bool? ReturnsOnly { get; set; }
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

        [Description("Currency the amounts on this transaction are denominated in (the selling store's currency). Always report amounts with this code.")]
        public string CurrencyCode { get; set; } = "";

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

        [Description("RETURNS LINK: the ORIGINAL sale receipt this transaction's lines were pulled from ('Retrieved from Receipt No.' on the Transaction Register). Non-empty on a refund/exchange — this is the only field that ties a return back to the sale it reverses. Look it up with get_pos_transaction receiptNo to see the original sale.")]
        public string RetrievedFromReceiptNo { get; set; } = "";

        [Description("Refund receipt number when the POS issued a separate refund receipt.")]
        public string RefundReceiptNo { get; set; } = "";

        [Description("The POS's own return flag. NOT reliable on its own — some refunds (e.g. an exchange settled with a return voucher) leave it false. Judge a return from retrievedFromReceiptNo, a positive gross amount, or the tender.")]
        public bool SaleIsReturnSale { get; set; }

        [Description("True when the receipt mixes sale and refund lines.")]
        public bool TransIsMixedSaleRefund { get; set; }

        [Description("True when retrievedFromReceiptNo is set, i.e. this transaction is linked to an original sale receipt.")]
        public bool LinkedToOriginalReceipt { get; set; }
    }

    // Wire shape of the gateway FindPosTransactions response. `skip` is nullable on
    // purpose: a gateway build that predates paging omits it, and paging against such
    // a build would silently return the same first page forever — detect and fail.
    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Transactions { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.retail.find_pos_transactions";
        // The gateway caps a page at 500 rows (GetPagedTop).
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindPosTransactions", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindPosTransactions", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Transactions,
            NextCursor = page.HasMore ? (offset + page.Transactions.Count).ToString() : null,
            Total      = null, // no cheap COUNT on the gateway for this filter set
        };
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_pos_transactions  (batch)
// ---------------------------------------------------------------------------

/// <summary>
/// Batch variant of <see cref="GetPosTransaction"/>: returns full detail for several
/// POS receipts in one gateway call, so the model never has to fan out one
/// get_pos_transaction per receipt when a question needs line-level detail across many.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_pos_transactions",
    Description = "Batch version of get_pos_transaction: fetch full detail (header, sales lines, payment tenders) for SEVERAL POS receipts in ONE call. Pass transactions[] (max 100), each item identified by receiptNo, or storeNo + posTerminalNo + transactionNo — exactly the identifiers find_pos_transactions returns. Use this instead of calling get_pos_transaction once per receipt. Receipts that cannot be resolved come back in notFound. For 'best-selling products' / 'top items' use get_top_items instead — it aggregates server-side rather than pulling every line.",
    Sensitivity = "read",
    MaxResultBytes = 262_144)]
public class GetPosTransactionsBatch : ToolHandler<GetPosTransactionsBatch.Args, GetPosTransactionsBatch.Result>
{
    public class TransactionRef
    {
        [Description("Optional. Receipt number (Receipt No.) — the usual user-facing identifier.")]
        public string ReceiptNo { get; set; } = "";

        [Description("Optional LS Central store number (e.g. S004). Scopes a receiptNo lookup, or part of the composite key with posTerminalNo + transactionNo.")]
        public string StoreNo { get; set; } = "";

        [Description("Optional. POS terminal number (POS Terminal No.), required with storeNo + transactionNo when receiptNo is omitted.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("Optional. Transaction number (Transaction No.), required with storeNo + posTerminalNo when receiptNo is omitted.")]
        public int? TransactionNo { get; set; }
    }

    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Receipts to fetch (1–100). Each item: receiptNo, or storeNo + posTerminalNo + transactionNo. Use the identifiers returned by find_pos_transactions.")]
        public List<TransactionRef> Transactions { get; set; } = new();
    }

    public class NotFoundRef
    {
        [Description("Receipt number that was requested.")]
        public string ReceiptNo { get; set; } = "";

        [Description("Store number that was requested.")]
        public string StoreNo { get; set; } = "";

        [Description("POS terminal number that was requested.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("Transaction number that was requested.")]
        public int TransactionNo { get; set; }

        [Description("Why this entry could not be returned.")]
        public string Reason { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of receipts requested.")]
        public int Requested { get; set; }

        [Description("Number of receipts resolved and returned.")]
        public int Count { get; set; }

        [Description("Full detail for each resolved receipt (same shape as get_pos_transaction).")]
        public List<GetPosTransaction.Result> Transactions { get; set; } = new();

        [Description("Requested receipts that could not be resolved.")]
        public List<NotFoundRef> NotFound { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        const int MaxBatch = 100;
        if (args.Transactions is null || args.Transactions.Count == 0)
            throw new ToolValidationException(
                "erp_bc.retail.get_pos_transactions",
                "Provide a non-empty transactions[] array, each item with receiptNo or storeNo + posTerminalNo + transactionNo.");
        if (args.Transactions.Count > MaxBatch)
            throw new ToolValidationException(
                "erp_bc.retail.get_pos_transactions",
                $"Too many transactions requested ({args.Transactions.Count}). Request at most {MaxBatch} per call.");

        return await BcClient.ExecuteAsync<Result>(
            "GetPosTransactions", args, "erp_bc.retail.get_pos_transactions", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_top_items
// ---------------------------------------------------------------------------

/// <summary>
/// Server-side "best-selling items" aggregation over LS Central POS sales lines,
/// grouped by item and ranked by quantity / sales / cost. Answers "top products for
/// branch X yesterday" in one call instead of pulling every receipt's lines.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_top_items",
    Description = "Best-selling / top items for a store and/or date range. Aggregates POS sales lines (LSC Trans. Sales Entry) by Item No. server-side and returns items ranked by the chosen metric — one fast call, no per-receipt loop. Use for 'best-selling products', 'top items', 'الأكثر مبيعاً' for a branch/period. Scope with storeNo or storeNameContains (e.g. 'مخرج 4' → S004) and/or fromDate/toDate. rankBy: quantity (default, units sold), netAmount (sales value), or costAmount. Returns at most 200 items (default 10); each item has quantity, netAmount, costAmount, discountAmount, lineCount, type (Inventory/Service/Non-Inventory), and currencyCode, plus totalQuantity, a per-currency money breakdown (totalsByCurrency), and quantityByType. Amounts are per currency: an item sold in more than one currency appears once per currency, and the ranked list can interleave currencies — always report money with its currencyCode and never add amounts of different currencies. Values are sales-positive (units/value sold; returns net out). When reporting quantities sold, call out Service/Non-Inventory separately from Inventory (use each item's type and the quantityByType breakdown) — those units were rung up at POS but never represented real stock movement.",
    Sensitivity = "read",
    MaxResultBytes = 65_536)]
public class GetTopItems : ToolHandler<GetTopItems.Args, GetTopItems.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description(BcToolDescriptions.StoreNo)]
        public string StoreNo { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. POS terminal number (POS Terminal No.) to scope to a single terminal.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("Optional. Item Category Code filter on the sales lines.")]
        public string ItemCategoryCode { get; set; } = "";

        [Description("Optional. Earliest sales date (yyyy-MM-dd), inclusive.")]
        public string FromDate { get; set; } = "";

        [Description("Optional. Latest sales date (yyyy-MM-dd), inclusive.")]
        public string ToDate { get; set; } = "";

        [Description("Optional. Ranking metric: quantity (default, units sold), netAmount (sales value), or costAmount.")]
        public string RankBy { get; set; } = "";

        [Description("Optional. Number of top items to return (default 10, max 200).")]
        public int? Top { get; set; }
    }

    public class TopItem
    {
        [Description("1-based rank by the chosen metric.")]
        public int Rank { get; set; }

        [Description("Item number.")]
        public string ItemNo { get; set; } = "";

        [Description("Item description.")]
        public string Description { get; set; } = "";

        [Description("Item type: Inventory, Service, or Non-Inventory. Service/Non-Inventory items were rung up at POS but never had a real on-hand quantity — don't treat their quantity as stock movement.")]
        public string Type { get; set; } = "";

        [Description("Currency the amounts on this row are denominated in (from the selling store). An item sold in more than one currency appears as one row per currency; always report the amount with this code.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total quantity sold (returns net out).")]
        public decimal Quantity { get; set; }

        [Description("Total net sales amount.")]
        public decimal NetAmount { get; set; }

        [Description("Total cost amount.")]
        public decimal CostAmount { get; set; }

        [Description("Total discount amount.")]
        public decimal DiscountAmount { get; set; }

        [Description("Number of sales lines aggregated for this item.")]
        public int LineCount { get; set; }
    }

    public class Result
    {
        [Description("Store number the aggregation was scoped to (empty if none).")]
        public string StoreNo { get; set; } = "";

        [Description("POS terminal the aggregation was scoped to (empty if none).")]
        public string PosTerminalNo { get; set; } = "";

        [Description("From date applied (yyyy-MM-dd).")]
        public string FromDate { get; set; } = "";

        [Description("To date applied (yyyy-MM-dd).")]
        public string ToDate { get; set; } = "";

        [Description("Ranking metric actually applied: quantity, netAmount, or costAmount.")]
        public string RankBy { get; set; } = "";

        [Description("Number of items returned.")]
        public int Count { get; set; }

        [Description("Distinct items that sold in the scope.")]
        public int DistinctItems { get; set; }

        [Description("Sales lines scanned to build the ranking.")]
        public int ScannedLines { get; set; }

        [Description("Reserved; always false. The gateway aggregates the full scope server-side (SQL GROUP BY), so the ranking is never partial.")]
        public bool Truncated { get; set; }

        [Description("Total quantity across all items in the scope. Units are currency-agnostic, so this is a single figure even across currencies.")]
        public decimal TotalQuantity { get; set; }

        [Description("Net and cost sales totals for the whole scope, broken down PER CURRENCY. There is no single scalar net/cost total on purpose: a multi-currency scope would add different currencies together. Report each currency separately.")]
        public List<CurrencyTotal> TotalsByCurrency { get; set; } = new();

        [Description("Quantity and line-count totals split by item type (Inventory, Service, Non-Inventory) across the WHOLE scope (not just the returned top N) — use this to report units sold separately for real inventory movement vs. service items rung up at POS.")]
        public List<TypeTotal> QuantityByType { get; set; } = new();

        [Description("Top items, highest metric first. When the scope spans multiple currencies the list interleaves them, so compare/report within a single currencyCode.")]
        public List<TopItem> Items { get; set; } = new();
    }

    public class CurrencyTotal
    {
        [Description("Currency code these totals are denominated in.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total net sales amount for this currency across the scope.")]
        public decimal TotalNetAmount { get; set; }

        [Description("Total cost amount for this currency across the scope.")]
        public decimal TotalCostAmount { get; set; }

        [Description("Number of sales lines aggregated for this currency.")]
        public int LineCount { get; set; }
    }

    public class TypeTotal
    {
        [Description("Item type: Inventory, Service, or Non-Inventory.")]
        public string Type { get; set; } = "";

        [Description("Total quantity sold for this item type (returns net out).")]
        public decimal Quantity { get; set; }

        [Description("Number of sales lines aggregated for this item type.")]
        public int LineCount { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        const int MaxTop = 200;
        args.Top = args.Top is int t && t > 0 ? Math.Min(t, MaxTop) : 10;

        var hasScope = !string.IsNullOrWhiteSpace(args.StoreNo)
            || !string.IsNullOrWhiteSpace(args.StoreNameContains)
            || !string.IsNullOrWhiteSpace(args.FromDate)
            || !string.IsNullOrWhiteSpace(args.ToDate)
            || !string.IsNullOrWhiteSpace(args.ItemCategoryCode);
        if (!hasScope)
            throw new ToolValidationException(
                "erp_bc.retail.get_top_items",
                "Provide storeNo/storeNameContains and/or a date range (fromDate/toDate) to scope the top-items aggregation.");

        return await BcClient.ExecuteAsync<Result>(
            "GetTopItems", args, "erp_bc.retail.get_top_items", ctx);
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

        [Description("Retail store where the sale was actually made (Created at Store). THIS is the store to attribute a customer-order sale to — NOT the line's Store No. (which can be a fulfilling warehouse like MDA01) nor the linked POS transaction's store.")]
        public string CreatedAtStore { get; set; } = "";

        [Description("Currency all amounts on this order are denominated in (derived from the creating store). Always report amounts with this code.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Created date/time (yyyy-MM-dd HH:mm:ss).")]
        public string Created { get; set; } = "";

        [Description("Processing status of the order (e.g. To Pick, To Collect, Delivered). A customer order is a RESERVATION until it is posted — do not count it as a completed/realized sale until then. Use totalDeliveredAmount / finalisedAmountLcy to judge whether it has been posted.")]
        public string ProcessingStatus { get; set; } = "";

        [Description("Total delivered amount (posted portion). 0 means nothing has been posted/delivered yet — the order is still a pending reservation, not a completed sale.")]
        public decimal TotalDeliveredAmount { get; set; }

        [Description("Finalised (posted & paid) amount in local currency. 0 means the order has not been finalised/posted — not yet a completed sale.")]
        public decimal FinalisedAmountLcy { get; set; }

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
    Description = "Search LS Central customer orders (LSC Customer Order Header), newest first, as a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Filter by document ID, external ID, customer, store, mobile phone, processing status, and/or created date range.",
    Sensitivity = "read")]
public class FindCustomerOrders : PaginatedToolHandler<FindCustomerOrders.Args, FindCustomerOrders.Match>
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
    }

    public class Match
    {
        [Description("Document ID.")]
        public string DocumentId { get; set; } = "";

        [Description("External ID.")]
        public string ExternalId { get; set; } = "";

        [Description("Retail store where the sale was actually made (Created at Store) — attribute the sale to this store, not the line Store No. (which may be a fulfilling warehouse) or the linked POS transaction's store.")]
        public string CreatedAtStore { get; set; } = "";

        [Description("Created date/time.")]
        public string Created { get; set; } = "";

        [Description("Processing status. The order is a reservation until posted — not a completed sale until then (open get_customer_order for delivered/finalised amounts).")]
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

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> CustomerOrders { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.retail.find_customer_orders";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindCustomerOrders", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindCustomerOrders", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.CustomerOrders,
            NextCursor = page.HasMore ? (offset + page.CustomerOrders.Count).ToString() : null,
            Total      = null,
        };
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_customer_orders  (batch)
// ---------------------------------------------------------------------------

/// <summary>
/// Batch variant of <see cref="GetCustomerOrder"/>: full detail (header + lines) for
/// several customer orders in one gateway call, so the model never fans out one
/// get_customer_order per order when a question needs line-level detail across many.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_customer_orders",
    Description = "Batch version of get_customer_order: fetch full detail (header + all lines) for SEVERAL LS Central customer orders in ONE call. Pass orders[] (max 100), each item identified by documentId or externalId — the identifiers find_customer_orders returns. Use this instead of calling get_customer_order once per order (e.g. 'the items/line status in yesterday's click & collect orders'). Orders that cannot be resolved come back in notFound.",
    Sensitivity = "read",
    MaxResultBytes = 262_144)]
public class GetCustomerOrdersBatch : ToolHandler<GetCustomerOrdersBatch.Args, GetCustomerOrdersBatch.Result>
{
    public class OrderRef
    {
        [Description("Customer order document ID (Document ID), e.g. CO26-000003883.")]
        public string DocumentId { get; set; } = "";

        [Description("Optional external ID when document ID is unknown, e.g. SA26010418162.")]
        public string ExternalId { get; set; } = "";
    }

    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Orders to fetch (1–100). Each item: documentId or externalId, as returned by find_customer_orders.")]
        public List<OrderRef> Orders { get; set; } = new();
    }

    public class NotFoundRef
    {
        [Description("Document ID that was requested.")]
        public string DocumentId { get; set; } = "";

        [Description("External ID that was requested.")]
        public string ExternalId { get; set; } = "";

        [Description("Why this entry could not be returned.")]
        public string Reason { get; set; } = "";
    }

    public class Result
    {
        [Description("Number of orders requested.")]
        public int Requested { get; set; }

        [Description("Number of orders resolved and returned.")]
        public int Count { get; set; }

        [Description("Full detail for each resolved order (same shape as get_customer_order).")]
        public List<GetCustomerOrder.Result> Orders { get; set; } = new();

        [Description("Requested orders that could not be resolved.")]
        public List<NotFoundRef> NotFound { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        const int MaxBatch = 100;
        if (args.Orders is null || args.Orders.Count == 0)
            throw new ToolValidationException(
                "erp_bc.retail.get_customer_orders",
                "Provide a non-empty orders[] array, each item with documentId or externalId.");
        if (args.Orders.Count > MaxBatch)
            throw new ToolValidationException(
                "erp_bc.retail.get_customer_orders",
                $"Too many orders requested ({args.Orders.Count}). Request at most {MaxBatch} per call.");

        return await BcClient.ExecuteAsync<Result>(
            "GetCustomerOrders", args, "erp_bc.retail.get_customer_orders", ctx);
    }
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
    Description = "Sum POS or customer-order sales amounts (gateway Sum operation). Use for period totals like 'total POS sales in 2024' or 'sales for Exit 9 today' — do NOT use find_pos_transactions for totals. Presets: posSales (gross), posNetSales, customerOrderSales. Filter by Date range and Store No., or pass storeNameContains (e.g. 'مخرج 9') to scope to one branch. Pass items[] to sum several periods in one call and read grandTotalByCurrency. Every total is returned as a per-currency breakdown (byCurrency / grandTotalByCurrency); report each currency separately and NEVER add amounts of different currencies. Always state the currency code with the amount.",
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

// ---------------------------------------------------------------------------
// bc.retail.get_member
// ---------------------------------------------------------------------------

/// <summary>
/// Full detail for one LS Central loyalty member account: account card, point
/// balances, contacts (people), and membership cards.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_member",
    Description = "Look up an LS Central loyalty MEMBER (LSC Member Account) with point balances, contacts, and membership cards. Identify the member by accountNo, cardNo (membership card), contactNo, mobileNo (exact), or email (exact). Points are held per ACCOUNT: pointsBalance is the open remaining points; totalIssuedPoints/usedPoints/expiredPoints explain it, and pointsExpiring30Days/90Days warn about upcoming expiry. For point transaction history use get_member_points; to search members by partial name/mobile use lookup_member or find_members.",
    Sensitivity = "read")]
public class GetMember : ToolHandler<GetMember.Args, GetMember.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Member account number (LSC Member Account No.). Provide ONE of accountNo, cardNo, contactNo, mobileNo, or email.")]
        public string AccountNo { get; set; } = "";

        [Description("Membership card number — the card swiped/scanned at the POS.")]
        public string CardNo { get; set; } = "";

        [Description("Member contact number (LSC Member Contact Contact No.).")]
        public string ContactNo { get; set; } = "";

        [Description("Exact mobile phone number of a member contact.")]
        public string MobileNo { get; set; } = "";

        [Description("Exact e-mail address of a member contact.")]
        public string Email { get; set; } = "";
    }

    public class ContactRow
    {
        [Description("Member contact number.")]
        public string ContactNo { get; set; } = "";

        [Description("Contact name.")]
        public string Name { get; set; } = "";

        [Description("Mobile phone number.")]
        public string MobilePhoneNo { get; set; } = "";

        [Description("Phone number.")]
        public string PhoneNo { get; set; } = "";

        [Description("E-mail address.")]
        public string Email { get; set; } = "";

        [Description("City.")]
        public string City { get; set; } = "";

        [Description("Gender when recorded.")]
        public string Gender { get; set; } = "";

        [Description("Date of birth (yyyy-MM-dd) when recorded.")]
        public string DateOfBirth { get; set; } = "";

        [Description("True when this is the account's main contact.")]
        public bool MainContact { get; set; }

        [Description("True when the contact is blocked.")]
        public bool Blocked { get; set; }
    }

    public class CardRow
    {
        [Description("Membership card number.")]
        public string CardNo { get; set; } = "";

        [Description("Card status: Free, Allocated, Active, or Blocked.")]
        public string Status { get; set; } = "";

        [Description("Contact the card is assigned to, when any.")]
        public string ContactNo { get; set; } = "";

        [Description("Last valid date of the card (yyyy-MM-dd).")]
        public string LastValidDate { get; set; } = "";

        [Description("Reason code when the card is blocked.")]
        public string ReasonBlocked { get; set; } = "";
    }

    public class Result
    {
        [Description("Member account number.")]
        public string AccountNo { get; set; } = "";

        [Description("Account status: Unassigned, Active, or Closed.")]
        public string Status { get; set; } = "";

        [Description("Account type: Private, Family, or Company.")]
        public string AccountType { get; set; } = "";

        [Description("Account description.")]
        public string Description { get; set; } = "";

        [Description("Main contact number.")]
        public string MainContactNo { get; set; } = "";

        [Description("Main contact name — usually the member's display name.")]
        public string MainContactName { get; set; } = "";

        [Description("Loyalty club code.")]
        public string ClubCode { get; set; } = "";

        [Description("Loyalty club description.")]
        public string ClubDescription { get; set; } = "";

        [Description("Loyalty scheme (tier) code, e.g. the member's level in the club.")]
        public string SchemeCode { get; set; } = "";

        [Description("Loyalty scheme (tier) description.")]
        public string SchemeDescription { get; set; } = "";

        [Description("BC customer number the account is linked to, when any.")]
        public string LinkedToCustomerNo { get; set; } = "";

        [Description("True when the account is blocked.")]
        public bool Blocked { get; set; }

        [Description("Reason code when blocked.")]
        public string ReasonBlocked { get; set; } = "";

        [Description("Date blocked (yyyy-MM-dd) when blocked.")]
        public string DateBlocked { get; set; } = "";

        [Description("Date the account was created (yyyy-MM-dd).")]
        public string CreatedDate { get; set; } = "";

        [Description("Date the account was activated (yyyy-MM-dd).")]
        public string DateActivated { get; set; } = "";

        [Description("Number of contacts on the account.")]
        public int NoOfContacts { get; set; }

        [Description("Current point balance — open remaining points on the account.")]
        public decimal PointsBalance { get; set; }

        [Description("Total points ever issued (sales + positive adjustments + transfers in).")]
        public decimal TotalIssuedPoints { get; set; }

        [Description("Issued points of type Award Points.")]
        public decimal IssuedAwardPoints { get; set; }

        [Description("Issued points of type Other Points.")]
        public decimal IssuedOtherPoints { get; set; }

        [Description("Points used (redemptions + negative adjustments + transfers out). Usually negative.")]
        public decimal UsedPoints { get; set; }

        [Description("Points lost to expiry. Usually negative.")]
        public decimal ExpiredPoints { get; set; }

        [Description("Member's total recorded sales amount in the company's local currency.")]
        public decimal TotalSalesLcy { get; set; }

        [Description("Date of the member's most recent sale (yyyy-MM-dd).")]
        public string LastSalesDate { get; set; } = "";

        [Description("Open points that expire within the next 30 days.")]
        public decimal PointsExpiring30Days { get; set; }

        [Description("Open points that expire within the next 90 days.")]
        public decimal PointsExpiring90Days { get; set; }

        [Description("Contacts (people) on the account.")]
        public List<ContactRow> Contacts { get; set; } = new();

        [Description("Membership cards on the account.")]
        public List<CardRow> Cards { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.AccountNo) && string.IsNullOrWhiteSpace(args.CardNo) &&
            string.IsNullOrWhiteSpace(args.ContactNo) && string.IsNullOrWhiteSpace(args.MobileNo) &&
            string.IsNullOrWhiteSpace(args.Email))
            throw new ToolValidationException(
                "erp_bc.retail.get_member",
                "Provide accountNo, cardNo, contactNo, mobileNo, or email to identify the member.");

        return await BcClient.ExecuteAsync<Result>(
            "GetMember", args, "erp_bc.retail.get_member", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.lookup_member
// ---------------------------------------------------------------------------

/// <summary>
/// One-round-trip member resolution: exact identifier or fuzzy name/mobile/e-mail
/// search; unique match returns the full member detail with resolved=true.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.lookup_member",
    Description = "Preferred for member questions ('عضو', 'نقاط العميل', loyalty member). Resolve an LS Central loyalty member by exact identifier (accountNo, cardNo, contactNo, mobileNo, email) OR fuzzy search (nameContains, mobileNoContains, emailContains) in ONE call. When exactly one account matches, returns the full member detail (same shape as get_member, including point balances) with resolved=true. When several match, returns resolved=false with a short candidate list — ask the user to pick or narrow. Use find_members only when the user wants a browse list.",
    Sensitivity = "read")]
public class LookupMember : ToolHandler<LookupMember.Args, LookupMember.Result>
{
    public class Args : GetMember.Args
    {
        [Description("Partial contact name to search when no exact identifier is known.")]
        public string NameContains { get; set; } = "";

        [Description("Partial mobile number to search (e.g. the last digits the user remembers).")]
        public string MobileNoContains { get; set; } = "";

        [Description("Partial e-mail address to search.")]
        public string EmailContains { get; set; } = "";

        [Description("Optional. Max candidates when the search is ambiguous (default 25, max 100).")]
        public int? Top { get; set; }
    }

    public class Result : GetMember.Result
    {
        [Description("True when a single member account was identified and full details are in this object.")]
        public bool Resolved { get; set; }

        [Description("Number of candidate contacts when resolved is false.")]
        public int Count { get; set; }

        [Description("Candidate list (contact-level rows) when resolved is false. Ask the user to pick one.")]
        public List<FindMembers.Match> Members { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.AccountNo) && string.IsNullOrWhiteSpace(args.CardNo) &&
            string.IsNullOrWhiteSpace(args.ContactNo) && string.IsNullOrWhiteSpace(args.MobileNo) &&
            string.IsNullOrWhiteSpace(args.Email) && string.IsNullOrWhiteSpace(args.NameContains) &&
            string.IsNullOrWhiteSpace(args.MobileNoContains) && string.IsNullOrWhiteSpace(args.EmailContains))
            throw new ToolValidationException(
                "erp_bc.retail.lookup_member",
                "Provide accountNo, cardNo, contactNo, mobileNo, email, nameContains, mobileNoContains, or emailContains.");

        return await BcClient.ExecuteAsync<Result>(
            "ResolveMember", args, "erp_bc.retail.lookup_member", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.find_members
// ---------------------------------------------------------------------------

/// <summary>
/// Searches LS Central member contacts by name, mobile, e-mail, club, or scheme.
/// Contact-level rows; points live on the account (get_member / get_member_points).
/// </summary>
[Tool(
    Key         = "erp_bc.retail.find_members",
    Description = "Search LS Central loyalty members (LSC Member Contact) by partial name, mobile number, e-mail, account, club, or scheme — returns contact-level rows with accountNo as a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Use when the user wants a LIST of members (e.g. 'members of club X', 'members named Ahmad'); for one member's detail and points prefer lookup_member. At least one filter is required.",
    Sensitivity = "read")]
public class FindMembers : PaginatedToolHandler<FindMembers.Args, FindMembers.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Partial contact name to search (case-insensitive).")]
        public string NameContains { get; set; } = "";

        [Description("Exact mobile phone number.")]
        public string MobileNo { get; set; } = "";

        [Description("Partial mobile phone number (case-insensitive).")]
        public string MobileNoContains { get; set; } = "";

        [Description("Exact e-mail address.")]
        public string Email { get; set; } = "";

        [Description("Partial e-mail address (case-insensitive).")]
        public string EmailContains { get; set; } = "";

        [Description("Member account number — lists the contacts on one account.")]
        public string AccountNo { get; set; } = "";

        [Description("Loyalty club code to filter by.")]
        public string ClubCode { get; set; } = "";

        [Description("Loyalty scheme (tier) code to filter by.")]
        public string SchemeCode { get; set; } = "";
    }

    public class Match
    {
        [Description("Member account number the contact belongs to — points are held here.")]
        public string AccountNo { get; set; } = "";

        [Description("Member contact number.")]
        public string ContactNo { get; set; } = "";

        [Description("Contact name.")]
        public string Name { get; set; } = "";

        [Description("Mobile phone number.")]
        public string MobilePhoneNo { get; set; } = "";

        [Description("Phone number.")]
        public string PhoneNo { get; set; } = "";

        [Description("E-mail address.")]
        public string Email { get; set; } = "";

        [Description("City.")]
        public string City { get; set; } = "";

        [Description("Loyalty club code.")]
        public string ClubCode { get; set; } = "";

        [Description("Loyalty scheme (tier) code.")]
        public string SchemeCode { get; set; } = "";

        [Description("True when this is the account's main contact.")]
        public bool MainContact { get; set; }

        [Description("True when the contact is blocked.")]
        public bool Blocked { get; set; }
    }

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Members { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.retail.find_members";
        const int GatewayMaxTop = 500;

        if (string.IsNullOrWhiteSpace(args.NameContains) && string.IsNullOrWhiteSpace(args.MobileNo) &&
            string.IsNullOrWhiteSpace(args.MobileNoContains) && string.IsNullOrWhiteSpace(args.Email) &&
            string.IsNullOrWhiteSpace(args.EmailContains) && string.IsNullOrWhiteSpace(args.AccountNo) &&
            string.IsNullOrWhiteSpace(args.ClubCode) && string.IsNullOrWhiteSpace(args.SchemeCode))
            throw new ToolValidationException(
                ToolKey,
                "Provide nameContains, mobileNo/mobileNoContains, email/emailContains, accountNo, clubCode, or schemeCode to narrow member search.");

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindMembers", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindMembers", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Members,
            NextCursor = page.HasMore ? (offset + page.Members.Count).ToString() : null,
            Total      = null,
        };
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_member_points
// ---------------------------------------------------------------------------

/// <summary>
/// Point balance summary plus point transaction history (LSC Member Point Entry)
/// for one member account, newest first.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_member_points",
    Description = "Point balance and point TRANSACTION HISTORY for one LS Central loyalty member (LSC Member Point Entry, newest first). Identify the member by accountNo, cardNo, contactNo, mobileNo, or email. Returns the summary (pointsBalance, totalIssuedPoints, usedPoints, expiredPoints, pointsExpiring30/90Days) plus individual point entries — each with date, entryType (Sales, Redemption, Expire, Positive Adjmt., Negative Adjmt, Transfer From/To), points, remaining points, expiration date, and the store/receipt that earned them. Filter with entryType and/or fromDate/toDate; page with top (default 50, max 200) + skip when hasMore. For just the balance, get_member/lookup_member already include the summary.",
    Sensitivity = "read")]
public class GetMemberPoints : ToolHandler<GetMemberPoints.Args, GetMemberPoints.Result>
{
    public class Args : GetMember.Args
    {
        [Description("Optional. Filter entries by type: Sales, Redemption, Expire, Positive Adjmt., Negative Adjmt, Transfer From, or Transfer To.")]
        public string EntryType { get; set; } = "";

        [Description("Optional. Earliest entry date (yyyy-MM-dd), inclusive.")]
        public string FromDate { get; set; } = "";

        [Description("Optional. Latest entry date (yyyy-MM-dd), inclusive.")]
        public string ToDate { get; set; } = "";

        [Description("Optional. Max entries to return (default 50, max 200).")]
        public int? Top { get; set; }

        [Description("Optional. Entries to skip for paging (with hasMore=true).")]
        public int? Skip { get; set; }
    }

    public class Entry
    {
        [Description("Point entry number.")]
        public int EntryNo { get; set; }

        [Description("Entry date (yyyy-MM-dd).")]
        public string Date { get; set; } = "";

        [Description("Entry type: Sales, Redemption, Expire, Positive Adjmt., Negative Adjmt, Transfer From, or Transfer To.")]
        public string EntryType { get; set; } = "";

        [Description("Point type: Award Points or Other Points.")]
        public string PointType { get; set; } = "";

        [Description("Points on the entry — positive when earned, negative when used/expired.")]
        public decimal Points { get; set; }

        [Description("Remaining (unspent) points on this entry.")]
        public decimal RemainingPoints { get; set; }

        [Description("True while the entry still has remaining points.")]
        public bool Open { get; set; }

        [Description("Date the remaining points expire (yyyy-MM-dd).")]
        public string ExpirationDate { get; set; } = "";

        [Description("Where the entry came from, e.g. POS, Journal.")]
        public string SourceType { get; set; } = "";

        [Description("Source document number.")]
        public string DocumentNo { get; set; } = "";

        [Description("Member contact on the entry.")]
        public string ContactNo { get; set; } = "";

        [Description("Membership card used.")]
        public string CardNo { get; set; } = "";

        [Description("Store where the points were earned/used.")]
        public string StoreNo { get; set; } = "";

        [Description("POS terminal of the transaction.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("POS transaction number — with storeNo + posTerminalNo identifies the receipt (get_pos_transaction).")]
        public int TransactionNo { get; set; }
    }

    public class Result
    {
        [Description("Member account number.")]
        public string AccountNo { get; set; } = "";

        [Description("Account description.")]
        public string Description { get; set; } = "";

        [Description("Loyalty club code.")]
        public string ClubCode { get; set; } = "";

        [Description("Loyalty scheme (tier) code.")]
        public string SchemeCode { get; set; } = "";

        [Description("Current point balance — open remaining points on the account.")]
        public decimal PointsBalance { get; set; }

        [Description("Total points ever issued.")]
        public decimal TotalIssuedPoints { get; set; }

        [Description("Issued points of type Award Points.")]
        public decimal IssuedAwardPoints { get; set; }

        [Description("Issued points of type Other Points.")]
        public decimal IssuedOtherPoints { get; set; }

        [Description("Points used (redemptions/adjustments/transfers out). Usually negative.")]
        public decimal UsedPoints { get; set; }

        [Description("Points lost to expiry. Usually negative.")]
        public decimal ExpiredPoints { get; set; }

        [Description("Member's total recorded sales amount in the company's local currency.")]
        public decimal TotalSalesLcy { get; set; }

        [Description("Date of the member's most recent sale (yyyy-MM-dd).")]
        public string LastSalesDate { get; set; } = "";

        [Description("Open points that expire within the next 30 days.")]
        public decimal PointsExpiring30Days { get; set; }

        [Description("Open points that expire within the next 90 days.")]
        public decimal PointsExpiring90Days { get; set; }

        [Description("Number of point entries returned.")]
        public int EntryCount { get; set; }

        [Description("Entry cap applied.")]
        public int Top { get; set; }

        [Description("Entries skipped (paging offset).")]
        public int Skip { get; set; }

        [Description("True when more entries match beyond this page — repeat with skip = skip + entryCount.")]
        public bool HasMore { get; set; }

        [Description("Point entries, newest first.")]
        public List<Entry> Entries { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.AccountNo) && string.IsNullOrWhiteSpace(args.CardNo) &&
            string.IsNullOrWhiteSpace(args.ContactNo) && string.IsNullOrWhiteSpace(args.MobileNo) &&
            string.IsNullOrWhiteSpace(args.Email))
            throw new ToolValidationException(
                "erp_bc.retail.get_member_points",
                "Provide accountNo, cardNo, contactNo, mobileNo, or email to identify the member.");

        return await BcClient.ExecuteAsync<Result>(
            "GetMemberPoints", args, "erp_bc.retail.get_member_points", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_sales_by_tender
// ---------------------------------------------------------------------------

/// <summary>
/// Payment-method breakdown: sums LSC Trans. Payment Entry per tender type
/// server-side. Amounts are LCY, negated so positive = money received.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_sales_by_tender",
    Description = "Sales by PAYMENT METHOD (طريقة الدفع) — how much was paid by Cash vs Card vs Voucher etc., aggregated server-side per tender type and ranked by amount. Scope with storeNo/storeNameContains, posTerminalNo, staffId, and/or a date range (a date range and/or a store is required). Amounts are in the company's local currency (currencyCode in the result); positive = received, refunds reduce the totals. ALWAYS present tenders by tenderDescription, never the code alone. Use this instead of run_sql for 'sales by tender' reports.",
    Sensitivity = "read")]
public class GetSalesByTender : ToolHandler<GetSalesByTender.Args, GetSalesByTender.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description(BcToolDescriptions.StoreNo)]
        public string StoreNo { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. POS terminal number to scope to one terminal.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("Optional. Staff ID to scope to one cashier.")]
        public string StaffId { get; set; } = "";

        [Description("Earliest date (yyyy-MM-dd), inclusive. A date range and/or a store is required.")]
        public string FromDate { get; set; } = "";

        [Description("Latest date (yyyy-MM-dd), inclusive.")]
        public string ToDate { get; set; } = "";
    }

    public class TenderRow
    {
        [Description("Tender type code, e.g. 1, CASH, CARD.")]
        public string TenderType { get; set; } = "";

        [Description("Tender description, e.g. 'Card', 'Voucher', 'قسيمة استرجاع فقط'. ALWAYS show this, not the code.")]
        public string TenderDescription { get; set; } = "";

        [Description("Amount received in this tender, LCY. Positive = received; refunds reduce it.")]
        public decimal AmountLcy { get; set; }

        [Description("Number of payment lines behind the amount.")]
        public int PaymentLineCount { get; set; }
    }

    public class Result
    {
        [Description("Store scope, when given.")]
        public string StoreNo { get; set; } = "";

        [Description("Start of the period.")]
        public string FromDate { get; set; } = "";

        [Description("End of the period.")]
        public string ToDate { get; set; } = "";

        [Description("Currency of every amount in this result (the company's local currency). Always state it.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Number of tender rows.")]
        public int Count { get; set; }

        [Description("Sum over all tenders, LCY.")]
        public decimal TotalAmountLcy { get; set; }

        [Description("Per-tender amounts, largest first.")]
        public List<TenderRow> Tenders { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.FromDate) && string.IsNullOrWhiteSpace(args.ToDate) &&
            string.IsNullOrWhiteSpace(args.StoreNo) && string.IsNullOrWhiteSpace(args.StoreNameContains))
            throw new ToolValidationException(
                "erp_bc.retail.get_sales_by_tender",
                "Provide fromDate/toDate and/or a store (storeNo or storeNameContains) to bound the breakdown.");

        return await BcClient.ExecuteAsync<Result>(
            "GetSalesByTender", args, "erp_bc.retail.get_sales_by_tender", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_staff_sales
// ---------------------------------------------------------------------------

/// <summary>
/// Per-cashier sales for a period, computed from LS Central's own staff
/// statistics FlowFields and ranked by net turnover.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_staff_sales",
    Description = "Sales per STAFF / cashier (مبيعات الموظفين) for a period — net turnover, received payment, discount, transaction count, items sold, voided count, and average transaction value per staff member, ranked by net turnover. Requires fromDate (and usually toDate); scope with storeNo/storeNameContains or a single staffId. Amounts are LCY (currencyCode in the result); positive = sales. Use for 'best cashier', 'sales by employee', commissions, and performance questions — never aggregate receipts yourself.",
    Sensitivity = "read")]
public class GetStaffSales : ToolHandler<GetStaffSales.Args, GetStaffSales.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description(BcToolDescriptions.StoreNo)]
        public string StoreNo { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. Staff ID to report a single cashier.")]
        public string StaffId { get; set; } = "";

        [Description("Earliest date (yyyy-MM-dd), inclusive. Required.")]
        public string FromDate { get; set; } = "";

        [Description("Latest date (yyyy-MM-dd), inclusive. Defaults to open-ended.")]
        public string ToDate { get; set; } = "";

        [Description("Optional. Max staff rows (default 50, max 200), ranked by net turnover.")]
        public int? Top { get; set; }
    }

    public class StaffRow
    {
        [Description("Staff ID.")]
        public string StaffId { get; set; } = "";

        [Description("First name.")]
        public string FirstName { get; set; } = "";

        [Description("Last name.")]
        public string LastName { get; set; } = "";

        [Description("Name printed on receipts.")]
        public string NameOnReceipt { get; set; } = "";

        [Description("The staff member's home store.")]
        public string HomeStoreNo { get; set; } = "";

        [Description("True when the staff account is blocked.")]
        public bool Blocked { get; set; }

        [Description("Net sales turnover in the period, LCY.")]
        public decimal NetTurnoverLcy { get; set; }

        [Description("Payments received in the period, LCY.")]
        public decimal ReceivedPaymentLcy { get; set; }

        [Description("Discounts given in the period, LCY.")]
        public decimal DiscountAmountLcy { get; set; }

        [Description("Number of sales transactions.")]
        public decimal SalesTransactionCount { get; set; }

        [Description("Number of items sold.")]
        public decimal ItemsSold { get; set; }

        [Description("Number of voided transactions — a fraud/training indicator.")]
        public int VoidedTransactionCount { get; set; }

        [Description("Average net turnover per transaction, LCY.")]
        public decimal AvgTransactionLcy { get; set; }
    }

    public class Result
    {
        [Description("Store scope, when given.")]
        public string StoreNo { get; set; } = "";

        [Description("Start of the period.")]
        public string FromDate { get; set; } = "";

        [Description("End of the period.")]
        public string ToDate { get; set; } = "";

        [Description("Currency of every amount in this result (the company's local currency). Always state it.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Number of staff rows returned.")]
        public int Count { get; set; }

        [Description("True when more active staff exist beyond top — raise top to see them.")]
        public bool Truncated { get; set; }

        [Description("Per-staff sales, ranked by net turnover (highest first).")]
        public List<StaffRow> Staff { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.FromDate) && string.IsNullOrWhiteSpace(args.ToDate))
            throw new ToolValidationException(
                "erp_bc.retail.get_staff_sales", "fromDate (and optionally toDate) is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetStaffSales", args, "erp_bc.retail.get_staff_sales", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.find_staff
// ---------------------------------------------------------------------------

/// <summary>Lists LS Central POS staff (cashiers) by store, name, or ID.</summary>
[Tool(
    Key         = "erp_bc.retail.find_staff",
    Description = "List LS Central POS staff (cashiers): ID, name, receipt name, home store, blocked. Filter by storeNo/storeNameContains, partial name (nameContains), or staffId; blocked staff are hidden unless includeBlocked=true. Use to resolve a cashier's name to a Staff ID before get_staff_sales, or to answer 'who works at branch X'. For sales figures use get_staff_sales, not this.",
    Sensitivity = "read")]
public class FindStaff : ToolHandler<FindStaff.Args, FindStaff.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description(BcToolDescriptions.StoreNo)]
        public string StoreNo { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. Partial staff name (first, last, or receipt name; case-insensitive).")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Exact staff ID.")]
        public string StaffId { get; set; } = "";

        [Description("Optional. When true, include blocked staff. Default false.")]
        public bool? IncludeBlocked { get; set; }

        [Description("Optional. Max rows (default 50, max 200).")]
        public int? Top { get; set; }
    }

    public class Match
    {
        [Description("Staff ID.")]
        public string StaffId { get; set; } = "";

        [Description("First name.")]
        public string FirstName { get; set; } = "";

        [Description("Last name.")]
        public string LastName { get; set; } = "";

        [Description("Name printed on receipts.")]
        public string NameOnReceipt { get; set; } = "";

        [Description("Home store number.")]
        public string StoreNo { get; set; } = "";

        [Description("True when blocked.")]
        public bool Blocked { get; set; }
    }

    public class Result
    {
        [Description("Number of rows returned.")]
        public int Count { get; set; }

        [Description("True when more staff match beyond top.")]
        public bool HasMore { get; set; }

        [Description("Matching staff.")]
        public List<Match> Staff { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx) =>
        await BcClient.ExecuteAsync<Result>("FindStaff", args, "erp_bc.retail.find_staff", ctx);
}

// ---------------------------------------------------------------------------
// bc.retail.get_hourly_sales
// ---------------------------------------------------------------------------

/// <summary>
/// Sales distribution across the 24 hours of the day for a store/period —
/// peak-hours and staffing analysis, aggregated server-side.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_hourly_sales",
    Description = "Hourly sales DISTRIBUTION (peak hours / ساعات الذروة): transaction count, gross/net amount, and discount per hour of day (24 rows) for a period, plus period totals. fromDate is required; toDate defaults to the same day; scope with storeNo/storeNameContains (required for ranges over 31 days, max 366). Amounts are LCY (currencyCode in the result); positive = sales. Use for 'busiest hour', 'peak hours', staffing questions — never derive this from individual receipts.",
    Sensitivity = "read")]
public class GetHourlySales : ToolHandler<GetHourlySales.Args, GetHourlySales.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description(BcToolDescriptions.StoreNo)]
        public string StoreNo { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Earliest date (yyyy-MM-dd), inclusive. Required.")]
        public string FromDate { get; set; } = "";

        [Description("Latest date (yyyy-MM-dd), inclusive. Defaults to fromDate (one day).")]
        public string ToDate { get; set; } = "";
    }

    public class HourRow
    {
        [Description("Hour of day, 0-23.")]
        public int Hour { get; set; }

        [Description("Human label, e.g. '09:00-10:00'.")]
        public string HourLabel { get; set; } = "";

        [Description("Sales transactions started in this hour.")]
        public int TransactionCount { get; set; }

        [Description("Gross sales in this hour, LCY.")]
        public decimal GrossAmountLcy { get; set; }

        [Description("Net sales in this hour, LCY.")]
        public decimal NetAmountLcy { get; set; }

        [Description("Discounts in this hour, LCY.")]
        public decimal DiscountAmountLcy { get; set; }
    }

    public class Result
    {
        [Description("Store scope, when given.")]
        public string StoreNo { get; set; } = "";

        [Description("Start of the period.")]
        public string FromDate { get; set; } = "";

        [Description("End of the period.")]
        public string ToDate { get; set; } = "";

        [Description("Currency of every amount in this result (the company's local currency). Always state it.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Total sales transactions in the period.")]
        public int TotalTransactionCount { get; set; }

        [Description("Total gross sales, LCY.")]
        public decimal TotalGrossAmountLcy { get; set; }

        [Description("Total net sales, LCY.")]
        public decimal TotalNetAmountLcy { get; set; }

        [Description("One row per hour of day (0-23), in order.")]
        public List<HourRow> Hours { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.FromDate))
            throw new ToolValidationException(
                "erp_bc.retail.get_hourly_sales", "fromDate is required (yyyy-MM-dd).");

        return await BcClient.ExecuteAsync<Result>(
            "GetHourlySales", args, "erp_bc.retail.get_hourly_sales", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.find_statements
// ---------------------------------------------------------------------------

/// <summary>
/// Lists POS end-of-day statements (posted by default) with sales, VAT,
/// income/expenses, and the total counted-vs-recorded difference.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.find_statements",
    Description = "List POS end-of-day STATEMENTS (LSC Posted Statement; end-of-day reconciliation / تقفيل اليومية) — sales amount, VAT, discounts, income/expenses, and totalDifferenceLcy (counted vs recorded — a nonzero value is a cash shortage/overage). Newest first, as a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Filter by storeNo/storeNameContains and/or date range; posted=false lists open (unposted) statements instead. Pass withDifferenceOnly=true (optionally minAbsDifferenceLcy to skip rounding) to return only statements with a cash over/short — e.g. posted=false + withDifferenceOnly=true = open statements that don't balance. Use for 'end of day', 'Z report', 'cash difference', 'unbalanced open statements' questions; drill into one statement's per-tender lines with get_statement.",
    Sensitivity = "read")]
public class FindStatements : PaginatedToolHandler<FindStatements.Args, FindStatements.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description(BcToolDescriptions.StoreNo)]
        public string StoreNo { get; set; } = "";

        [Description(BcToolDescriptions.StoreNameContains)]
        public string StoreNameContains { get; set; } = "";

        [Description("Optional. Filter statement numbers containing this text.")]
        public string StatementNoContains { get; set; } = "";

        [Description("Optional. Earliest statement date (yyyy-MM-dd), inclusive.")]
        public string FromDate { get; set; } = "";

        [Description("Optional. Latest statement date (yyyy-MM-dd), inclusive.")]
        public string ToDate { get; set; } = "";

        [Description("Optional. Default true (posted statements). Set false for open, not-yet-posted statements.")]
        public bool? Posted { get; set; }

        [Description("Optional. When true, return only statements with a cash over/short (totalDifferenceLcy <> 0) — the reconciliation exceptions. Default false = all statements.")]
        public bool? WithDifferenceOnly { get; set; }

        [Description("Optional. Only return statements whose absolute cash difference is at least this amount (LCY). Use to ignore tiny rounding differences, e.g. 1.0. Combine with posted=false to find open statements with real differences.")]
        public decimal? MinAbsDifferenceLcy { get; set; }
    }

    public class Match
    {
        [Description("Statement number.")]
        public string StatementNo { get; set; } = "";

        [Description("Currency of every amount in this row (the company's local currency). Always state it.")]
        public string CurrencyCode { get; set; } = "";

        [Description("True when this is a posted statement.")]
        public bool Posted { get; set; }

        [Description("Store number.")]
        public string StoreNo { get; set; } = "";

        [Description("Store name.")]
        public string StoreName { get; set; } = "";

        [Description("Statement date (yyyy-MM-dd).")]
        public string Date { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Sales amount, LCY.")]
        public decimal SalesAmountLcy { get; set; }

        [Description("VAT amount, LCY.")]
        public decimal VatAmountLcy { get; set; }

        [Description("Total discount, LCY.")]
        public decimal TotalDiscountLcy { get; set; }

        [Description("Income postings, LCY.")]
        public decimal IncomeLcy { get; set; }

        [Description("Expense postings, LCY.")]
        public decimal ExpensesLcy { get; set; }

        [Description("Counted minus recorded across all tender lines, LCY — nonzero means a shortage/overage.")]
        public decimal TotalDifferenceLcy { get; set; }
    }

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Statements { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.retail.find_statements";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindStatements", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindStatements", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Statements,
            NextCursor = page.HasMore ? (offset + page.Statements.Count).ToString() : null,
            Total      = null,
        };
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_statement
// ---------------------------------------------------------------------------

/// <summary>
/// One statement's header plus its per-tender reconciliation lines
/// (counted vs recorded per tender, terminal, and staff).
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_statement",
    Description = "Full detail of ONE POS end-of-day statement: header totals plus per-TENDER reconciliation lines — for each tender/terminal/staff the recorded amount (transAmountLcy), the physically counted amount (countedAmountLcy), and the differenceLcy (positive = overage, negative = shortage). Provide statementNo copied verbatim from find_statements — the number on its own, never joined to the store (not 'S027-2205192') — plus storeNo from the same row, which OPEN statements need because their numbers repeat across stores. posted defaults to true, pass false for an open statement. ALWAYS show tenderTypeName with each line.",
    Sensitivity = "read")]
public class GetStatement : ToolHandler<GetStatement.Args, GetStatement.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Statement number exactly as find_statements returns it (the number on its own — do NOT combine it with the store). Required.")]
        public string StatementNo { get; set; } = "";

        [Description("Optional. Store the statement belongs to, as returned alongside statementNo by find_statements. Open (unposted) statement numbers are only unique per store, so pass this when the same number exists in several stores.")]
        public string StoreNo { get; set; } = "";

        [Description("Optional. Default true (posted statement). Set false for an open statement.")]
        public bool? Posted { get; set; }
    }

    public class Line
    {
        [Description("Staff the drawer/count belongs to, when per-staff.")]
        public string StaffId { get; set; } = "";

        [Description("POS terminal, when per-terminal.")]
        public string PosTerminalNo { get; set; } = "";

        [Description("Tender type code.")]
        public string TenderType { get; set; } = "";

        [Description("Tender name, e.g. Cash, Card. ALWAYS show this with the line.")]
        public string TenderTypeName { get; set; } = "";

        [Description("Currency of the tender line.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Recorded (transaction) amount in the tender currency.")]
        public decimal TransAmount { get; set; }

        [Description("Physically counted amount in the tender currency.")]
        public decimal CountedAmount { get; set; }

        [Description("Counted minus recorded in the tender currency.")]
        public decimal DifferenceAmount { get; set; }

        [Description("Recorded amount, LCY.")]
        public decimal TransAmountLcy { get; set; }

        [Description("Counted amount, LCY.")]
        public decimal CountedAmountLcy { get; set; }

        [Description("Counted minus recorded, LCY — positive = overage, negative = shortage.")]
        public decimal DifferenceLcy { get; set; }
    }

    public class Result
    {
        [Description("Statement number.")]
        public string StatementNo { get; set; } = "";

        [Description("True when posted.")]
        public bool Posted { get; set; }

        [Description("Store number.")]
        public string StoreNo { get; set; } = "";

        [Description("Store name.")]
        public string StoreName { get; set; } = "";

        [Description("Statement date (yyyy-MM-dd).")]
        public string Date { get; set; } = "";

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("First transaction date covered.")]
        public string TransStartingDate { get; set; } = "";

        [Description("Last transaction date covered.")]
        public string TransEndingDate { get; set; } = "";

        [Description("Sales amount, LCY.")]
        public decimal SalesAmountLcy { get; set; }

        [Description("VAT amount, LCY.")]
        public decimal VatAmountLcy { get; set; }

        [Description("Total discount, LCY.")]
        public decimal TotalDiscountLcy { get; set; }

        [Description("Line discount, LCY.")]
        public decimal LineDiscountLcy { get; set; }

        [Description("Income postings, LCY.")]
        public decimal IncomeLcy { get; set; }

        [Description("Expense postings, LCY.")]
        public decimal ExpensesLcy { get; set; }

        [Description("Total counted-minus-recorded difference, LCY.")]
        public decimal TotalDifferenceLcy { get; set; }

        [Description("Number of tender lines.")]
        public int LineCount { get; set; }

        [Description("Per-tender reconciliation lines.")]
        public List<Line> Lines { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.StatementNo))
            throw new ToolValidationException(
                "erp_bc.retail.get_statement", "statementNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetStatement", args, "erp_bc.retail.get_statement", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.find_offers
// ---------------------------------------------------------------------------

/// <summary>
/// Lists LS Central promotions (LSC Periodic Discount): enabled offers active
/// on a date, optionally scoped to one item.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.find_offers",
    Description = "List retail PROMOTIONS / offers (LSC Periodic Discount; عروض / تخفيضات): Multibuy, Mix&Match, Disc. Offer, Total Discount, Tender Type, Item Point, Line Discount — as a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). The three LS Central pages are this list filtered by offerType: 'Discount Offer List' = offerType 'Disc. Offer', 'Line Discount Offer List' = 'Line Discount', 'MixMatch Offer List' = 'Mix&Match'. Defaults to ENABLED offers — those pages show disabled ones too, so pass status 'Any' to reproduce a page in full. Pass activeOn (yyyy-MM-dd, e.g. today) to keep only offers whose start/end dates cover that date; validToday on each row is the page's 'Valid Today' (dates + weekday + hours checked against now). Filter by partial description (nameContains) or itemNo ('is there an offer on item X' — matches item-specific offer lines). includeStatistics=true adds lifetime salesQty/salesLcy/profitLcy per offer. A blank start/end date means open-ended; validationPeriodId can further restrict weekdays/hours. For one offer's ITEMS and line groups use get_offer; for the resulting PRICES of an offer's items use get_item_prices with offerNo.",
    Sensitivity = "read")]
public class FindOffers : PaginatedToolHandler<FindOffers.Args, FindOffers.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Partial offer description (case-insensitive).")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Enabled (default), Disabled, or Any.")]
        public string Status { get; set; } = "";

        [Description("Optional. Offer type: Multibuy, Mix&Match, Disc. Offer, Total Discount, Tender Type, Item Point, or Line Discount.")]
        public string OfferType { get; set; } = "";

        [Description("Optional. Item number — only offers with an item-specific line for this item.")]
        public string ItemNo { get; set; } = "";

        [Description("Optional. Date (yyyy-MM-dd) the offer must be active on — pass today for 'current promotions'.")]
        public string ActiveOn { get; set; } = "";

        [Description("Optional. When true, add lifetime sales statistics (salesQty, salesLcy, profitLcy, profitPct) per offer — slower. Default false.")]
        public bool? IncludeStatistics { get; set; }
    }

    public class Match
    {
        [Description("Offer number.")]
        public string OfferNo { get; set; } = "";

        [Description("Offer description.")]
        public string Description { get; set; } = "";

        [Description("Offer type: Multibuy, Mix&Match, Disc. Offer, Total Discount, Tender Type, Item Point, Line Discount.")]
        public string OfferType { get; set; } = "";

        [Description("Enabled or Disabled.")]
        public string Status { get; set; } = "";

        [Description("Priority when several offers apply (lower wins in LS).")]
        public int Priority { get; set; }

        [Description("Price group the offer is valid in; ALL = every store.")]
        public string PriceGroup { get; set; } = "";

        [Description("Benefit form: Deal Price, Discount %, Discount Amount, Least Expensive, or Line spec.")]
        public string DiscountType { get; set; } = "";

        [Description("First valid date (from the validation period); empty = open-ended.")]
        public string StartingDate { get; set; } = "";

        [Description("Last valid date (from the validation period); empty = open-ended.")]
        public string EndingDate { get; set; } = "";

        [Description("Validation period restricting weekdays/hours, when set.")]
        public string ValidationPeriodId { get; set; } = "";

        [Description("Validation period description.")]
        public string ValidationDescription { get; set; } = "";

        [Description("Discount percentage, for %-type offers.")]
        public decimal DiscountPctValue { get; set; }

        [Description("Deal price, for deal-price offers.")]
        public decimal DealPriceValue { get; set; }

        [Description("Discount amount, for amount-off offers.")]
        public decimal DiscountAmountValue { get; set; }

        [Description("Loyalty club/scheme the offer is limited to; empty = everyone.")]
        public string MemberValue { get; set; } = "";

        [Description("The offer list page's 'Valid Today': the validation period's dates, weekday and hours checked against now. Independent of status — a Disabled offer can be validToday and still discount nothing.")]
        public bool ValidToday { get; set; }

        [Description("Mix&Match: whether any line pops a message up on the POS (the 'Triggers Pop-up on POS' column).")]
        public bool TriggersPopUpOnPos { get; set; }

        [Description("Line Discount offers: Automatic (the POS applies it by itself), Manual-Line or Manual-Trans. (a cashier must trigger it).")]
        public string LineDiscountExecution { get; set; } = "";

        [Description("Disc. Offer: when true, no Line Discount offer stacks on top of it.")]
        public bool BlockLineDiscountOffer { get; set; }

        [Description("Cap on the discount amount per application; 0 = no cap.")]
        public decimal MaximumDiscountAmount { get; set; }

        [Description("Lifetime quantity sold on the offer (with includeStatistics).")]
        public decimal? SalesQty { get; set; }

        [Description("Lifetime sales amount, LCY (with includeStatistics).")]
        public decimal? SalesLcy { get; set; }

        [Description("Lifetime profit (sales - COGS), LCY (with includeStatistics).")]
        public decimal? ProfitLcy { get; set; }

        [Description("Profit percentage of sales (with includeStatistics).")]
        public decimal? ProfitPct { get; set; }
    }

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Offers { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.retail.find_offers";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindOffers", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindOffers", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Offers,
            NextCursor = page.HasMore ? (offset + page.Offers.Count).ToString() : null,
            Total      = null,
        };
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_offer
// ---------------------------------------------------------------------------

/// <summary>
/// One promotion's full detail: header, lifetime sales statistics, and the
/// offer lines (items / line groups) — e.g. what a Mix&amp;Match bundle contains.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_offer",
    Description = "Full detail of ONE promotion (LSC Periodic Discount) by offerNo — header, lifetime sales statistics (salesQty, salesLcy, profitLcy, profitPct), and the offer LINES: the items (or product groups/categories) in the offer, their line group, standard price, and deal price / discount % (dealPriceOrDiscPct is always a PERCENT; the deal price is offerPriceIncludingVat; exclude=true lines REMOVE their item from the offer). For Mix&Match, noOfLinesToTrigger + lineGroup show how the bundle is composed ('buy N from group A'). Use after find_offers when the user asks what an offer includes or how it performs. For what the offer's items actually COST now (shelf price and price after this and any other discount) use get_item_prices with offerNo — a line's stored offer price can be stale.",
    Sensitivity = "read")]
public class GetOffer : ToolHandler<GetOffer.Args, GetOffer.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Offer number (LSC Periodic Discount No.), e.g. from find_offers. Required.")]
        public string OfferNo { get; set; } = "";
    }

    public class Line
    {
        [Description("Line number.")]
        public int LineNo { get; set; }

        [Description("Line group — Mix&Match lines with the same group are interchangeable.")]
        public string LineGroup { get; set; } = "";

        [Description("What the line targets: Item, Product Group, Item Category, All, or Special Group.")]
        public string Type { get; set; } = "";

        [Description("Item / group / category number the line targets.")]
        public string No { get; set; } = "";

        [Description("Variant code, when variant-specific.")]
        public string VariantCode { get; set; } = "";

        [Description("Line description.")]
        public string Description { get; set; } = "";

        [Description("Unit of measure.")]
        public string UnitOfMeasure { get; set; } = "";

        [Description("Standard price including VAT before the offer (as recorded when the line was entered).")]
        public decimal StandardPriceIncludingVat { get; set; }

        [Description("Standard price excluding VAT before the offer.")]
        public decimal StandardPrice { get; set; }

        [Description("Benefit form of the line: Deal Price or Disc. %.")]
        public string DiscType { get; set; } = "";

        [Description("The discount PERCENT of the line — always a percent, even when discType is Deal Price (LS Central stores the deal price as a percent off the standard price). The deal price itself is offerPriceIncludingVat.")]
        public decimal DealPriceOrDiscPct { get; set; }

        [Description("Resulting offer price including VAT — the deal price for a Deal Price line.")]
        public decimal OfferPriceIncludingVat { get; set; }

        [Description("Resulting offer price excluding VAT.")]
        public decimal OfferPrice { get; set; }

        [Description("Discount amount including VAT taken off one unit. For a Deal Price line this fixed amount is what the POS subtracts, so a later shelf-price change does not move the deal price.")]
        public decimal DiscountAmountIncludingVat { get; set; }

        [Description("Discount amount excluding VAT.")]
        public decimal DiscountAmount { get; set; }

        [Description("Items needed from this line to trigger the offer.")]
        public int NoOfItemsNeeded { get; set; }

        [Description("True when this line EXCLUDES its item/group from the offer rather than including it.")]
        public bool Exclude { get; set; }

        [Description("Whether this line pops a message up on the POS when it triggers.")]
        public bool TriggerPopUpOnPos { get; set; }

        [Description("Line status: Enabled or Disabled.")]
        public string Status { get; set; } = "";
    }

    public class Result : FindOffers.Match
    {
        [Description("Lines needed to trigger the offer (Mix&Match).")]
        public decimal NoOfLinesToTrigger { get; set; }

        [Description("Number of least-expensive items discounted, for least-expensive offers.")]
        public int NoOfLeastExpensiveItems { get; set; }

        [Description("Whether memberValue refers to a loyalty Scheme or Club.")]
        public string MemberType { get; set; } = "";

        [Description("Currency of the offer's deal prices; the company's local currency when not set.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Number of offer lines.")]
        public int LineCount { get; set; }

        [Description("The offer lines — items / groups in the promotion.")]
        public List<Line> Lines { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OfferNo))
            throw new ToolValidationException("erp_bc.retail.get_offer", "offerNo is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetOffer", args, "erp_bc.retail.get_offer", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.find_coupons
// ---------------------------------------------------------------------------

/// <summary>
/// Lists LS Central coupons (LSC Coupon Header): store / manufacturer / return
/// coupons redeemed or issued at the POS.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.find_coupons",
    Description = "List COUPONS (LSC Coupon Header; كوبون / قسيمة): Store, Manufacturer, and Return coupons redeemed or issued at the POS — as a DATASET (sample + dataset_ref; full set exportable on demand, never page manually). Defaults to ENABLED coupons; filter by couponType (e.g. Store Coupon), partial description (nameContains), couponIssuer, or activeOn (yyyy-MM-dd — keeps only coupons whose validation-period dates cover that date). includeStatistics=true adds issued/used quantities and amounts. Rows show handling (Tender or Discount), discountType, price group, validity dates, and any loyalty club/scheme restriction. For one coupon's item lines and trigger rules use get_coupon.",
    Sensitivity = "read")]
public class FindCoupons : PaginatedToolHandler<FindCoupons.Args, FindCoupons.Match>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. Partial coupon description (case-insensitive).")]
        public string NameContains { get; set; } = "";

        [Description("Optional. Enabled (default), Disabled, or Any.")]
        public string Status { get; set; } = "";

        [Description("Optional. Coupon type: Store Coupon, Manufacturer Coupon, or Return Coupon.")]
        public string CouponType { get; set; } = "";

        [Description("Optional. Coupon issuer code.")]
        public string CouponIssuer { get; set; } = "";

        [Description("Optional. Date (yyyy-MM-dd) the coupon must be valid on — pass today for currently valid coupons.")]
        public string ActiveOn { get; set; } = "";

        [Description("Optional. When true, add issued/used quantity and amount per coupon — slower. Default false.")]
        public bool? IncludeStatistics { get; set; }
    }

    public class Match
    {
        [Description("Coupon code.")]
        public string Code { get; set; } = "";

        [Description("Coupon description.")]
        public string Description { get; set; } = "";

        [Description("Second description line.")]
        public string Description2 { get; set; } = "";

        [Description("Enabled or Disabled.")]
        public string Status { get; set; } = "";

        [Description("Store Coupon, Manufacturer Coupon, or Return Coupon.")]
        public string CouponType { get; set; } = "";

        [Description("How the POS applies it: Tender (like payment) or Discount.")]
        public string Handling { get; set; } = "";

        [Description("Benefit form: Discount Amount or Discount %.")]
        public string DiscountType { get; set; } = "";

        [Description("Issuer code, when issued by a partner.")]
        public string CouponIssuer { get; set; } = "";

        [Description("Reference number embedded in the coupon barcode.")]
        public string CouponReferenceNo { get; set; } = "";

        [Description("Price group the coupon is valid in; ALL = every store.")]
        public string PriceGroup { get; set; } = "";

        [Description("Validation period ID.")]
        public string ValidationPeriodId { get; set; } = "";

        [Description("Validation period description.")]
        public string ValidationDescription { get; set; } = "";

        [Description("First valid date (from the validation period); empty = open-ended.")]
        public string StartingDate { get; set; } = "";

        [Description("Last valid date (from the validation period); empty = open-ended.")]
        public string EndingDate { get; set; } = "";

        [Description("Whether memberValue refers to a loyalty Scheme or Club.")]
        public string MemberType { get; set; } = "";

        [Description("Loyalty club/scheme the coupon is limited to; empty = everyone.")]
        public string MemberValue { get; set; } = "";

        [Description("Coupons issued, lifetime (with includeStatistics).")]
        public decimal? IssuedQuantity { get; set; }

        [Description("Issued amount, LCY (with includeStatistics).")]
        public decimal? IssuedAmountLcy { get; set; }

        [Description("Coupons used/redeemed, lifetime (with includeStatistics).")]
        public decimal? UsedQuantity { get; set; }

        [Description("Used/redeemed amount, LCY (with includeStatistics).")]
        public decimal? UsedAmountLcy { get; set; }
    }

    private sealed class GatewayPage
    {
        public int Count { get; set; }
        public int? Skip { get; set; }
        public bool HasMore { get; set; }
        public List<Match> Coupons { get; set; } = new();
    }

    public override async Task<DatasetPage<Match>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.retail.find_coupons";
        const int GatewayMaxTop = 500;

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? Math.Min(cursor.PageSize, GatewayMaxTop) : 200;

        var page = await BcClient.ExecuteAsync<GatewayPage>(
            "FindCoupons", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);
        BcPaging.RequireSkipEcho(page.Skip, "FindCoupons", ToolKey);

        return new DatasetPage<Match>
        {
            Rows       = page.Coupons,
            NextCursor = page.HasMore ? (offset + page.Coupons.Count).ToString() : null,
            Total      = null,
        };
    }
}

// ---------------------------------------------------------------------------
// bc.retail.get_coupon
// ---------------------------------------------------------------------------

/// <summary>
/// One coupon's full detail: header, usage statistics, trigger rules, and its
/// item lines (which items it applies to or is issued on).
/// </summary>
[Tool(
    Key         = "erp_bc.retail.get_coupon",
    Description = "Full detail of ONE coupon (LSC Coupon Header) by code — header, issued/used statistics, trigger rules (noOfItemsToTrigger, applyToNoOfItems, affects), and the coupon LINES: listType Use = items the coupon discounts, listType Issue = items whose purchase issues the coupon; exclude=true lines are exceptions. Use after find_coupons when the user asks what a coupon applies to or how much it has been used.",
    Sensitivity = "read")]
public class GetCoupon : ToolHandler<GetCoupon.Args, GetCoupon.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Coupon code (LSC Coupon Header Code), e.g. from find_coupons. Required.")]
        public string Code { get; set; } = "";
    }

    public class Line
    {
        [Description("Line number.")]
        public int LineNo { get; set; }

        [Description("Use = items the coupon discounts; Issue = items whose purchase issues the coupon.")]
        public string ListType { get; set; } = "";

        [Description("What the line targets: Item, Product Group, Item Category, Special Group, or All.")]
        public string Type { get; set; } = "";

        [Description("Item / group / category number the line targets.")]
        public string No { get; set; } = "";

        [Description("Variant code, when variant-specific.")]
        public string VariantCode { get; set; } = "";

        [Description("Line description.")]
        public string Description { get; set; } = "";

        [Description("True when this line is an exclusion.")]
        public bool Exclude { get; set; }
    }

    public class Result : FindCoupons.Match
    {
        [Description("Items required in the basket to trigger the coupon.")]
        public int NoOfItemsToTrigger { get; set; }

        [Description("Number of items the discount applies to.")]
        public int ApplyToNoOfItems { get; set; }

        [Description("Which line the coupon affects: Any Item Line, Last Item Line, or Next Item Line.")]
        public string Affects { get; set; } = "";

        [Description("True when the POS can issue this coupon.")]
        public bool IssueAtPos { get; set; }

        [Description("Loyalty scheme linked to POS issuing, when set.")]
        public string LoyaltyScheme { get; set; } = "";

        [Description("Number of coupon lines.")]
        public int LineCount { get; set; }

        [Description("Use/Issue item lines.")]
        public List<Line> Lines { get; set; } = new();
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Code))
            throw new ToolValidationException("erp_bc.retail.get_coupon", "code is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetCoupon", args, "erp_bc.retail.get_coupon", ctx);
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
    Description = "Count rows of a supported Business Central / LS Central entity, optionally filtered. Set allCompanies=true for a per-company breakdown and grand total. " + BcToolDescriptions.CompanyPriority + " In allCompanies mode 'ASG - HData' still appears in the breakdown, but the headline number should come from 'ASG'.",
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
        [JsonConverter(typeof(TolerantListConverter<Filter>))]
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
    Description = "Sum a numeric field on a supported entity (totals, not row counts). Use preset for common cases: posSales, posNetSales, customerOrderSales, salesInvoices, bcSalesOrders. Pass items[] to sum several sources in one call and read grandTotalByCurrency. Every total is returned as a per-currency breakdown (byCurrency / grandTotalByCurrency); report each currency separately and NEVER add amounts of different currencies together. Always state the currency code with the amount. " + BcToolDescriptions.CompanyPriority,
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
        [JsonConverter(typeof(TolerantListConverter<Filter>))]
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
        [JsonConverter(typeof(TolerantListConverter<Filter>))]
        public List<Filter> Filters { get; set; } = new();

        [Description("When true (default), include matching row count alongside total.")]
        public bool IncludeCount { get; set; } = true;

        [Description("Batch mode: sum several entities/presets in one call.")]
        [JsonConverter(typeof(TolerantListConverter<SumItem>))]
        public List<SumItem> Items { get; set; } = new();
    }

    public class CurrencyTotal
    {
        [Description("ISO currency code the total is denominated in (blank means the company local currency was unset). NEVER add totals with different currency codes together.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Sum total for this currency.")]
        public decimal Total { get; set; }

        [Description("Matching row count for this currency when includeCount=true.")]
        public int Count { get; set; }
    }

    public class ItemResult
    {
        [Description("Optional alias from the request.")]
        public string Alias { get; set; } = "";

        [Description("Entity that was summed.")]
        public string Entity { get; set; } = "";

        [Description("Field that was summed.")]
        public string Field { get; set; } = "";

        [Description("Sum total broken down per currency. One entry per currency found; report each separately and never add across currencies.")]
        public List<CurrencyTotal> ByCurrency { get; set; } = new();
    }

    public class Result
    {
        [Description("Single-sum mode: entity summed.")]
        public string Entity { get; set; } = "";

        [Description("Single-sum mode: field summed.")]
        public string Field { get; set; } = "";

        [Description("Single-sum mode: sum total per currency. One entry per currency; report each separately and never add across currencies.")]
        public List<CurrencyTotal> ByCurrency { get; set; } = new();

        [Description("Batch mode: per-item results (each with its own per-currency breakdown).")]
        public List<ItemResult> Items { get; set; } = new();

        [Description("Batch mode: grand total per currency, summed across all items OF THE SAME CURRENCY. There is no single scalar grand total on purpose — amounts in different currencies are kept apart.")]
        public List<CurrencyTotal> GrandTotalByCurrency { get; set; } = new();
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
    Description = "List/filter rows of a supported Business Central / LS Central entity as a dataset — returns a sample of the rows plus a dataset_ref; the full set can be exported or analyzed on demand, so never page manually. Provide filters as {field, operator, value} and optional field names to return. " + BcToolDescriptions.CompanyPriority + " " + BcToolDescriptions.ItemTypes,
    Sensitivity = "read")]
public class QueryRecords : PaginatedToolHandler<QueryRecords.Args, Dictionary<string, object>>
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
        [JsonConverter(typeof(TolerantListConverter<Filter>))]
        public List<Filter> Filters { get; set; } = new();

        [Description("Optional. Exact Business Central field CAPTIONS to return (e.g. \"No.\", \"Description\", \"Unit Price\", \"Reorder Point\", \"Reorder Quantity\", \"Safety Stock Quantity\"). Must match the caption exactly — an unrecognized name (e.g. \"Reorder Qty.\" or \"Safety Stock\") can make the query return NO rows. When unsure of the exact caption, OMIT this to get the entity's default field set instead of guessing.")]
        [JsonConverter(typeof(TolerantListConverter<string>))]
        public List<string> Fields { get; set; } = new();
    }

    // TRow is Dictionary<string, object>: the row shape depends on the queried entity.
    // The value type is `object`, not JsonElement: some NJsonSchema versions render a
    // Dictionary<string,JsonElement> with an array-valued `additionalProperties`, which
    // fails draft-07 metaschema validation at tool registration ("got array, want
    // boolean or object"). `object` renders as the canonical "any" schema across
    // versions; System.Text.Json still deserializes each value into a JsonElement at
    // runtime. Rows stream in the entity's primary-key order (gateway RecRef default),
    // so the offset cursor pages are stable.

    public override async Task<DatasetPage<Dictionary<string, object>>> FetchPageAsync(
        Args args, DatasetCursor cursor, ToolContext ctx)
    {
        const string ToolKey = "erp_bc.data.query_records";

        if (string.IsNullOrWhiteSpace(args.Entity))
            throw new ToolValidationException(ToolKey, "entity is required.");

        var offset   = BcPaging.ParseOffset(cursor.Token, ToolKey);
        var pageSize = cursor.PageSize > 0 ? cursor.PageSize : 200;

        // Read the raw gateway envelope instead of a fixed POCO. The row array is
        // located tolerantly (BcPaging.ExtractRows tries "rows" and several fallbacks,
        // plus a bare top-level array), so a gateway whose paged Query response uses a
        // different container key does not silently yield an empty dataset.
        var data = await BcClient.ExecuteAsync(
            "Query", BcPaging.WithPaging(args, pageSize, offset), ToolKey, ctx);

        var rows    = BcPaging.ExtractRows<Dictionary<string, object>>(data, BcClient.JsonOptions);
        var skip    = BcPaging.ReadNullableInt(data, "skip");
        var hasMore = BcPaging.ReadBool(data, "hasMore");

        // Diagnostic: an empty page from a gateway that clearly did work is worth a
        // breadcrumb. Logs the echoed paging fields and the envelope's top-level keys
        // so a "sample: []" can be traced to the real cause (genuinely no rows vs. an
        // unrecognised response shape) without guessing.
        if (rows.Count == 0)
            Console.WriteLine(
                $"[bc] query_records '{args.Entity}' returned 0 rows " +
                $"(skip={(skip?.ToString() ?? "null")}, hasMore={hasMore}, keys=[{DescribeKeys(data)}]).");

        // A gateway build that predates paging omits "skip" but still returns rows.
        // Returning them as a single, final page keeps the data instead of throwing
        // (which the platform surfaces as a silent empty dataset).
        return new DatasetPage<Dictionary<string, object>>
        {
            Rows       = rows,
            NextCursor = skip is not null && hasMore ? (offset + rows.Count).ToString() : null,
            Total      = null, // counting would need a second full scan on the gateway
        };
    }

    // Comma-separated top-level property names of the gateway 'data' object, for the
    // empty-page diagnostic above. "(array)" when data is a bare array, "(none)" otherwise.
    private static string DescribeKeys(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
            return "(array)";
        if (data.ValueKind != JsonValueKind.Object)
            return "(none)";
        return string.Join(", ", data.EnumerateObject().Select(p => p.Name));
    }
}

// ---------------------------------------------------------------------------
// bc.finance.create_bank_account
// ---------------------------------------------------------------------------

/// <summary>
/// Creates a Bank Account master record. Note the two confusable arguments:
/// <c>bankAccountNo</c> is the Business Central key (Bank Account."No."),
/// <c>bankAccountNumber</c> is the account number at the bank itself.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.create_bank_account",
    Description = "Create a Business Central bank account (master data). bankAccountNo is the BC record number — omit it to draw the next number from the Bank Account Nos. series; bankAccountNumber is the account number AT THE BANK (a different field). name is required, and bankAccPostingGroup should be set or the account cannot be used in postings. This is setup data: it does not move money or change any balance.",
    Sensitivity = "write")]
public class CreateBankAccount : ToolHandler<CreateBankAccount.Args, CreateBankAccount.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional. The Business Central bank account number (No.), e.g. BANK-SAR-01. Omit to take the next number from the Bank Account Nos. series in General Ledger Setup.")]
        public string BankAccountNo { get; set; } = "";

        [Description("Required. Bank account name as it should appear in Business Central.")]
        public string Name { get; set; } = "";

        [Description("Bank account posting group. Set this — without it the account cannot be used in postings.")]
        public string BankAccPostingGroup { get; set; } = "";

        [Description("Optional currency code (e.g. SAR, AED). Empty means the company's local currency.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Optional. The account number AT THE BANK ('Bank Account No.') — not the BC record number.")]
        public string BankAccountNumber { get; set; } = "";

        [Description("Optional bank branch number.")]
        public string BankBranchNo { get; set; } = "";

        [Description("Optional IBAN.")]
        public string Iban { get; set; } = "";

        [Description("Optional SWIFT/BIC code.")]
        public string SwiftCode { get; set; } = "";

        [Description("Optional country/region code of the bank.")]
        public string CountryRegionCode { get; set; } = "";

        [Description("Optional street address.")]
        public string Address { get; set; } = "";

        [Description("Optional city.")]
        public string City { get; set; } = "";

        [Description("Optional post code.")]
        public string PostCode { get; set; } = "";

        [Description("Optional contact person at the bank.")]
        public string Contact { get; set; } = "";

        [Description("Optional phone number.")]
        public string PhoneNo { get; set; } = "";

        [Description("Optional e-mail address.")]
        public string Email { get; set; } = "";
    }

    public class Result
    {
        [Description("The bank account number (No.) created. Always report this to the user.")]
        public string BankAccountNo { get; set; } = "";

        [Description("Bank account name.")]
        public string Name { get; set; } = "";

        [Description("Bank account posting group; empty means postings will fail until it is set.")]
        public string BankAccPostingGroup { get; set; } = "";

        [Description("Currency code; empty means the company's local currency.")]
        public string CurrencyCode { get; set; } = "";

        [Description("The account number at the bank.")]
        public string BankAccountNumber { get; set; } = "";

        [Description("Bank branch number.")]
        public string BankBranchNo { get; set; } = "";

        [Description("IBAN.")]
        public string Iban { get; set; } = "";

        [Description("SWIFT/BIC code.")]
        public string SwiftCode { get; set; } = "";

        [Description("Country/region code.")]
        public string CountryRegionCode { get; set; } = "";

        [Description("Whether the account is blocked.")]
        public bool Blocked { get; set; }

        [Description("True when the number came from the Bank Account Nos. series rather than the caller.")]
        public bool NumberFromNoSeries { get; set; }
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Name))
            throw new ToolValidationException(
                "erp_bc.finance.create_bank_account", "name is required.");

        BcCallerContext.Require(ctx, "erp_bc.finance.create_bank_account");
        return await BcClient.ExecuteAsync<Result>(
            "CreateBankAccount", args, "erp_bc.finance.create_bank_account", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.create_journal_batch
// ---------------------------------------------------------------------------

/// <summary>
/// Creates the (empty, unposted) general journal batch that
/// <see cref="AddJournalLine"/> writes lines into.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.create_journal_batch",
    Description = "Create an empty general journal batch to stage accounting entries in. The batch is UNPOSTED — add lines with add_journal_line, then a person reviews and posts it in Business Central. The gateway cannot post journals. templateName defaults to the first General journal template.",
    Sensitivity = "write")]
public class CreateJournalBatch : ToolHandler<CreateJournalBatch.Args, CreateJournalBatch.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional general journal template name. Omit to use the first template of type General.")]
        public string TemplateName { get; set; } = "";

        [Description("Required. Name for the new batch (max 10 characters), e.g. AI-0725.")]
        public string BatchName { get; set; } = "";

        [Description("Optional description of what the batch is for.")]
        public string Description { get; set; } = "";

        [Description("Optional balancing account type for every line in the batch: G/L Account (default), Customer, Vendor, Bank Account, Fixed Asset, IC Partner, Employee.")]
        public string BalAccountType { get; set; } = "";

        [Description("Optional balancing account number applied to the whole batch.")]
        public string BalAccountNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Journal template the batch was created in.")]
        public string TemplateName { get; set; } = "";

        [Description("The batch name. Always report this to the user — it is how they find the batch in Business Central.")]
        public string BatchName { get; set; } = "";

        [Description("Batch description.")]
        public string Description { get; set; } = "";

        [Description("Batch-level balancing account type.")]
        public string BalAccountType { get; set; } = "";

        [Description("Batch-level balancing account number.")]
        public string BalAccountNo { get; set; } = "";

        [Description("Document number series on the batch, if any.")]
        public string NoSeries { get; set; } = "";

        [Description("Always false — the gateway never posts journals.")]
        public bool Posted { get; set; }

        [Description("Human-readable status note. Relay its meaning to the user.")]
        public string Note { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.BatchName))
            throw new ToolValidationException(
                "erp_bc.finance.create_journal_batch", "batchName is required.");

        BcCallerContext.Require(ctx, "erp_bc.finance.create_journal_batch");
        return await BcClient.ExecuteAsync<Result>(
            "CreateJournalBatch", args, "erp_bc.finance.create_journal_batch", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.add_journal_line
// ---------------------------------------------------------------------------

/// <summary>
/// Adds one unposted line to a general journal batch and returns the batch's
/// running debit/credit totals so the caller can see whether it balances.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.add_journal_line",
    Description = "Add one UNPOSTED line to a general journal batch. Amount is positive for a DEBIT and negative for a CREDIT; alternatively pass debitAmount or creditAmount and the sign is handled for you. Returns the line plus the batch's running totals (totalDebitLcy, totalCreditLcy, balanceLcy, balanced) — a batch can only be posted when it balances. Posting is done by a person in Business Central; the gateway cannot post.",
    Sensitivity = "write")]
public class AddJournalLine : ToolHandler<AddJournalLine.Args, AddJournalLine.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional general journal template name. Omit to use the first template of type General (must match the batch).")]
        public string TemplateName { get; set; } = "";

        [Description("Required. The journal batch to add the line to; create it first with create_journal_batch.")]
        public string BatchName { get; set; } = "";

        [Description("Optional posting date (yyyy-MM-dd). Defaults to the Business Central work date.")]
        public string PostingDate { get; set; } = "";

        [Description("Optional document number. Defaults to the document number already used in the batch, else the batch's number series.")]
        public string DocumentNo { get; set; } = "";

        [Description("Optional document type: Payment, Invoice, Credit Memo, Finance Charge Memo, Reminder, Refund. Omit for a plain entry.")]
        public string DocumentType { get; set; } = "";

        [Description("Account type: G/L Account (default), Customer, Vendor, Bank Account, Fixed Asset, IC Partner, Employee.")]
        public string AccountType { get; set; } = "";

        [Description("Required. Account number to post to, matching accountType (e.g. a G/L account number).")]
        public string AccountNo { get; set; } = "";

        [Description("Optional line description. Defaults to the account name.")]
        public string Description { get; set; } = "";

        [Description("Signed amount: POSITIVE = debit, NEGATIVE = credit. Use this or debitAmount/creditAmount, never both.")]
        public decimal? Amount { get; set; }

        [Description("Debit amount as a positive number (sign-safe alternative to amount).")]
        public decimal? DebitAmount { get; set; }

        [Description("Credit amount as a positive number (sign-safe alternative to amount).")]
        public decimal? CreditAmount { get; set; }

        [Description("Optional currency code. Empty means the company's local currency.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Optional balancing account type for this line: G/L Account (default), Customer, Vendor, Bank Account, Fixed Asset, IC Partner, Employee.")]
        public string BalAccountType { get; set; } = "";

        [Description("Optional balancing account number. A line with a balancing account balances itself.")]
        public string BalAccountNo { get; set; } = "";

        [Description("Optional Shortcut Dimension 1 code (usually department).")]
        public string Dimension1Code { get; set; } = "";

        [Description("Optional Shortcut Dimension 2 code.")]
        public string Dimension2Code { get; set; } = "";

        [Description("Optional external document number (e.g. the supplier's reference).")]
        public string ExternalDocumentNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Journal template of the batch.")]
        public string TemplateName { get; set; } = "";

        [Description("Batch the line was added to.")]
        public string BatchName { get; set; } = "";

        [Description("Line number created.")]
        public int LineNo { get; set; }

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Document type.")]
        public string DocumentType { get; set; } = "";

        [Description("Document number of the line.")]
        public string DocumentNo { get; set; } = "";

        [Description("Account type.")]
        public string AccountType { get; set; } = "";

        [Description("Account number posted to.")]
        public string AccountNo { get; set; } = "";

        [Description("Line description.")]
        public string LineDescription { get; set; } = "";

        [Description("Signed amount in the line currency: positive = debit, negative = credit.")]
        public decimal Amount { get; set; }

        [Description("Signed amount in the company's local currency.")]
        public decimal AmountLcy { get; set; }

        [Description("Currency code of the line; empty means local currency.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Balancing account type on the line.")]
        public string BalAccountType { get; set; } = "";

        [Description("Balancing account number on the line.")]
        public string BalAccountNo { get; set; } = "";

        [Description("Shortcut Dimension 1 code.")]
        public string Dimension1Code { get; set; } = "";

        [Description("Shortcut Dimension 2 code.")]
        public string Dimension2Code { get; set; } = "";

        [Description("External document number.")]
        public string ExternalDocumentNo { get; set; } = "";

        [Description("Total lines now in the batch.")]
        public int BatchLineCount { get; set; }

        [Description("Sum of all debit amounts in the batch (LCY).")]
        public decimal TotalDebitLcy { get; set; }

        [Description("Sum of all credit amounts in the batch (LCY).")]
        public decimal TotalCreditLcy { get; set; }

        [Description("Debits minus credits (LCY), excluding self-balancing lines. Must be 0 before the batch can be posted.")]
        public decimal BalanceLcy { get; set; }

        [Description("Lines that carry their own balancing account and therefore balance themselves.")]
        public int SelfBalancingLines { get; set; }

        [Description("True when the batch balances and is ready for a person to post.")]
        public bool Balanced { get; set; }

        [Description("Always false — the gateway never posts journals.")]
        public bool Posted { get; set; }

        [Description("Human-readable status note. Relay its meaning to the user.")]
        public string Note { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.BatchName))
            throw new ToolValidationException(
                "erp_bc.finance.add_journal_line", "batchName is required.");
        if (string.IsNullOrWhiteSpace(args.AccountNo))
            throw new ToolValidationException(
                "erp_bc.finance.add_journal_line", "accountNo is required.");

        var hasAmount = args.Amount is not null && args.Amount != 0;
        var hasDebit  = args.DebitAmount is not null && args.DebitAmount != 0;
        var hasCredit = args.CreditAmount is not null && args.CreditAmount != 0;

        if (!hasAmount && !hasDebit && !hasCredit)
            throw new ToolValidationException(
                "erp_bc.finance.add_journal_line",
                "A non-zero amount is required: pass amount (positive = debit, negative = credit), or debitAmount, or creditAmount.");
        if (hasDebit && hasCredit)
            throw new ToolValidationException(
                "erp_bc.finance.add_journal_line",
                "Pass either debitAmount or creditAmount on a line, not both.");
        if (hasAmount && (hasDebit || hasCredit))
            throw new ToolValidationException(
                "erp_bc.finance.add_journal_line",
                "Pass either amount or debitAmount/creditAmount, not both.");

        BcCallerContext.Require(ctx, "erp_bc.finance.add_journal_line");
        return await BcClient.ExecuteAsync<Result>(
            "AddJournalLine", args, "erp_bc.finance.add_journal_line", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.finance.get_journal_batch
// ---------------------------------------------------------------------------

/// <summary>
/// Reads back a staged journal batch with its lines and balance check.
/// </summary>
[Tool(
    Key         = "erp_bc.finance.get_journal_batch",
    Description = "Read back an unposted general journal batch: every staged line plus the batch totals (totalDebitLcy, totalCreditLcy, balanceLcy, balanced). Use this to review what was staged before telling the user the batch is ready to post.",
    Sensitivity = "read")]
public class GetJournalBatch : ToolHandler<GetJournalBatch.Args, GetJournalBatch.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Optional general journal template name. Omit to use the first template of type General.")]
        public string TemplateName { get; set; } = "";

        [Description("Required. The journal batch to read.")]
        public string BatchName { get; set; } = "";
    }

    public class Line
    {
        [Description("Line number.")]
        public int LineNo { get; set; }

        [Description("Posting date (yyyy-MM-dd).")]
        public string PostingDate { get; set; } = "";

        [Description("Document type.")]
        public string DocumentType { get; set; } = "";

        [Description("Document number.")]
        public string DocumentNo { get; set; } = "";

        [Description("Account type.")]
        public string AccountType { get; set; } = "";

        [Description("Account number.")]
        public string AccountNo { get; set; } = "";

        [Description("Line description.")]
        public string LineDescription { get; set; } = "";

        [Description("Signed amount: positive = debit, negative = credit.")]
        public decimal Amount { get; set; }

        [Description("Signed amount in the company's local currency.")]
        public decimal AmountLcy { get; set; }

        [Description("Currency code; empty means local currency.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Balancing account type.")]
        public string BalAccountType { get; set; } = "";

        [Description("Balancing account number.")]
        public string BalAccountNo { get; set; } = "";

        [Description("Shortcut Dimension 1 code.")]
        public string Dimension1Code { get; set; } = "";

        [Description("Shortcut Dimension 2 code.")]
        public string Dimension2Code { get; set; } = "";

        [Description("External document number.")]
        public string ExternalDocumentNo { get; set; } = "";
    }

    public class Result
    {
        [Description("Journal template of the batch.")]
        public string TemplateName { get; set; } = "";

        [Description("Batch name.")]
        public string BatchName { get; set; } = "";

        [Description("Batch description.")]
        public string BatchDescription { get; set; } = "";

        [Description("Batch-level balancing account type.")]
        public string BalAccountType { get; set; } = "";

        [Description("Batch-level balancing account number.")]
        public string BalAccountNo { get; set; } = "";

        [Description("Number of lines returned.")]
        public int LineCount { get; set; }

        [Description("The staged (unposted) journal lines.")]
        public List<Line> Lines { get; set; } = new();

        [Description("Total lines in the batch.")]
        public int BatchLineCount { get; set; }

        [Description("Sum of all debit amounts (LCY).")]
        public decimal TotalDebitLcy { get; set; }

        [Description("Sum of all credit amounts (LCY).")]
        public decimal TotalCreditLcy { get; set; }

        [Description("Debits minus credits (LCY), excluding self-balancing lines. Must be 0 before posting.")]
        public decimal BalanceLcy { get; set; }

        [Description("Lines that carry their own balancing account.")]
        public int SelfBalancingLines { get; set; }

        [Description("True when the batch balances and is ready for a person to post.")]
        public bool Balanced { get; set; }

        [Description("Always false — the gateway never posts journals.")]
        public bool Posted { get; set; }

        [Description("Human-readable status note. Relay its meaning to the user.")]
        public string Note { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.BatchName))
            throw new ToolValidationException(
                "erp_bc.finance.get_journal_batch", "batchName is required.");

        return await BcClient.ExecuteAsync<Result>(
            "GetJournalBatch", args, "erp_bc.finance.get_journal_batch", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.create_offer
// ---------------------------------------------------------------------------

/// <summary>
/// Creates an LS Central periodic discount (offer) header. Always created
/// disabled — <see cref="SetOfferStatus"/> is the deliberate go-live step.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.create_offer",
    Description = "Create an LS Central promotion (periodic discount offer) header. It is created DISABLED and discounts nothing until you add lines with add_offer_line and enable it with set_offer_status. offerNo is required — LS Central has no offer number series. startingDate/endingDate are stored in a validation period created for you. HEAD OFFICE ONLY: the offer reaches the tills when LS Central replication next runs.",
    Sensitivity = "write")]
public class CreateOffer : ToolHandler<CreateOffer.Args, CreateOffer.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Required. Offer number (max 20 characters), e.g. OFF-2026-07. LS Central has no number series for offers, so you must supply one.")]
        public string OfferNo { get; set; } = "";

        [Description("Required. Offer description shown in LS Central (max 30 characters).")]
        public string Description { get; set; } = "";

        [Description("Offer type: Disc. Offer (default), Multibuy, Mix&Match, Total Discount, Tender Type, Item Point, Line Discount.")]
        public string OfferType { get; set; } = "";

        [Description("How the discount is expressed: Discount % (default), Deal Price, Discount Amount, Least Expensive, Line spec.")]
        public string DiscountType { get; set; } = "";

        [Description("Discount percentage when discountType is Discount %, e.g. 15 for 15%.")]
        public decimal? DiscountPctValue { get; set; }

        [Description("Discount amount when discountType is Discount Amount.")]
        public decimal? DiscountAmountValue { get; set; }

        [Description("Deal price when discountType is Deal Price.")]
        public decimal? DealPriceValue { get; set; }

        [Description("Optional first day the offer is valid (yyyy-MM-dd). A validation period is created automatically to hold the dates.")]
        public string StartingDate { get; set; } = "";

        [Description("Optional last day the offer is valid (yyyy-MM-dd). Omit for open-ended.")]
        public string EndingDate { get; set; } = "";

        [Description("Optional existing validation period ID to reuse instead of creating one from the dates.")]
        public string ValidationPeriodId { get; set; } = "";

        [Description("Optional price group the offer applies to (limits it to certain stores).")]
        public string PriceGroup { get; set; } = "";

        [Description("Optional currency code.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Optional priority when several offers compete; higher wins.")]
        public int? Priority { get; set; }

        [Description("Optional number of least expensive items covered, for Least Expensive offers.")]
        public int? NoOfLeastExpensiveItems { get; set; }

        [Description("Optional loyalty scope type when limiting the offer to members: Scheme (default) or Club.")]
        public string MemberType { get; set; } = "";

        [Description("Optional member scheme or club code the offer is limited to.")]
        public string MemberValue { get; set; } = "";
    }

    public class Result
    {
        [Description("Offer number created. Always report this to the user.")]
        public string OfferNo { get; set; } = "";

        [Description("Offer description.")]
        public string Description { get; set; } = "";

        [Description("Offer type.")]
        public string OfferType { get; set; } = "";

        [Description("Status: Disabled or Enabled.")]
        public string Status { get; set; } = "";

        [Description("True only when the offer is Enabled. A newly created offer is always false.")]
        public bool Enabled { get; set; }

        [Description("How the discount is expressed.")]
        public string DiscountType { get; set; } = "";

        [Description("Discount percentage value.")]
        public decimal DiscountPctValue { get; set; }

        [Description("Discount amount value.")]
        public decimal DiscountAmountValue { get; set; }

        [Description("Deal price value.")]
        public decimal DealPriceValue { get; set; }

        [Description("Priority against competing offers.")]
        public int Priority { get; set; }

        [Description("Price group the offer applies to.")]
        public string PriceGroup { get; set; } = "";

        [Description("Currency code.")]
        public string CurrencyCode { get; set; } = "";

        [Description("Validation period ID holding the offer's dates.")]
        public string ValidationPeriodId { get; set; } = "";

        [Description("Validation period description.")]
        public string ValidationDescription { get; set; } = "";

        [Description("First day the offer is valid (yyyy-MM-dd); empty means open-ended.")]
        public string StartingDate { get; set; } = "";

        [Description("Last day the offer is valid (yyyy-MM-dd); empty means open-ended.")]
        public string EndingDate { get; set; } = "";

        [Description("Loyalty scope type.")]
        public string MemberType { get; set; } = "";

        [Description("Member scheme or club the offer is limited to.")]
        public string MemberValue { get; set; } = "";

        [Description("Number of lines on the offer. Zero means it discounts nothing yet.")]
        public int LineCount { get; set; }

        [Description("Status and replication note. Relay its meaning to the user — the offer is NOT live at the POS until replication runs.")]
        public string Note { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OfferNo))
            throw new ToolValidationException(
                "erp_bc.retail.create_offer", "offerNo is required.");
        if (string.IsNullOrWhiteSpace(args.Description))
            throw new ToolValidationException(
                "erp_bc.retail.create_offer", "description is required.");

        BcCallerContext.Require(ctx, "erp_bc.retail.create_offer");
        return await BcClient.ExecuteAsync<Result>(
            "CreateOffer", args, "erp_bc.retail.create_offer", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.add_offer_line
// ---------------------------------------------------------------------------

/// <summary>
/// Adds an item (or group) line to an LS Central offer.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.add_offer_line",
    Description = "Add a line to an LS Central offer — the item, product group, item category, or special group the discount applies to. lineType All covers every item and takes no 'no'. Create the offer first with create_offer; enable it afterwards with set_offer_status.",
    Sensitivity = "write")]
public class AddOfferLine : ToolHandler<AddOfferLine.Args, AddOfferLine.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Required. The offer number to add the line to.")]
        public string OfferNo { get; set; } = "";

        [Description("What the line targets: Item (default), Product Group, Item Category, Special Group, or All.")]
        public string LineType { get; set; } = "";

        [Description("The item number (or group/category code) the line applies to. Required unless lineType is All.")]
        public string No { get; set; } = "";

        [Description("Optional variant code to narrow the line to one variant.")]
        public string VariantCode { get; set; } = "";

        [Description("Optional unit of measure code.")]
        public string UnitOfMeasure { get; set; } = "";

        [Description("Optional line group (a single character) used by Mix&Match to group lines that trigger together.")]
        public string LineGroup { get; set; } = "";

        [Description("How this line discounts: Disc. % (default) or Deal Price.")]
        public string DiscType { get; set; } = "";

        [Description("The discount percentage or deal price for this line, matching discType.")]
        public decimal? DealPriceOrDiscPct { get; set; }

        [Description("Optional number of this item needed to trigger the offer (Multibuy / Mix&Match).")]
        public int? NoOfItemsNeeded { get; set; }
    }

    public class Result
    {
        [Description("Offer the line belongs to.")]
        public string OfferNo { get; set; } = "";

        [Description("Line number created.")]
        public int LineNo { get; set; }

        [Description("What the line targets.")]
        public string LineType { get; set; } = "";

        [Description("Item number or group code on the line.")]
        public string No { get; set; } = "";

        [Description("Variant code.")]
        public string VariantCode { get; set; } = "";

        [Description("Line description resolved from the item or group.")]
        public string LineDescription { get; set; } = "";

        [Description("Unit of measure.")]
        public string UnitOfMeasure { get; set; } = "";

        [Description("Line group for Mix&Match.")]
        public string LineGroup { get; set; } = "";

        [Description("How the line discounts.")]
        public string DiscType { get; set; } = "";

        [Description("Discount percentage or deal price on the line.")]
        public decimal DealPriceOrDiscPct { get; set; }

        [Description("Number of items needed to trigger.")]
        public int NoOfItemsNeeded { get; set; }

        [Description("Normal price including VAT.")]
        public decimal StandardPriceIncludingVat { get; set; }

        [Description("Discounted offer price including VAT.")]
        public decimal OfferPriceIncludingVat { get; set; }

        [Description("Status of the parent offer — still Disabled until you enable it.")]
        public string OfferStatus { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OfferNo))
            throw new ToolValidationException(
                "erp_bc.retail.add_offer_line", "offerNo is required.");

        BcCallerContext.Require(ctx, "erp_bc.retail.add_offer_line");
        return await BcClient.ExecuteAsync<Result>(
            "AddOfferLine", args, "erp_bc.retail.add_offer_line", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.set_offer_status
// ---------------------------------------------------------------------------

/// <summary>
/// Enables or disables an LS Central offer — the go-live switch. Returns the
/// same shape as <see cref="CreateOffer"/> so the caller sees the full state.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.set_offer_status",
    Description = "Enable or disable an LS Central offer. Enabling is the go-live step that makes the discount real, so only call it once the user has confirmed the offer's items and dates. An offer with no lines cannot be enabled. HEAD OFFICE ONLY: the change reaches the tills when LS Central replication next runs.",
    Sensitivity = "destructive")]
public class SetOfferStatus : ToolHandler<SetOfferStatus.Args, CreateOffer.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Required. The offer number to enable or disable.")]
        public string OfferNo { get; set; } = "";

        [Description("True to enable (go live), false to disable. Either this or status is required.")]
        public bool? Enabled { get; set; }

        [Description("Alternative to enabled: 'Enabled' or 'Disabled'.")]
        public string Status { get; set; } = "";
    }

    public override async Task<CreateOffer.Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.OfferNo))
            throw new ToolValidationException(
                "erp_bc.retail.set_offer_status", "offerNo is required.");
        if (args.Enabled is null && string.IsNullOrWhiteSpace(args.Status))
            throw new ToolValidationException(
                "erp_bc.retail.set_offer_status",
                "Pass enabled (true/false) or status ('Enabled'/'Disabled').");

        BcCallerContext.Require(ctx, "erp_bc.retail.set_offer_status");
        return await BcClient.ExecuteAsync<CreateOffer.Result>(
            "SetOfferStatus", args, "erp_bc.retail.set_offer_status", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.create_coupon
// ---------------------------------------------------------------------------

/// <summary>
/// Creates an LS Central coupon header. Always created disabled —
/// <see cref="SetCouponStatus"/> is the deliberate go-live step.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.create_coupon",
    Description = "Create an LS Central coupon (store, manufacturer, or return coupon). It is created DISABLED and does nothing until you add Use lines with add_coupon_line and enable it with set_coupon_status. code is required and is limited to 10 CHARACTERS — LS Central has no coupon number series. handling Discount reduces the price; Tender means the coupon is used like a payment. HEAD OFFICE ONLY: the coupon reaches the tills when LS Central replication next runs.",
    Sensitivity = "write")]
public class CreateCoupon : ToolHandler<CreateCoupon.Args, CreateCoupon.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Required. Coupon code, MAX 10 CHARACTERS, e.g. EID26. LS Central has no number series for coupons, so you must supply one.")]
        public string Code { get; set; } = "";

        [Description("Required. Coupon description (max 30 characters).")]
        public string Description { get; set; } = "";

        [Description("Optional second description line.")]
        public string Description2 { get; set; } = "";

        [Description("Coupon type: Store Coupon (default), Manufacturer Coupon, Return Coupon.")]
        public string CouponType { get; set; } = "";

        [Description("How the coupon is used at the POS: Discount (default, reduces the price) or Tender (used like a payment).")]
        public string Handling { get; set; } = "";

        [Description("How the value is expressed: Discount % (default) or Discount Amount.")]
        public string DiscountType { get; set; } = "";

        [Description("The coupon's value — a percentage when discountType is Discount %, otherwise an amount.")]
        public decimal? Value { get; set; }

        [Description("Optional first day the coupon is valid (yyyy-MM-dd). A validation period is created automatically to hold the dates.")]
        public string StartingDate { get; set; } = "";

        [Description("Optional last day the coupon is valid (yyyy-MM-dd). Omit for open-ended.")]
        public string EndingDate { get; set; } = "";

        [Description("Optional existing validation period ID to reuse instead of creating one from the dates.")]
        public string ValidationPeriodId { get; set; } = "";

        [Description("Optional coupon issuer code.")]
        public string CouponIssuer { get; set; } = "";

        [Description("Optional price group limiting the coupon to certain stores.")]
        public string PriceGroup { get; set; } = "";

        [Description("Optional number of qualifying items needed to trigger the coupon.")]
        public int? NoOfItemsToTrigger { get; set; }

        [Description("Optional number of items the coupon discount applies to.")]
        public int? ApplyToNoOfItems { get; set; }

        [Description("Optional maximum times one loyalty member may use the coupon.")]
        public int? MaxPerMember { get; set; }

        [Description("Optional minimum transaction amount before the coupon applies.")]
        public decimal? MinimumTransAmount { get; set; }

        [Description("Whether the coupon can be issued at the POS. Defaults to false.")]
        public bool? IssueAtPos { get; set; }

        [Description("Optional loyalty scheme code.")]
        public string LoyaltyScheme { get; set; } = "";

        [Description("Optional loyalty scope type when limiting the coupon to members: Scheme (default) or Club.")]
        public string MemberType { get; set; } = "";

        [Description("Optional member scheme or club code the coupon is limited to.")]
        public string MemberValue { get; set; } = "";
    }

    public class Result
    {
        [Description("Coupon code created. Always report this to the user.")]
        public string Code { get; set; } = "";

        [Description("Coupon description.")]
        public string Description { get; set; } = "";

        [Description("Second description line.")]
        public string Description2 { get; set; } = "";

        [Description("Status: Disabled or Enabled.")]
        public string Status { get; set; } = "";

        [Description("True only when the coupon is Enabled. A newly created coupon is always false.")]
        public bool Enabled { get; set; }

        [Description("Coupon type.")]
        public string CouponType { get; set; } = "";

        [Description("Handling: Discount or Tender.")]
        public string Handling { get; set; } = "";

        [Description("How the value is expressed.")]
        public string DiscountType { get; set; } = "";

        [Description("The coupon's value (percentage or amount, per discountType).")]
        public decimal Value { get; set; }

        [Description("Coupon issuer code.")]
        public string CouponIssuer { get; set; } = "";

        [Description("Price group.")]
        public string PriceGroup { get; set; } = "";

        [Description("Validation period ID holding the coupon's dates.")]
        public string ValidationPeriodId { get; set; } = "";

        [Description("Validation period description.")]
        public string ValidationDescription { get; set; } = "";

        [Description("First day the coupon is valid (yyyy-MM-dd); empty means open-ended.")]
        public string StartingDate { get; set; } = "";

        [Description("Last day the coupon is valid (yyyy-MM-dd); empty means open-ended.")]
        public string EndingDate { get; set; } = "";

        [Description("Qualifying items needed to trigger the coupon.")]
        public int NoOfItemsToTrigger { get; set; }

        [Description("Items the coupon discount applies to.")]
        public int ApplyToNoOfItems { get; set; }

        [Description("Maximum uses per loyalty member.")]
        public int MaxPerMember { get; set; }

        [Description("Minimum transaction amount before the coupon applies.")]
        public decimal MinimumTransAmount { get; set; }

        [Description("Whether the coupon can be issued at the POS.")]
        public bool IssueAtPos { get; set; }

        [Description("Loyalty scheme code.")]
        public string LoyaltyScheme { get; set; } = "";

        [Description("Loyalty scope type.")]
        public string MemberType { get; set; } = "";

        [Description("Member scheme or club the coupon is limited to.")]
        public string MemberValue { get; set; } = "";

        [Description("Number of lines on the coupon. Zero means it applies to nothing yet.")]
        public int LineCount { get; set; }

        [Description("Status and replication note. Relay its meaning to the user — the coupon is NOT live at the POS until replication runs.")]
        public string Note { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Code))
            throw new ToolValidationException(
                "erp_bc.retail.create_coupon", "code is required.");
        if (args.Code.Trim().Length > 10)
            throw new ToolValidationException(
                "erp_bc.retail.create_coupon",
                $"code must be at most 10 characters (LS Central coupon codes are Code[10]); '{args.Code.Trim()}' is {args.Code.Trim().Length}.");
        if (string.IsNullOrWhiteSpace(args.Description))
            throw new ToolValidationException(
                "erp_bc.retail.create_coupon", "description is required.");

        BcCallerContext.Require(ctx, "erp_bc.retail.create_coupon");
        return await BcClient.ExecuteAsync<Result>(
            "CreateCoupon", args, "erp_bc.retail.create_coupon", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.add_coupon_line
// ---------------------------------------------------------------------------

/// <summary>
/// Adds a Use or Issue line to an LS Central coupon.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.add_coupon_line",
    Description = "Add a line to an LS Central coupon. listType 'Use' (default) is what the coupon can be redeemed against — a coupon needs at least one Use line before it can be enabled. listType 'Issue' is what triggers issuing the coupon. lineType All covers every item and takes no 'no'.",
    Sensitivity = "write")]
public class AddCouponLine : ToolHandler<AddCouponLine.Args, AddCouponLine.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Required. The coupon code to add the line to.")]
        public string Code { get; set; } = "";

        [Description("Use (default) for what the coupon is redeemed against, or Issue for what triggers issuing it.")]
        public string ListType { get; set; } = "";

        [Description("What the line targets: Item (default), Product Group, Item Category, Special Group, or All.")]
        public string LineType { get; set; } = "";

        [Description("The item number (or group/category code) the line applies to. Required unless lineType is All.")]
        public string No { get; set; } = "";

        [Description("Optional variant or dimension 1 code.")]
        public string VariantCode { get; set; } = "";

        [Description("Optional unit of measure code.")]
        public string UnitOfMeasure { get; set; } = "";

        [Description("Set true to EXCLUDE this item/group from the coupon instead of including it.")]
        public bool? Exclude { get; set; }
    }

    public class Result
    {
        [Description("Coupon the line belongs to.")]
        public string Code { get; set; } = "";

        [Description("Line number created.")]
        public int LineNo { get; set; }

        [Description("Use or Issue.")]
        public string ListType { get; set; } = "";

        [Description("What the line targets.")]
        public string LineType { get; set; } = "";

        [Description("Item number or group code on the line.")]
        public string No { get; set; } = "";

        [Description("Variant or dimension 1 code.")]
        public string VariantCode { get; set; } = "";

        [Description("Line description resolved from the item or group.")]
        public string LineDescription { get; set; } = "";

        [Description("Unit of measure.")]
        public string UnitOfMeasure { get; set; } = "";

        [Description("True when the line excludes rather than includes.")]
        public bool Exclude { get; set; }

        [Description("Status of the parent coupon — still Disabled until you enable it.")]
        public string CouponStatus { get; set; } = "";
    }

    public override async Task<Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Code))
            throw new ToolValidationException(
                "erp_bc.retail.add_coupon_line", "code is required.");

        BcCallerContext.Require(ctx, "erp_bc.retail.add_coupon_line");
        return await BcClient.ExecuteAsync<Result>(
            "AddCouponLine", args, "erp_bc.retail.add_coupon_line", ctx);
    }
}

// ---------------------------------------------------------------------------
// bc.retail.set_coupon_status
// ---------------------------------------------------------------------------

/// <summary>
/// Enables or disables an LS Central coupon — the go-live switch. Returns the
/// same shape as <see cref="CreateCoupon"/> so the caller sees the full state.
/// </summary>
[Tool(
    Key         = "erp_bc.retail.set_coupon_status",
    Description = "Enable or disable an LS Central coupon. Enabling is the go-live step that makes the coupon redeemable, so only call it once the user has confirmed its items, value, and dates. A coupon with no Use lines cannot be enabled. HEAD OFFICE ONLY: the change reaches the tills when LS Central replication next runs.",
    Sensitivity = "destructive")]
public class SetCouponStatus : ToolHandler<SetCouponStatus.Args, CreateCoupon.Result>
{
    public class Args
    {
        [Description(BcToolDescriptions.Country)]
        public string Country { get; set; } = "";

        [Description("Required. The coupon code to enable or disable.")]
        public string Code { get; set; } = "";

        [Description("True to enable (go live), false to disable. Either this or status is required.")]
        public bool? Enabled { get; set; }

        [Description("Alternative to enabled: 'Enabled' or 'Disabled'.")]
        public string Status { get; set; } = "";
    }

    public override async Task<CreateCoupon.Result> HandleAsync(Args args, ToolContext ctx)
    {
        if (string.IsNullOrWhiteSpace(args.Code))
            throw new ToolValidationException(
                "erp_bc.retail.set_coupon_status", "code is required.");
        if (args.Enabled is null && string.IsNullOrWhiteSpace(args.Status))
            throw new ToolValidationException(
                "erp_bc.retail.set_coupon_status",
                "Pass enabled (true/false) or status ('Enabled'/'Disabled').");

        BcCallerContext.Require(ctx, "erp_bc.retail.set_coupon_status");
        return await BcClient.ExecuteAsync<CreateCoupon.Result>(
            "SetCouponStatus", args, "erp_bc.retail.set_coupon_status", ctx);
    }
}
