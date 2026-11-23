using Gradian.Mdm.Services.DTO;

namespace Gradian.Mdm.Services.Interfaces
{
    /// <summary>Administrator column locks and display names (Administration &gt; Column Editing).</summary>
    public interface IColumnEditService
    {
        /// <summary>Every catalog table with the current lock and label of each column.</summary>
        Task<IReadOnlyList<TableEditStatus>> GetStatusAsync();

        /// <summary>Makes exactly <paramref name="editableColumns"/> editable and locks the rest. Returns how many columns changed.</summary>
        Task<int> SaveAsync(string tableKey, IEnumerable<string> editableColumns, string userName);

        /// <summary>
        /// Saves display names, keyed by column key; a blank name goes back to the built-in label.
        /// Returns how many names changed, or an error (and saves nothing) when a name is too long or used twice.
        /// </summary>
        Task<(int Changed, string? Error)> SaveDisplayNamesAsync(string tableKey, IDictionary<string, string?> displayNames, string userName);

        /// <summary>The label to show for every catalog column of the table: the admin's display name, else the built-in label.</summary>
        Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(string tableKey);

        /// <summary>Column keys locked on the table.</summary>
        Task<IReadOnlySet<string>> GetLockedColumnsAsync(string tableKey);

        /// <summary>
        /// Copies every locked column from the stored record onto the record about to be written, so a locked column
        /// keeps its stored value whatever the browser posted.
        /// </summary>
        Task KeepLockedValuesAsync(string tableKey, object recordToWrite, object storedRecord);

        /// <summary>Text for the checker when an approval could not change locked columns; empty when nothing is locked.</summary>
        Task<string> DescribeLockedForApprovalAsync(string tableKey);
    }
}
