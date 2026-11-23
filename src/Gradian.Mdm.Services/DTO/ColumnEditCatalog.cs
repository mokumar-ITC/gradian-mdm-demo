namespace Gradian.Mdm.Services.DTO
{
    /// <summary>A column an administrator can lock or rename. <see cref="Key"/> is the C# property name.</summary>
    public sealed record ManagedColumn(string Key, string Label);

    /// <summary>A table whose columns appear under Administration &gt; Column Editing.</summary>
    /// <param name="Controller">The MVC controller whose Edit / BulkUpdate pages show this table's fields.</param>
    public sealed record ManagedTable(string Key, string Label, string Group, string Controller, IReadOnlyList<ManagedColumn> Columns);

    /// <summary>Current lock and display name of one column.</summary>
    /// <param name="DisplayName">The administrator's label; null means the built-in <see cref="ManagedColumn.Label"/>.</param>
    public sealed record ColumnEditStatus(ManagedColumn Column, bool IsEditable, string? DisplayName, string? UpdatedBy, DateTime? UpdatedDate)
    {
        /// <summary>The label to show on screen.</summary>
        public string Label => DisplayName ?? Column.Label;
    }

    public sealed record TableEditStatus(ManagedTable Table, IReadOnlyList<ColumnEditStatus> Columns);

    /// <summary>
    /// The columns an administrator can lock or rename. Each key is the property name used by the entities, DTOs and
    /// form fields. Record identifiers and read-only hierarchy columns are never editable, so they are not listed.
    /// </summary>
    public static class ColumnEditCatalog
    {
        public const string Product = "product";

        private const string ProductGroup = "Product Master (approval workflow)";

        public static readonly IReadOnlyList<ManagedTable> Tables = new List<ManagedTable>
        {
            new(Product, "Product", ProductGroup, "ProductMaster", new ManagedColumn[]
            {
                new("ProductName", "Product Name"),
                new("ProductCode", "Product Code"),
                new("Status", "Status"),
                new("CapacityGroup", "Capacity Group"),
                new("SiopModel", "SIOP Model"),
                new("SiopTier", "SIOP Tier"),
                new("DefaultSupplySource", "Default Supply Source"),
                new("SectionsPerUnit", "Sections per Unit"),
                new("StandardUnitMultiplier", "Standard Unit Multiplier"),
                new("SalePricePerUnit", "Sale Price per Unit"),
                new("SalePricePerHour", "Sale Price per Hour"),
                new("EstFabHours", "Est. Fabrication Hours"),
                new("EstAssemblyHours", "Est. Assembly Hours"),
                new("FabLbrHrsPct", "Fabrication Labor Hrs %"),
                new("AssmbLbrHrsPct", "Assembly Labor Hrs %"),
                new("QtyFans", "Qty Fans"),
                new("QtyCoils", "Qty Coils"),
                new("QtyPanels", "Qty Panels"),
            }),
        };

        /// <summary>Finds a table by key, ignoring case.</summary>
        public static ManagedTable? Find(string? tableKey) =>
            Tables.FirstOrDefault(t => string.Equals(t.Key, tableKey, StringComparison.OrdinalIgnoreCase));

        /// <summary>Finds the table edited by an MVC controller, ignoring case.</summary>
        public static ManagedTable? ForController(string? controller) =>
            Tables.FirstOrDefault(t => string.Equals(t.Controller, controller, StringComparison.OrdinalIgnoreCase));
    }
}
