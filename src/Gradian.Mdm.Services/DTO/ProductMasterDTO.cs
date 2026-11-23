using Gradian.Mdm.Core.Entities;

namespace Gradian.Mdm.Services.DTO
{
    // One node of the product hierarchy tree, with product counts rolled up from everything below it
    public class ProductTreeNode
    {
        public ProductHierarchyNode Node { get; init; } = new();
        public List<ProductTreeNode> Children { get; } = new();
        public int TotalProducts { get; set; }
        public int ActiveProducts { get; set; }
    }

    public class ProductMasterBrowse
    {
        public IReadOnlyList<ProductTreeNode> Roots { get; init; } = Array.Empty<ProductTreeNode>();
        public ProductTreeNode? Selected { get; init; }
        public IReadOnlyList<ProductHierarchyNode> Breadcrumb { get; init; } = Array.Empty<ProductHierarchyNode>();
        public IReadOnlyList<ProductMasterRecord> Products { get; init; } = Array.Empty<ProductMasterRecord>();
        public IReadOnlyDictionary<string, int> StatusCounts { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> ApprovalCounts { get; init; } = new Dictionary<string, int>();
        public IReadOnlyDictionary<int, int> NodesPerLevel { get; init; } = new Dictionary<int, int>();
        public IReadOnlyDictionary<string, string?> FieldDescriptions { get; init; } = new Dictionary<string, string?>();
        public int ProductsInScope { get; init; }
        public int PendingTotal { get; init; }
        // Open change request of each product waiting for approval (product id -> request), for bulk approve on the list
        public IReadOnlyDictionary<long, ProductMasterAudit> PendingRequests { get; init; } = new Dictionary<long, ProductMasterAudit>();
        public string? Search { get; init; }
        public string? Status { get; init; }
        public string? Approval { get; init; }
        public long? NodeId { get; init; }
    }

    public class ProductMasterDetail
    {
        public ProductMasterRecord Product { get; init; } = new();
        public IReadOnlyList<ProductHierarchyNode> Breadcrumb { get; init; } = Array.Empty<ProductHierarchyNode>();
        public IReadOnlyDictionary<string, string?> FieldDescriptions { get; init; } = new Dictionary<string, string?>();
        public IReadOnlyList<ProductMasterRecord> Siblings { get; init; } = Array.Empty<ProductMasterRecord>();
        public IReadOnlyList<ProductMasterAudit> History { get; init; } = Array.Empty<ProductMasterAudit>();
        public ProductMasterAudit? PendingRequest => History.FirstOrDefault(h => h.ApprovalStatus == ProductApproval.Pending);
    }

    // Posted by the Edit form
    public class ProductEditForm : ProductMasterValues
    {
        public long ProductId { get; set; }
        public string? ChangeRemarks { get; set; }
    }

