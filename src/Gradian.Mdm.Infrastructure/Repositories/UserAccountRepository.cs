using System.Data;
using System.Data.Common;
using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Core.Interfaces;
using Gradian.Mdm.Infrastructure.Data;
using static Gradian.Mdm.Infrastructure.Data.DbSession;

namespace Gradian.Mdm.Infrastructure.Repositories
{
    /// <summary><c>mdm.user_account</c>: sign-in accounts and their access level. Logins are matched without regard to case.</summary>
    public sealed class UserAccountRepository : IUserAccountRepository
    {
        private const string Table = "mdm.user_account";
        private const string Columns = "login, display_name, level_no, level_desc, password_hash, is_active, last_login";

        private readonly DbSession _db;

        public UserAccountRepository(DbSession db) => _db = db;

        public Task<IReadOnlyList<UserAccount>> GetAllAsync() =>
            _db.QueryAsync($"SELECT {Columns} FROM {Table} ORDER BY level_no DESC, lower(login)", Map);

        public async Task<UserAccount?> GetAsync(string login) =>
            (await _db.QueryAsync($"SELECT {Columns} FROM {Table} WHERE lower(login) = lower(@login)", Map, P("login", login ?? ""))).FirstOrDefault();

        public Task CreateAsync(UserAccount a) =>
            _db.ExecuteAsync(
                $"INSERT INTO {Table} (login, display_name, level_no, level_desc, password_hash, is_active, created_date) " +
                "VALUES (@login, @name, @level, @desc, @hash, @active, @now)",
                P("login", a.Login), P("name", a.DisplayName, DbType.String), P("level", a.LevelNo), P("desc", a.LevelDesc, DbType.String),
                P("hash", a.PasswordHash, DbType.String), P("active", a.IsActive), P("now", DateTime.UtcNow));

        public Task UpdateAsync(UserAccount a) =>
            _db.ExecuteAsync(
                $"UPDATE {Table} SET display_name = @name, level_no = @level, level_desc = @desc, is_active = @active WHERE lower(login) = lower(@login)",
                P("name", a.DisplayName, DbType.String), P("level", a.LevelNo), P("desc", a.LevelDesc, DbType.String),
                P("active", a.IsActive), P("login", a.Login));

        public Task SetPasswordAsync(string login, string passwordHash) =>
            _db.ExecuteAsync($"UPDATE {Table} SET password_hash = @hash WHERE lower(login) = lower(@login)",
                P("hash", passwordHash), P("login", login));

        public Task TouchLastLoginAsync(string login) =>
            _db.ExecuteAsync($"UPDATE {Table} SET last_login = @now WHERE lower(login) = lower(@login)",
                P("now", DateTime.UtcNow), P("login", login));

        private static UserAccount Map(DbDataReader r) => new()
        {
            Login = r.GetString(0),
            DisplayName = r.IsDBNull(1) ? null : r.GetString(1),
            LevelNo = Convert.ToInt32(r.GetValue(2)),
            LevelDesc = r.IsDBNull(3) ? null : r.GetString(3),
            PasswordHash = r.IsDBNull(4) ? null : r.GetString(4),
            IsActive = !r.IsDBNull(5) && Convert.ToBoolean(r.GetValue(5)),
            LastLogin = r.IsDBNull(6) ? null : r.GetDateTime(6),
        };
    }
}
