using System.Security.Cryptography;
using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Core.Interfaces;
using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Interfaces;

namespace Gradian.Mdm.Services.Service
{
    /// <summary>
    /// PBKDF2-SHA256 password hashing. Hashes are stored as <c>PBKDF2$&lt;iterations&gt;$&lt;salt b64&gt;$&lt;hash b64&gt;</c>,
    /// so the iteration count can be raised later without breaking existing passwords.
    /// </summary>
    public static class PasswordHasher
    {
        /// <summary>OWASP's recommended minimum for PBKDF2-HMAC-SHA256.</summary>
        public const int Iterations = 600_000;

        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        /// <summary>Hashes a password with a new random salt.</summary>
        public static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
            return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>Checks a password against a stored hash in constant time. False for a missing or malformed hash.</summary>
        public static bool Verify(string? password, string? stored)
        {
            var parts = stored?.Split('$');
            if (parts is not { Length: 4 } || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var iterations) || iterations < 1)
                return false;
            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    /// <summary>Sign-in and user administration over <c>mdm.user_account</c>.</summary>
    public class UserAccountService : IUserAccountService
    {
        /// <summary>Shortest password accepted when an admin creates or resets an account.</summary>
        public const int MinPasswordLength = 12;

        // Checked when the login doesn't exist, so an unknown user name takes as long as a wrong password
        private static readonly string DummyHash = PasswordHasher.Hash(Guid.NewGuid().ToString());

        private readonly IUnitOfWork _unitOfWork;

        public UserAccountService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        public async Task<UserAccount?> ValidateAsync(string login, string password)
        {
            var account = await _unitOfWork.UserAccountRepository.GetAsync(login?.Trim() ?? "");
            var passwordOk = PasswordHasher.Verify(password, account?.PasswordHash ?? DummyHash);
            if (account == null || !account.IsActive || !passwordOk) return null;

            await _unitOfWork.UserAccountRepository.TouchLastLoginAsync(account.Login);
            return account;
        }

        public Task<IReadOnlyList<UserAccount>> GetAllAsync() => _unitOfWork.UserAccountRepository.GetAllAsync();

        public Task<UserAccount?> GetAsync(string login) => _unitOfWork.UserAccountRepository.GetAsync(login);

        public async Task CreateAsync(string login, string? displayName, int levelNo, string? password)
        {
            login = login?.Trim() ?? "";
            if (login.Length == 0) throw new InvalidOperationException("Login is required.");
            if (login.Length > 100) throw new InvalidOperationException("Login can be at most 100 characters.");
            CheckPassword(password);
            var level = ValidLevel(levelNo);
            if (await _unitOfWork.UserAccountRepository.GetAsync(login) != null)
                throw new InvalidOperationException($"User \"{login}\" already exists.");

            await _unitOfWork.UserAccountRepository.CreateAsync(new UserAccount
            {
                Login = login,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? login : displayName.Trim(),
                LevelNo = level,
                LevelDesc = AccessLevel.Name(level),
                PasswordHash = PasswordHasher.Hash(password!),
                IsActive = true,
            });
        }

        public async Task UpdateAsync(string login, string? displayName, int levelNo, bool isActive, string? newPassword)
        {
            var account = await _unitOfWork.UserAccountRepository.GetAsync(login)
                          ?? throw new InvalidOperationException($"User \"{login}\" was not found.");
            if (!string.IsNullOrEmpty(newPassword)) CheckPassword(newPassword);

            account.DisplayName = string.IsNullOrWhiteSpace(displayName) ? account.Login : displayName.Trim();
            account.LevelNo = ValidLevel(levelNo);
            account.LevelDesc = AccessLevel.Name(account.LevelNo);
            account.IsActive = isActive;
            await _unitOfWork.UserAccountRepository.UpdateAsync(account);

            if (!string.IsNullOrEmpty(newPassword))
                await _unitOfWork.UserAccountRepository.SetPasswordAsync(account.Login, PasswordHasher.Hash(newPassword));
        }

        private static void CheckPassword(string? password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
                throw new InvalidOperationException($"Password must be at least {MinPasswordLength} characters.");
        }

        private static int ValidLevel(int levelNo) =>
            AccessLevel.Names.ContainsKey(levelNo) ? levelNo : throw new InvalidOperationException("Access level must be 1 to 4.");
    }
}