    public class ProductEditPage
    {
        public ProductMasterRecord Product { get; init; } = new();
        public ProductEditForm Form { get; init; } = new();
        public IReadOnlyList<ProductHierarchyNode> Breadcrumb { get; init; } = Array.Empty<ProductHierarchyNode>();
        public IReadOnlyDictionary<string, string?> FieldDescriptions { get; init; } = new Dictionary<string, string?>();
        public IReadOnlyList<string> CapacityGroups { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> SupplySources { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> SiopModels { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> SiopTiers { get; init; } = Array.Empty<string>();
        public bool GoesToApproval { get; init; }   // true for Makers
    }

    // Checker's side-by-side view of one request
    public class ProductReview
    {
        public ProductMasterRecord Current { get; init; } = new();
        public ProductMasterAudit Request { get; init; } = new();
        public IReadOnlyCollection<string> Changed { get; init; } = Array.Empty<string>();
        public IReadOnlyCollection<string> Locked { get; init; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
        public bool IsOwnRequest { get; init; }
    }

    public record ProductWorkflowResult(bool Success, string Message, long? ProductId = null);

    // Posted by the Bulk Update grid: one row per selected product, one reason for the whole batch
    public class ProductBulkEditForm
    {
        public List<ProductEditForm> Rows { get; set; } = new();
        public string? ChangeRemarks { get; set; }
    }

    public class ProductBulkEditPage
    {
        public ProductBulkEditForm Form { get; init; } = new();
        public IReadOnlyDictionary<long, ProductMasterRecord> Products { get; init; } = new Dictionary<long, ProductMasterRecord>();
        public IReadOnlyList<string> Skipped { get; init; } = Array.Empty<string>();          // selected but can't be edited now
        public IReadOnlyDictionary<long, string> RowErrors { get; init; } = new Dictionary<long, string>();
        public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
        public IReadOnlyCollection<string> Locked { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> CapacityGroups { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> SupplySources { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> SiopModels { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> SiopTiers { get; init; } = Array.Empty<string>();
        public bool GoesToApproval { get; init; }   // true for Makers
    }

    // Outcome of a bulk update, approve or reject. RowErrors (product id -> reason) is set when the
    // bulk update was refused because of invalid rows; nothing was saved in that case.
    public record ProductBulkResult(bool Success, string Message, IReadOnlyDictionary<long, string>? RowErrors = null);

    public static class ProductBulk
    {
        public const int MaxRows = 200;
    }

    public static class ProductApproval
    {
        public const string Pending = "PendingApproval";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Direct = "UpdateDirectly";
        public const string Unchanged = "Unchanged";   // filter value for products never edited in the app

        public static readonly IReadOnlyList<(string Key, string Label)> Filters = new[]
        {
            (Pending, "Pending Approval"), (Approved, "Approved"), (Rejected, "Rejected"), (Direct, "Updated Directly"), (Unchanged, "Unchanged"),
        };

        public static string Label(string? status) => status switch
        {
            Pending => "Pending Approval",
            Approved => "Approved",
            Rejected => "Rejected",
            Direct => "Updated Directly",
            _ => "",
        };

        public static string BadgeClass(string? status) => status switch
        {
            Pending => "bg-warning text-dark",
            Approved => "bg-success",
            Rejected => "bg-danger",
            Direct => "bg-info text-dark",
            _ => "bg-light text-muted",
        };
    }

    // Built-in display labels for the editable product columns, in form order. An admin can rename them
    // under Administration > Column Editing; screens show the renamed label through Label(labels, property).
    public static class ProductFieldLabels
    {
        public static readonly IReadOnlyList<(string Property, string Field, string Label)> All = new[]
        {
            ("ProductName", "Product_Name", "Product Name"),
            ("ProductCode", "Product_Code", "Product Code"),
            ("Status", "Status", "Status"),
            ("CapacityGroup", "Capacity Group", "Capacity Group"),
            ("SiopModel", "SIOP_Model", "SIOP Model"),
            ("SiopTier", "SIOP_Tier", "SIOP Tier"),
            ("DefaultSupplySource", "Default_Supply_Source", "Default Supply Source"),
            ("SectionsPerUnit", "Sections_Per_Unit", "Sections per Unit"),
            ("StandardUnitMultiplier", "Standard_Unit_Multiplier", "Standard Unit Multiplier"),
            ("SalePricePerUnit", "Sale_Price_Per_Unit", "Sale Price per Unit"),
            ("SalePricePerHour", "Sale_Price_Per_Hour", "Sale Price per Hour"),
            ("EstFabHours", "Est_Fab_Hours", "Est. Fabrication Hours"),
            ("EstAssemblyHours", "Est_Assembly_Hours", "Est. Assembly Hours"),
            ("FabLbrHrsPct", "Fab_Lbr_Hrs_%", "Fabrication Labor Hrs %"),
            ("AssmbLbrHrsPct", "Assmb_Lbr_Hrs_%", "Assembly Labor Hrs %"),
            ("QtyFans", "Qty_Fans", "Qty Fans"),
            ("QtyCoils", "Qty_Coils", "Qty Coils"),
            ("QtyPanels", "Qty_Panels", "Qty Panels"),
        };

        public static string Label(string property) => All.FirstOrDefault(f => f.Property == property).Label ?? property;

        // The admin's display name when one is set (see ColumnEditService.GetLabelsAsync), else the built-in label
        public static string Label(IReadOnlyDictionary<string, string>? labels, string property) =>
            labels != null && labels.TryGetValue(property, out var l) ? l : Label(property);

        public static string Format(ProductMasterValues v, string property)
        {
            var value = typeof(ProductMasterValues).GetProperty(property)?.GetValue(v);
            return value switch
            {
                null => "",
                decimal d when property.StartsWith("SalePrice") => d.ToString("N0"),
                decimal d => d.ToString("0.##"),
                _ => value.ToString() ?? "",
            };
        }
    }
}
