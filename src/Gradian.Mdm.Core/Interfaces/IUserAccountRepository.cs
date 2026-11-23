using Gradian.Mdm.Core.Entities;

namespace Gradian.Mdm.Core.Interfaces
{
    /// <summary>Sign-in accounts. Logins are compared without regard to case.</summary>
    public interface IUserAccountRepository
    {
        Task<IReadOnlyList<UserAccount>> GetAllAsync();
        Task<UserAccount?> GetAsync(string login);
        Task CreateAsync(UserAccount account);

        /// <summary>Updates display name, level and active flag. The password is changed with <see cref="SetPasswordAsync"/>.</summary>
        Task UpdateAsync(UserAccount account);

        Task SetPasswordAsync(string login, string passwordHash);
        Task TouchLastLoginAsync(string login);
    }
}
