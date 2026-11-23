using Microsoft.Extensions.Configuration;

namespace Gradian.Mdm.Infrastructure.Data
{
    /// <summary>The database engines the app can run on.</summary>
    public enum DatabaseProvider
    {
        /// <summary>PostgreSQL 13+ (local, Azure Database for PostgreSQL, etc.) through Npgsql.</summary>
        PostgreSql,

        /// <summary>Microsoft Fabric Warehouse (SQL analytics endpoint, T-SQL) through Microsoft.Data.SqlClient.</summary>
        FabricWarehouse,
    }

    /// <summary>
    /// Database settings, read from configuration:
    /// <code>
    /// "ConnectionStrings": { "Mdm": "..." },
    /// "Database": { "Provider": "PostgreSql" | "FabricWarehouse", "CommandTimeoutSeconds": 30 }
    /// </code>
    /// Keep the connection string out of source control: use user-secrets locally and
    /// App Service settings / Key Vault references in Azure.
    /// </summary>
    public sealed class DatabaseOptions
    {
        public const string SectionName = "Database";
        public const string ConnectionStringName = "Mdm";

        public DatabaseProvider Provider { get; init; } = DatabaseProvider.PostgreSql;
        public string ConnectionString { get; init; } = "";
        public int CommandTimeoutSeconds { get; init; } = 30;

        /// <summary>Reads and validates the settings. Throws at startup so a misconfiguration is caught early.</summary>
        public static DatabaseOptions FromConfiguration(IConfiguration configuration)
        {
            var section = configuration.GetSection(SectionName);
            var providerText = section["Provider"] ?? nameof(DatabaseProvider.PostgreSql);
            if (!Enum.TryParse<DatabaseProvider>(providerText, ignoreCase: true, out var provider))
                throw new InvalidOperationException(
                    $"Database:Provider \"{providerText}\" is not supported. Use \"{nameof(DatabaseProvider.PostgreSql)}\" or \"{nameof(DatabaseProvider.FabricWarehouse)}\".");

            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionStringName} is not set. Set it with user-secrets, an environment variable " +
                    $"(ConnectionStrings__{ConnectionStringName}) or an App Service connection string.");

            return new DatabaseOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                CommandTimeoutSeconds = section.GetValue("CommandTimeoutSeconds", 30),
            };
        }
    }
}
