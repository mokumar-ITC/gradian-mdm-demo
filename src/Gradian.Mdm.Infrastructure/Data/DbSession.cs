using System.Data;
using System.Data.Common;

namespace Gradian.Mdm.Infrastructure.Data
{
    /// <summary>A named SQL parameter. Create it with <see cref="DbSession.P"/>.</summary>
    public readonly record struct SqlParam(string Name, object? Value, DbType? Type = null);

    /// <summary>
    /// One database connection for the lifetime of a web request (registered as scoped).
    /// Opens the connection on first use, runs parameterised SQL only, and supports one ambient transaction
    /// so a repository method can change several tables atomically.
    /// </summary>
    public sealed class DbSession : IDisposable, IAsyncDisposable
    {
        private readonly DatabaseOptions _options;
        private DbConnection? _connection;
        private DbTransaction? _transaction;

        public DbSession(DatabaseOptions options, ISqlDialect dialect)
        {
            _options = options;
            Dialect = dialect;
        }

        public ISqlDialect Dialect { get; }

        /// <summary>Shorthand for a parameter: <c>P("id", 42)</c>. Pass <paramref name="type"/> when the value may be null.</summary>
        public static SqlParam P(string name, object? value, DbType? type = null) => new(name, value, type);

        /// <summary>Runs a query and maps every row.</summary>
        public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Func<DbDataReader, T> map, params SqlParam[] args)
        {
            await using var cmd = await CreateCommandAsync(sql, args);
            await using var reader = await cmd.ExecuteReaderAsync();
            var rows = new List<T>();
            while (await reader.ReadAsync()) rows.Add(map(reader));
            return rows;
        }

        /// <summary>Runs an INSERT / UPDATE / DELETE and returns the number of rows affected.</summary>
        public async Task<int> ExecuteAsync(string sql, params SqlParam[] args)
        {
            await using var cmd = await CreateCommandAsync(sql, args);
            return await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Runs a query and returns the first column of the first row, or null.</summary>
        public async Task<object?> ScalarAsync(string sql, params SqlParam[] args)
        {
            await using var cmd = await CreateCommandAsync(sql, args);
            var value = await cmd.ExecuteScalarAsync();
            return value is DBNull ? null : value;
        }

        /// <summary>How many times a transaction is run when the engine keeps aborting it because of concurrent writers.</summary>
        public const int MaxAttempts = 3;

        /// <summary>
        /// Runs <paramref name="work"/> in a transaction: commits when it returns, rolls back when it throws.
        /// A call made while a transaction is already open joins that transaction.
        /// When the engine aborts the transaction because of a concurrent change (common under Fabric's snapshot
        /// isolation), the whole transaction is retried, up to <see cref="MaxAttempts"/> times; after that an
        /// <see cref="InvalidOperationException"/> with a message the user can act on is thrown.
        /// <paramref name="work"/> must therefore be safe to run again from the start.
        /// </summary>
        public async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
        {
            if (_transaction != null) return await work();

            for (var attempt = 1; ; attempt++)
            {
                var connection = await OpenAsync();
                _transaction = await connection.BeginTransactionAsync();
                try
                {
                    var result = await work();
                    await _transaction.CommitAsync();
                    return result;
                }
                catch (DbException ex) when (Dialect.IsWriteConflict(ex))
                {
                    await TryRollbackAsync();
                    if (attempt >= MaxAttempts)
                        throw new InvalidOperationException("Another user changed the same data at the same time. Please try again.", ex);
                    await Task.Delay(Random.Shared.Next(50, 250) * attempt);   // back off a little before retrying
                }
                catch
                {
                    await TryRollbackAsync();
                    throw;
                }
                finally
                {
                    await _transaction.DisposeAsync();
                    _transaction = null;
                }
            }
        }

        /// <summary>Opens (once) and returns the connection.</summary>
        public async Task<DbConnection> OpenAsync()
        {
            if (_connection == null)
            {
                var connection = Dialect.Factory.CreateConnection()
                                 ?? throw new InvalidOperationException($"{Dialect.Provider} cannot create connections.");
                connection.ConnectionString = _options.ConnectionString;
                _connection = connection;
            }
            if (_connection.State != ConnectionState.Open) await _connection.OpenAsync();
            return _connection;
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            _connection?.Dispose();
            _transaction = null;
            _connection = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction != null) await _transaction.DisposeAsync();
            if (_connection != null) await _connection.DisposeAsync();
            _transaction = null;
            _connection = null;
        }

        private async Task<DbCommand> CreateCommandAsync(string sql, SqlParam[] args)
        {
            var connection = await OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = _options.CommandTimeoutSeconds;
            cmd.Transaction = _transaction;
            foreach (var arg in args)
            {
                var parameter = cmd.CreateParameter();
                parameter.ParameterName = "@" + arg.Name;
                Dialect.SetValue(parameter, arg.Value, arg.Type);
                cmd.Parameters.Add(parameter);
            }
            return cmd;
        }

        private async Task TryRollbackAsync()
        {
            try
            {
                await _transaction!.RollbackAsync();
            }
            catch (DbException)
            {
                // The engine already rolled back (or the connection dropped); the original error is what matters.
            }
            catch (InvalidOperationException)
            {
                // Same as above, reported by some providers as an invalid state.
            }
        }
    }
}
