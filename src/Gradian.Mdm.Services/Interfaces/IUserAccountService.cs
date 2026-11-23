using Gradian.Mdm.Core.Entities;

namespace Gradian.Mdm.Services.Interfaces
{
    /// <summary>Sign-in and user administration.</summary>
    public interface IUserAccountService
    {
        /// <summary>Returns the account when the login and password match an active account, otherwise null.</summary>
        Task<UserAccount?> ValidateAsync(string login, string password);

        Task<IReadOnlyList<UserAccount>> GetAllAsync();
        Task<UserAccount?> GetAsync(string login);

        /// <exception cref="InvalidOperationException">Missing login, weak password, unknown level or duplicate login.</exception>
        Task CreateAsync(string login, string? displayName, int levelNo, string? password);

        /// <summary>Updates an account; a non-empty <paramref name="newPassword"/> also resets the password.</summary>
        /// <exception cref="InvalidOperationException">Unknown account, unknown level or weak password.</exception>
        Task UpdateAsync(string login, string? displayName, int levelNo, bool isActive, string? newPassword);
    }
}
