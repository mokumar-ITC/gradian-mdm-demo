using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Gradian.Mdm.Infrastructure.Data
{
    /// <summary>
    /// The few places where PostgreSQL and Fabric Warehouse SQL differ. Repositories write plain SQL that runs on
    /// both engines (named <c>@parameters</c>, <c>lower()</c>, <c>COALESCE</c>, explicit transactions) and ask the
    /// dialect only for what can't be shared.
    /// </summary>
    public interface ISqlDialect
    {
        DatabaseProvider Provider { get; }
        DbProviderFactory Factory { get; }

        /// <summary>Boolean literal for <c>WHERE flag = {True}</c>: <c>TRUE</c> in PostgreSQL, <c>1</c> for a T-SQL bit column.</summary>
        string True { get; }

        /// <summary>
        /// True when the engine generates key values (PostgreSQL identity columns with <c>INSERT ... RETURNING</c>).
        /// When false (Fabric Warehouse), the repository takes the next key from <c>mdm.key_counter</c> inside the write transaction.
        /// </summary>
        bool GeneratesKeys { get; }

        /// <summary>Sets a parameter's value and, when the engine needs it, its type.</summary>
        void SetValue(DbParameter parameter, object? value, DbType? type);

        /// <summary>True for a unique-constraint violation. Fabric does not enforce constraints, so this never fires there.</summary>
        bool IsUniqueViolation(DbException ex);

        /// <summary>True when two transactions changed the same data at once and this one was rolled back.</summary>
        bool IsWriteConflict(DbException ex);

        /// <summary>Creates the dialect for a provider.</summary>
        static ISqlDialect For(DatabaseProvider provider) => provider switch
        {
            DatabaseProvider.FabricWarehouse => new FabricWarehouseDialect(),
            _ => new PostgreSqlDialect(),
        };
    }

    /// <summary>PostgreSQL through Npgsql. Timestamps are <c>timestamptz</c> and written in UTC.</summary>
    public sealed class PostgreSqlDialect : ISqlDialect
    {
        public DatabaseProvider Provider => DatabaseProvider.PostgreSql;
        public DbProviderFactory Factory => NpgsqlFactory.Instance;
        public string True => "TRUE";
        public bool GeneratesKeys => true;

        public void SetValue(DbParameter parameter, object? value, DbType? type)
        {
            // All timestamp columns are timestamptz and the app writes UTC values. Npgsql maps DbType.DateTime2 to
            // "timestamp without time zone", so date parameters are typed explicitly instead.
            if (value is DateTime || type is DbType.DateTime or DbType.DateTime2 or DbType.DateTimeOffset)
                ((NpgsqlParameter)parameter).NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz;
            else if (type != null)
                parameter.DbType = type.Value;
            parameter.Value = value ?? DBNull.Value;
        }

        public bool IsUniqueViolation(DbException ex) => ex is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        public bool IsWriteConflict(DbException ex) =>
            ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected };
    }

    /// <summary>
    /// Microsoft Fabric Warehouse through Microsoft.Data.SqlClient (TDS). Fabric has no identity columns, no enforced
    /// unique constraints and runs every transaction under snapshot isolation; the repositories account for that.
    /// </summary>
    public sealed class FabricWarehouseDialect : ISqlDialect
    {
        // SQL Server / Fabric error numbers for "snapshot isolation transaction aborted due to update conflict"
        private static readonly HashSet<int> ConflictErrors = new() { 3960, 24556, 24706, 1205 };

        public DatabaseProvider Provider => DatabaseProvider.FabricWarehouse;
        public DbProviderFactory Factory => SqlClientFactory.Instance;
        public string True => "1";
        public bool GeneratesKeys => false;

        public void SetValue(DbParameter parameter, object? value, DbType? type)
        {
            // Fabric tables use datetime2; SqlClient would otherwise send DateTime values as the legacy datetime type
            if (value is DateTime) parameter.DbType = DbType.DateTime2;
            else if (type != null) parameter.DbType = type.Value;
            parameter.Value = value ?? DBNull.Value;
        }

        // Only reachable when the scripts run against SQL Server (for local testing), which does enforce constraints
        public bool IsUniqueViolation(DbException ex) => ex is SqlException { Number: 2627 or 2601 };

        public bool IsWriteConflict(DbException ex) =>
            ex is SqlException sql && (ConflictErrors.Contains(sql.Number)
                                       || sql.Message.Contains("update conflict", StringComparison.OrdinalIgnoreCase));
    }
}
