namespace Gradian.Mdm.Core.Entities
{
    /// <summary>
    /// One row of <c>mdm.column_edit_setting</c>: an administrator's lock and/or display name for a column.
    /// A column without a row is editable and uses its built-in label.
    /// </summary>
    public class ColumnEditSetting
    {
        public string TableKey { get; set; } = "";
        public string ColumnKey { get; set; } = "";
        public bool IsEditable { get; set; } = true;

        /// <summary>Label shown on screens; <c>null</c> means the built-in label.</summary>
        public string? DisplayName { get; set; }

        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}
