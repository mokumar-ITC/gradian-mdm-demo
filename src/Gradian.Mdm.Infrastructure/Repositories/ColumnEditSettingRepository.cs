using System.Data;
using System.Data.Common;
using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Core.Interfaces;
using Gradian.Mdm.Infrastructure.Data;
using static Gradian.Mdm.Infrastructure.Data.DbSession;

namespace Gradian.Mdm.Infrastructure.Repositories
{
    /// <summary>
    /// <c>mdm.column_edit_setting</c>: one row per (table, column) an administrator has locked or renamed.
    /// Saves are "update, else insert" in a transaction, which works on both PostgreSQL and Fabric Warehouse.
    /// </summary>
    public sealed class ColumnEditSettingRepository : IColumnEditSettingRepository
    {
        private const string Table = "mdm.column_edit_setting";
        private const string Columns = "table_key, column_key, is_editable, updated_by, updated_date, display_name";

        private readonly DbSession _db;

        public ColumnEditSettingRepository(DbSession db) => _db = db;

        public Task<IReadOnlyList<ColumnEditSetting>> GetAllAsync() =>
            _db.QueryAsync($"SELECT {Columns} FROM {Table}", Map);

        public Task<IReadOnlyList<ColumnEditSetting>> GetByTableAsync(string tableKey) =>
            _db.QueryAsync($"SELECT {Columns} FROM {Table} WHERE table_key = @table", Map, P("table", tableKey));

        public Task SaveEditableAsync(string tableKey, string columnKey, bool isEditable, string userName) =>
            UpsertAsync(tableKey, columnKey, userName, "is_editable", P("value", isEditable, DbType.Boolean), isEditable, null);

        public Task SaveDisplayNameAsync(string tableKey, string columnKey, string? displayName, string userName) =>
            UpsertAsync(tableKey, columnKey, userName, "display_name", P("value", displayName, DbType.String), true, displayName);

        /// <summary>Sets one column of the setting row, creating the row (editable, built-in label) when it doesn't exist yet.</summary>
        private async Task UpsertAsync(string tableKey, string columnKey, string userName, string column, SqlParam value,
            bool editableForNewRow, string? displayNameForNewRow)
        {
            var key = new[] { P("table", tableKey), P("column", columnKey), P("user", userName, DbType.String), P("now", DateTime.UtcNow) };
            try
            {
                await _db.InTransactionAsync(async () =>
                {
                    var updated = await _db.ExecuteAsync(
                        $"UPDATE {Table} SET {column} = @value, updated_by = @user, updated_date = @now " +
                        " WHERE table_key = @table AND column_key = @column", key.Append(value).ToArray());
                    if (updated == 0)
                    {
                        await _db.ExecuteAsync(
                            $"INSERT INTO {Table} (table_key, column_key, is_editable, display_name, updated_by, updated_date) " +
                            "VALUES (@table, @column, @editable, @name, @user, @now)",
                            key.Append(P("editable", editableForNewRow, DbType.Boolean)).Append(P("name", displayNameForNewRow, DbType.String)).ToArray());
                    }
                    return updated;
                });
            }
            catch (DbException ex) when (_db.Dialect.IsUniqueViolation(ex))
            {
                // Another admin inserted the row at the same moment; the row exists now, so the update succeeds
                await _db.ExecuteAsync(
                    $"UPDATE {Table} SET {column} = @value, updated_by = @user, updated_date = @now " +
                    " WHERE table_key = @table AND column_key = @column", key.Append(value).ToArray());
            }
        }

        private static ColumnEditSetting Map(DbDataReader r) => new()
        {
            TableKey = r.GetString(0),
            ColumnKey = r.GetString(1),
            IsEditable = Convert.ToBoolean(r.GetValue(2)),
            UpdatedBy = r.IsDBNull(3) ? null : r.GetString(3),
            UpdatedDate = r.IsDBNull(4) ? null : r.GetDateTime(4),
            DisplayName = r.IsDBNull(5) ? null : r.GetString(5),
        };
    }
}
