using Gradian.Mdm.Core.Interfaces;
using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Interfaces;

namespace Gradian.Mdm.Services.Service
{
    /// <summary>Holds the current request's user and reads access levels from <c>mdm.user_account</c>.</summary>
    public class UserRoleService : IUserRoleService
    {
        private readonly IUnitOfWork _unitOfWork;

        public UserRoleService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public string CurrentUserName { get; set; } = "";
        public UserRole? CurrentUserRole { get; set; }

        public async Task<UserRole?> GetUserRoleAsync(string userName)
        {
            var account = await _unitOfWork.UserAccountRepository.GetAsync(userName);
            return account is { IsActive: true }
                ? new UserRole(account.Login, account.LevelNo, account.LevelDesc ?? AccessLevel.Name(account.LevelNo))
                : null;
        }
    }
}
