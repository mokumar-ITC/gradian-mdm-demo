namespace Gradian.Mdm.Services.DTO
{
    /// <summary>The signed-in user's access level, as checked by the role filter.</summary>
    public sealed record UserRole(string Login, int LevelNo, string? LevelDesc);

    /// <summary>Access levels. Each level includes everything below it.</summary>
    public static class AccessLevel
    {
        public const int Viewer = 1;   // browse products
        public const int Maker = 2;    // submit changes for approval
        public const int Checker = 3;  // approve / reject, and edit directly
        public const int Admin = 4;    // manage users and column settings

        public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
        {
            [Viewer] = "Viewer", [Maker] = "Maker", [Checker] = "Checker", [Admin] = "Admin",
        };

        /// <summary>The level's name, or "Level n" for an unknown level.</summary>
        public static string Name(int level) => Names.TryGetValue(level, out var name) ? name : $"Level {level}";
    }
}
