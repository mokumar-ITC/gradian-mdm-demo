using System.Security.Claims;
using Gradian.Mdm.Services.Interfaces;
using Gradian.Mdm.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace Gradian.Mdm.Web.Controllers
{
    /// <summary>Sign-in and sign-out with accounts from <c>mdm.user_account</c> (cookie authentication).</summary>
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private const string FailedLoginMessage = "Invalid user name or password, or the account is disabled.";

        private readonly IUserAccountService _userAccountService;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;

        public AccountController(IUserAccountService userAccountService, IMemoryCache cache,
            IConfiguration configuration, ILogger<AccountController> logger)
        {
            _userAccountService = userAccountService;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>Shows the sign-in page, or goes straight on when the user is already signed in.</summary>
        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturnUrl(returnUrl));
            await SetDemoAccountsAsync();
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        /// <summary>Checks the credentials and issues the sign-in cookie. Rate-limited per client address.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimitPolicies.Login)]
        public async Task<IActionResult> Login(string login, string password, string? returnUrl = null)
        {
            var account = await _userAccountService.ValidateAsync(login ?? "", password ?? "");
            if (account == null)
            {
                _logger.LogWarning("Failed sign-in for {Login} from {Address}", login, HttpContext.Connection.RemoteIpAddress);
                ViewBag.Error = FailedLoginMessage;   // same message whatever was wrong, so accounts can't be probed
                ViewBag.Login = login;
                ViewBag.ReturnUrl = returnUrl;
                await SetDemoAccountsAsync();
                return View();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, account.Login),
                new("DisplayName", account.DisplayName ?? account.Login),
                new("LevelNo", account.LevelNo.ToString()),
                new("LevelDesc", account.LevelDesc ?? ""),
            };
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

            _cache.Forget(account.Login);   // the first request reads the current level
            _logger.LogInformation("User {Login} signed in (level {Level})", account.Login, account.LevelNo);
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        /// <summary>Signs out and returns to the sign-in page.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            if (User.Identity?.Name is { } name) _cache.Forget(name);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        /// <summary>Only local URLs are followed after sign-in, which prevents open redirects.</summary>
        private string SafeReturnUrl(string? returnUrl) =>
            !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Index", "ProductMaster")!;

        /// <summary>
        /// With <c>Demo:ShowDemoAccounts</c> on, lists the active accounts on the sign-in page so a presenter can switch
        /// roles quickly. Keep it off for any environment reachable by people outside the demo.
        /// </summary>
        private async Task SetDemoAccountsAsync()
        {
            if (!_configuration.GetValue<bool>("Demo:ShowDemoAccounts")) return;
            ViewBag.DemoAccounts = (await _userAccountService.GetAllAsync()).Where(a => a.IsActive).OrderBy(a => a.LevelNo).ToList();
            ViewBag.DemoPassword = _configuration["Demo:Password"];
        }
    }
}
