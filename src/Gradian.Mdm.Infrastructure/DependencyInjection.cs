using Gradian.Mdm.Core.Interfaces;
using Gradian.Mdm.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gradian.Mdm.Infrastructure
{
    /// <summary>Registers data access for the configured database provider.</summary>
    public static class DependencyInjection
    {
        /// <summary>
        /// Adds <see cref="IUnitOfWork"/> (scoped, one connection per request) for PostgreSQL or Fabric Warehouse,
        /// as selected by <c>Database:Provider</c>. Throws when the settings are missing or invalid.
        /// </summary>
        public static IServiceCollection AddMdmDataAccess(this IServiceCollection services, IConfiguration configuration)
        {
            var options = DatabaseOptions.FromConfiguration(configuration);
            services.AddSingleton(options);
            services.AddSingleton(ISqlDialect.For(options.Provider));
            services.AddScoped<DbSession>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            return services;
        }
    }

    /// <summary>Health check for <c>/health</c>: opens a connection and runs <c>SELECT 1</c>.</summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly DbSession _db;

        public DatabaseHealthCheck(DbSession db) => _db = db;

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await _db.ScalarAsync("SELECT 1");
                return HealthCheckResult.Healthy($"{_db.Dialect.Provider} reachable");
            }
            catch (Exception ex)
            {
                // The exception goes to the log only; the endpoint shows just the status, so no connection details leak
                return HealthCheckResult.Unhealthy($"{_db.Dialect.Provider} unreachable", ex);
            }
        }
    }
}
