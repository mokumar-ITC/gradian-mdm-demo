using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Interfaces;
using Gradian.Mdm.Web.Models;
using Gradian.Mdm.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Gradian.Mdm.Web.Controllers
{
    /// <summary>User and access management. Admin (level 4) only.</summary>
    [RoleRequirement(AccessLevel.Admin)]
    public class UsersController : Controller
    {
        private readonly IUserAccountService _userAccountService;
        private readonly IUserRoleService _userRoleService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<UsersController> _logger;

        public UsersController(IUserAccountService userAccountService, IUserRoleService userRoleService, IMemoryCache cache,
            ILogger<UsersController> logger)
        {
            _userAccountService = userAccountService;
            _userRoleService = userRoleService;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>Lists every account.</summary>
        public async Task<IActionResult> Index() => View(await _userAccountService.GetAllAsync());

        /// <summary>Shows the form for a new account (no id) or an existing one.</summary>
        public async Task<IActionResult> Edit(string? id)
        {
            if (string.IsNullOrEmpty(id)) return View(new UserEditViewModel { IsNew = true, LevelNo = AccessLevel.Viewer, IsActive = true });

            var account = await _userAccountService.GetAsync(id);
            if (account == null) return NotFound();
            return View(new UserEditViewModel
            {
                Login = account.Login,
                DisplayName = account.DisplayName,
                LevelNo = account.LevelNo,
                IsActive = account.IsActive,
            });
        }

        /// <summary>Creates or updates an account. An admin can't remove their own Admin access or disable themselves.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserEditViewModel model)
        {
            try
            {
                if (model.IsNew)
                {
                    await _userAccountService.CreateAsync(model.Login, model.DisplayName, model.LevelNo, model.Password);
                    TempData["alertMessage"] = $"User \"{model.Login}\" created.";
                }
                else
                {
                    if (string.Equals(model.Login, _userRoleService.CurrentUserName, StringComparison.OrdinalIgnoreCase)
                        && (model.LevelNo < AccessLevel.Admin || !model.IsActive))
                        throw new InvalidOperationException("You cannot remove your own Admin access or disable your own account.");

                    await _userAccountService.UpdateAsync(model.Login, model.DisplayName, model.LevelNo, model.IsActive, model.Password);
                    TempData["alertMessage"] = $"User \"{model.Login}\" updated.";
                }

                _logger.LogInformation("{Admin} saved user {Login} (level {Level}, active {Active})",
                    _userRoleService.CurrentUserName, model.Login, model.LevelNo, model.IsActive);
                _cache.Forget(model.Login);   // the change applies on the user's next request
                return RedirectToAction(nameof(Index));
            }
            catch (InvalidOperationException ex)
            {
                ViewBag.Error = ex.Message;
                model.Password = null;   // never echo a password back into the page
                return View(model);
            }
        }
    }
}
