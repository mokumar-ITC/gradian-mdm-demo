using Gradian.Mdm.Services.DTO;

namespace Gradian.Mdm.Services.Interfaces
{
    /// <summary>
    /// The current request's user. Scoped: the role filter fills <see cref="CurrentUserName"/> and
    /// <see cref="CurrentUserRole"/> before an action runs, and controllers read them.
    /// </summary>
    public interface IUserRoleService
    {
        string CurrentUserName { get; set; }
        UserRole? CurrentUserRole { get; set; }

        /// <summary>Looks up the active account's level; null when the account doesn't exist or is disabled.</summary>
        Task<UserRole?> GetUserRoleAsync(string userName);
    }
}
