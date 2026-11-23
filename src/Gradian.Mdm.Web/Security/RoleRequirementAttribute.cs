using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace Gradian.Mdm.Web.Security
{
    /// <summary>
    /// Requires the signed-in user to have at least the given access level (see <see cref="AccessLevel"/>).
    /// The level is read from the database, not from the sign-in cookie, so a level change or a disabled account takes
    /// effect within <see cref="RoleCache.Lifetime"/> even for users who are already signed in.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RoleRequirementAttribute : Attribute, IFilterFactory
    {
        private readonly int _requiredLevel;

        /// <param name="requiredLevel">1 = Viewer, 2 = Maker, 3 = Checker, 4 = Admin.</param>
        public RoleRequirementAttribute(int requiredLevel) => _requiredLevel = requiredLevel;

        public bool IsReusable => false;

        public IFilterMetadata CreateInstance(IServiceProvider services) =>
            new RoleRequirementFilter(_requiredLevel,
                services.GetRequiredService<IUserRoleService>(),
                services.GetRequiredService<IMemoryCache>(),
                services.GetRequiredService<ILogger<RoleRequirementFilter>>());
    }

    /// <summary>The filter created by <see cref="RoleRequirementAttribute"/> for each request.</summary>
    public sealed class RoleRequirementFilter : IAsyncAuthorizationFilter
    {
        private readonly int _requiredLevel;
        private readonly IUserRoleService _userRoleService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<RoleRequirementFilter> _logger;

        public RoleRequirementFilter(int requiredLevel, IUserRoleService userRoleService, IMemoryCache cache, ILogger<RoleRequirementFilter> logger)
        {
            _requiredLevel = requiredLevel;
            _userRoleService = userRoleService;
            _cache = cache;
            _logger = logger;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var userName = context.HttpContext.User.Identity?.IsAuthenticated == true ? context.HttpContext.User.Identity.Name : null;
            if (string.IsNullOrEmpty(userName))
            {
                context.Result = new ChallengeResult();   // redirects to the login page
                return;
            }

            var role = await _cache.GetOrCreateAsync(RoleCache.Key(userName), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = RoleCache.Lifetime;
                return _userRoleService.GetUserRoleAsync(userName);
            });

            _userRoleService.CurrentUserName = userName;
            _userRoleService.CurrentUserRole = role;

            if (role == null || role.LevelNo < _requiredLevel)
            {
                _logger.LogWarning("Access denied for {User} (level {Level}) to {Path}: requires level {Required}",
                    userName, role?.LevelNo, context.HttpContext.Request.Path.Value, _requiredLevel);
                context.Result = new RedirectToActionResult("AccessDenied", "Error", new { required = _requiredLevel });
            }
        }
    }

    /// <summary>Cache of each user's access level. Remove a user's entry after changing their account.</summary>
    public static class RoleCache
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        public static string Key(string login) => "role:" + login.ToLowerInvariant();

        /// <summary>Drops the cached level so the next request reads the account again.</summary>
        public static void Forget(this IMemoryCache cache, string login) => cache.Remove(Key(login));
    }

    /// <summary>Names of the rate-limiting policies registered in Program.cs.</summary>
    public static class RateLimitPolicies
    {
        public const string Login = "login";
    }
}
