using System.Data;
using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Infrastructure.Data;
using Gradian.Mdm.Services.Service;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Gradian.Mdm.Tests
{
    public class PasswordHasherTests
    {
        [Fact]
        public void Hash_verifies_only_the_same_password()
        {
            var hash = PasswordHasher.Hash("correct horse battery staple");

            Assert.StartsWith($"PBKDF2${PasswordHasher.Iterations}$", hash);
            Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
            Assert.False(PasswordHasher.Verify("Correct horse battery staple", hash));
        }

        [Fact]
        public void Same_password_gets_a_different_salt_each_time() =>
            Assert.NotEqual(PasswordHasher.Hash("same-password"), PasswordHasher.Hash("same-password"));

        [Fact]
        public void Demo_account_hashes_with_fewer_iterations_still_verify() =>
            // admin1 from db/*/003_demo_users.sql (100,000 iterations): the iteration count is read from the hash
            Assert.True(PasswordHasher.Verify("Demo@123", "PBKDF2$100000$J1s7dzfsRJf6hPuqSILbPg==$u4mmE1IplqEkIzk17RIePp8aR2bapGRchb+NxT01IEI="));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("plain-text-password")]
        [InlineData("PBKDF2$abc$salt$hash")]
        [InlineData("PBKDF2$1000$not-base64!$not-base64!")]
        public void Malformed_hashes_never_verify(string? stored) => Assert.False(PasswordHasher.Verify("anything", stored));
    }

    public class ProductMasterValuesTests
    {
        [Fact]
        public void Blank_and_trimmed_strings_and_decimal_scale_are_not_changes()
        {
            var a = new ProductMasterValues { ProductName = "AHU", ProductCode = null, SalePricePerUnit = 85000m };
            var b = new ProductMasterValues { ProductName = " AHU ", ProductCode = "  ", SalePricePerUnit = 85000.00m };
            Assert.Empty(a.ChangedFrom(b));
        }

        [Fact]
        public void Changed_properties_are_listed()
        {
            var a = new ProductMasterValues { ProductName = "AHU", QtyFans = 1, Status = "Active" };
            var b = new ProductMasterValues { ProductName = "AHU", QtyFans = 2, Status = "Archived" };
            Assert.Equal(new[] { "QtyFans", "Status" }, a.ChangedFrom(b));
        }
    }

    public class DatabaseOptionsTests
    {
        private static IConfiguration Config(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

        [Fact]
        public void Missing_connection_string_fails_at_startup()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => DatabaseOptions.FromConfiguration(Config(("Database:Provider", "PostgreSql"))));
            Assert.Contains("ConnectionStrings:Mdm", ex.Message);
        }

        [Fact]
        public void Unknown_provider_fails_at_startup() =>
            Assert.Throws<InvalidOperationException>(() => DatabaseOptions.FromConfiguration(
                Config(("Database:Provider", "Oracle"), ("ConnectionStrings:Mdm", "x"))));

        [Theory]
        [InlineData("FabricWarehouse", DatabaseProvider.FabricWarehouse)]
        [InlineData("fabricwarehouse", DatabaseProvider.FabricWarehouse)]
        [InlineData("PostgreSql", DatabaseProvider.PostgreSql)]
        [InlineData(null, DatabaseProvider.PostgreSql)]
        public void Provider_is_read_ignoring_case(string? configured, DatabaseProvider expected) =>
            Assert.Equal(expected, DatabaseOptions.FromConfiguration(
                Config(("Database:Provider", configured), ("ConnectionStrings:Mdm", "Host=x"))).Provider);
    }

    public class SqlDialectTests
    {
        [Fact]
        public void Fabric_uses_bit_literals_app_keys_and_datetime2()
        {
            var dialect = ISqlDialect.For(DatabaseProvider.FabricWarehouse);
            var parameter = new SqlParameter();
            dialect.SetValue(parameter, DateTime.UtcNow, null);

            Assert.Equal("1", dialect.True);
            Assert.False(dialect.GeneratesKeys);
            Assert.Equal(DbType.DateTime2, parameter.DbType);
        }

        [Fact]
        public void PostgreSql_uses_boolean_literals_identity_keys_and_timestamptz()
        {
            var dialect = ISqlDialect.For(DatabaseProvider.PostgreSql);
            var parameter = new NpgsqlParameter();
            dialect.SetValue(parameter, null, DbType.DateTime2);

            Assert.Equal("TRUE", dialect.True);
            Assert.True(dialect.GeneratesKeys);
            Assert.Equal(NpgsqlTypes.NpgsqlDbType.TimestampTz, parameter.NpgsqlDbType);
            Assert.Equal(DBNull.Value, parameter.Value);
        }
    }
}
