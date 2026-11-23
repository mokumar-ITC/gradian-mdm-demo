using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Interfaces;
using Gradian.Mdm.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace Gradian.Mdm.Web.Controllers
{
    /// <summary>Which columns can be edited on each table, and what each column is called on screen. Admin (level 4) only.</summary>
    public class ColumnSettingsController : Controller
    {
        private readonly IColumnEditService _columnEdit;
        private readonly IUserRoleService _userRoleService;

        public ColumnSettingsController(IColumnEditService columnEdit, IUserRoleService userRoleService)
        {
            _columnEdit = columnEdit;
            _userRoleService = userRoleService;
        }

        [RoleRequirement(4)]
        public async Task<IActionResult> Index()
        {
            return View(await _columnEdit.GetStatusAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RoleRequirement(4)]
        public async Task<IActionResult> Save(string tableKey, List<string> editable, Dictionary<string, string?>? displayNames)
        {
            var table = ColumnEditCatalog.Find(tableKey);
            if (table == null) return NotFound();

            // Names are checked first, so a bad name saves nothing
            var (renamed, error) = await _columnEdit.SaveDisplayNamesAsync(table.Key, displayNames ?? new(), _userRoleService.CurrentUserName);
            if (error != null)
            {
                TempData["errorMessage"] = $"{table.Label}: {error}";
                return RedirectToAction(nameof(Index));
            }

            var locks = await _columnEdit.SaveAsync(table.Key, editable, _userRoleService.CurrentUserName);
            var parts = new List<string>();
            if (renamed > 0) parts.Add($"{renamed} column name(s)");
            if (locks > 0) parts.Add($"{locks} column lock(s)");
            TempData["alertMessage"] = parts.Count == 0
                ? $"{table.Label}: no changes to save."
                : $"{table.Label}: {string.Join(" and ", parts)} updated.";
            return RedirectToAction(nameof(Index));
        }
    }
}
