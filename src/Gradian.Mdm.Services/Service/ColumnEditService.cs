using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Core.Interfaces;
using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Gradian.Mdm.Services.Service
{
    /// <summary>
    /// Column locks and display names. Registered per request, so a request sees one consistent set of locks and
    /// labels and reads each table's settings from the database only once.
    /// </summary>
    public class ColumnEditService : IColumnEditService
    {
        /// <summary>Longest display name an administrator can enter (matches the database column).</summary>
        public const int MaxDisplayNameLength = 100;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<ColumnEditService> _logger;
        private readonly Dictionary<string, IReadOnlyList<ColumnEditSetting>> _settingsByTable = new(StringComparer.OrdinalIgnoreCase);

        public ColumnEditService(IUnitOfWork unitOfWork, ILogger<ColumnEditService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<IReadOnlyList<TableEditStatus>> GetStatusAsync()
        {
            var settings = (await _unitOfWork.ColumnEditSettingRepository.GetAllAsync())
                .ToDictionary(s => (s.TableKey, s.ColumnKey));

            return ColumnEditCatalog.Tables.Select(table => new TableEditStatus(table, table.Columns.Select(column =>
            {
                settings.TryGetValue((table.Key, column.Key), out var setting);
                return new ColumnEditStatus(column, setting?.IsEditable ?? true, setting?.DisplayName, setting?.UpdatedBy, setting?.UpdatedDate);
            }).ToList())).ToList();
        }

        public async Task<int> SaveAsync(string tableKey, IEnumerable<string> editableColumns, string userName)
        {
            var table = ColumnEditCatalog.Find(tableKey) ?? throw new InvalidOperationException($"Unknown table \"{tableKey}\".");
            var editable = new HashSet<string>(editableColumns ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var locked = await GetLockedColumnsAsync(table.Key);

            var changed = 0;
            foreach (var column in table.Columns)
            {
                var shouldBeEditable = editable.Contains(column.Key);
                if (shouldBeEditable == !locked.Contains(column.Key)) continue;

                await _unitOfWork.ColumnEditSettingRepository.SaveEditableAsync(table.Key, column.Key, shouldBeEditable, userName);
                _logger.LogInformation("{User} set {Table}.{Column} editable = {Editable}", userName, table.Key, column.Key, shouldBeEditable);
                changed++;
            }

            _settingsByTable.Remove(table.Key);
            return changed;
        }

        public async Task<(int Changed, string? Error)> SaveDisplayNamesAsync(string tableKey, IDictionary<string, string?> displayNames, string userName)
        {
            var table = ColumnEditCatalog.Find(tableKey) ?? throw new InvalidOperationException($"Unknown table \"{tableKey}\".");
            var stored = (await SettingsAsync(table.Key)).ToDictionary(s => s.ColumnKey, s => s.DisplayName, StringComparer.OrdinalIgnoreCase);
            var posted = new Dictionary<string, string?>(displayNames ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase);

            // The name each column will have after the save; a column not posted keeps its stored name
            var wanted = table.Columns.ToDictionary(c => c.Key, c =>
            {
                var name = posted.TryGetValue(c.Key, out var p) ? Clean(p) : stored.GetValueOrDefault(c.Key);
                return string.Equals(name, c.Label, StringComparison.Ordinal) ? null : name;   // same as built-in = no override
            });

            var tooLong = table.Columns.FirstOrDefault(c => wanted[c.Key]?.Length > MaxDisplayNameLength);
            if (tooLong != null) return (0, $"The name for \"{tooLong.Label}\" can be at most {MaxDisplayNameLength} characters.");

            var duplicate = table.Columns.Select(c => wanted[c.Key] ?? c.Label)
                .GroupBy(l => l, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) return (0, $"Two columns would both be named \"{duplicate.Key}\". Give each column a different name.");

            var changed = 0;
            foreach (var column in table.Columns)
            {
                var name = wanted[column.Key];
                if (string.Equals(name, stored.GetValueOrDefault(column.Key), StringComparison.Ordinal)) continue;

                await _unitOfWork.ColumnEditSettingRepository.SaveDisplayNameAsync(table.Key, column.Key, name, userName);
                _logger.LogInformation("{User} renamed {Table}.{Column} to {Name}", userName, table.Key, column.Key, name ?? "(built-in label)");
                changed++;
            }

            _settingsByTable.Remove(table.Key);
            return (changed, null);
        }

        public async Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(string tableKey)
        {
            var table = ColumnEditCatalog.Find(tableKey);
            if (table == null) return new Dictionary<string, string>();

            var names = (await SettingsAsync(table.Key)).Where(s => s.DisplayName != null)
                .ToDictionary(s => s.ColumnKey, s => s.DisplayName!, StringComparer.OrdinalIgnoreCase);
            return table.Columns.ToDictionary(c => c.Key, c => names.GetValueOrDefault(c.Key) ?? c.Label, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<IReadOnlySet<string>> GetLockedColumnsAsync(string tableKey) =>
            new HashSet<string>((await SettingsAsync(tableKey)).Where(s => !s.IsEditable).Select(s => s.ColumnKey), StringComparer.OrdinalIgnoreCase);

        public async Task KeepLockedValuesAsync(string tableKey, object recordToWrite, object storedRecord)
        {
            foreach (var column in await GetLockedColumnsAsync(tableKey))
            {
                var target = recordToWrite.GetType().GetProperty(column);
                var source = storedRecord.GetType().GetProperty(column);

                // Refuse the write rather than silently let a locked column through.
                if (target == null || source == null)
                    throw new InvalidOperationException($"Locked column {tableKey}.{column} is not on {recordToWrite.GetType().Name} / {storedRecord.GetType().Name}.");

                target.SetValue(recordToWrite, source.GetValue(storedRecord));
            }
        }

        public async Task<string> DescribeLockedForApprovalAsync(string tableKey)
        {
            var table = ColumnEditCatalog.Find(tableKey);
            var locked = await GetLockedColumnsAsync(tableKey);
            if (table == null || locked.Count == 0) return "";

            var labels = await GetLabelsAsync(tableKey);
            return $" Locked columns were not changed: {string.Join(", ", table.Columns.Where(c => locked.Contains(c.Key)).Select(c => labels[c.Key]))}.";
        }

        /// <summary>The table's stored settings, read once per request.</summary>
        private async Task<IReadOnlyList<ColumnEditSetting>> SettingsAsync(string tableKey)
        {
            if (_settingsByTable.TryGetValue(tableKey, out var cached)) return cached;
            var settings = await _unitOfWork.ColumnEditSettingRepository.GetByTableAsync(tableKey.ToLowerInvariant());
            _settingsByTable[tableKey] = settings;
            return settings;
        }

        /// <summary>Trims a name and collapses inner whitespace; blank becomes null.</summary>
        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
