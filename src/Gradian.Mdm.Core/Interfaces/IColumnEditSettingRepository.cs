using Gradian.Mdm.Core.Entities;

namespace Gradian.Mdm.Core.Interfaces
{
    /// <summary>Reads and writes administrator column settings (locks and display names).</summary>
    public interface IColumnEditSettingRepository
    {
        Task<IReadOnlyList<ColumnEditSetting>> GetAllAsync();
        Task<IReadOnlyList<ColumnEditSetting>> GetByTableAsync(string tableKey);

        /// <summary>Locks or unlocks a column. Leaves its display name unchanged.</summary>
        Task SaveEditableAsync(string tableKey, string columnKey, bool isEditable, string userName);

        /// <summary>Sets a column's display name; <c>null</c> goes back to the built-in label. Leaves the lock unchanged.</summary>
        Task SaveDisplayNameAsync(string tableKey, string columnKey, string? displayName, string userName);
    }
}
