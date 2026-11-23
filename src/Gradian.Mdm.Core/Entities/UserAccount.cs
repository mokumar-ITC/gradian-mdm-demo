namespace Gradian.Mdm.Core.Entities
{
    /// <summary>A sign-in account from <c>mdm.user_account</c>.</summary>
    public class UserAccount
    {
        public string Login { get; set; } = "";
        public string? DisplayName { get; set; }

        /// <summary>Access level: 1 = Viewer, 2 = Maker, 3 = Checker, 4 = Admin.</summary>
        public int LevelNo { get; set; }

        public string? LevelDesc { get; set; }

        /// <summary>PBKDF2 hash in the format written by <c>PasswordHasher</c>; never the plain password.</summary>
        public string? PasswordHash { get; set; }

        public bool IsActive { get; set; }
        public DateTime? LastLogin { get; set; }
    }
}
