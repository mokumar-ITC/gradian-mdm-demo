using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Interfaces;
using Gradian.Mdm.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace Gradian.Mdm.Web.Controllers
{
    /// <summary>
    /// Product master: browse Product Family -> Product Type -> Model -> Size -> Product, and change products
    /// through the maker / checker process.
    /// </summary>
    public class ProductMasterController : Controller
    {
        private readonly IProductMasterService _productMaster;
        private readonly IUserRoleService _userRoleService;
        private readonly ILogger<ProductMasterController> _logger;

        public ProductMasterController(IProductMasterService productMaster, IUserRoleService userRoleService,
            ILogger<ProductMasterController> logger)
        {
            _productMaster = productMaster;
            _userRoleService = userRoleService;
            _logger = logger;
        }

        private int UserLevel => _userRoleService.CurrentUserRole?.LevelNo ?? 0;
        private string UserName => _userRoleService.CurrentUserName;
        private string? RoleDesc => _userRoleService.CurrentUserRole?.LevelDesc;

        [RoleRequirement(1)] //1 => read
        public async Task<IActionResult> Index(long? nodeId, string? search, string? status, string? approval)
        {
            ViewBag.UserLevel = UserLevel;
            ViewBag.UserName = UserName;
            return View(await _productMaster.BrowseAsync(nodeId, search, status, approval));
        }

        [RoleRequirement(1)]
        public async Task<IActionResult> Details(long id)
        {
            ViewBag.UserLevel = UserLevel;
            var detail = await _productMaster.GetDetailAsync(id);
            return detail == null ? NotFound() : View(detail);
        }

        [RoleRequirement(1)]
        public async Task<IActionResult> Fields()
        {
            return View(await _productMaster.GetFieldsAsync());
        }

        // ------------------------------------------------------------ edit

        [RoleRequirement(2)] //2 => Maker or above
        public async Task<IActionResult> Edit(long id)
        {
            var page = await _productMaster.GetEditAsync(id, UserLevel);
            if (page == null) return NotFound();
            if (page.Product.UpdateStatus == ProductApproval.Pending)
            {
                TempData["errorMessage"] = "This product has a change waiting for approval. It can be edited again once that is approved or rejected.";
                return RedirectToAction(nameof(Details), new { id });
            }
            return View(page);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RoleRequirement(2)]
        public async Task<IActionResult> Edit(ProductEditForm form)
        {
            var result = await _productMaster.SubmitEditAsync(form, UserName, UserLevel, RoleDesc);
            if (!result.Success)
            {
                var page = await _productMaster.GetEditAsync(form.ProductId, UserLevel, form);
                if (page == null) return NotFound();
                ViewBag.FormError = result.Message;
                return View(page);
            }

            _logger.LogInformation("{User} (level {Level}) changed product {ProductId}: {Message}", UserName, UserLevel, form.ProductId, result.Message);
            TempData["alertMessage"] = result.Message;
            return RedirectToAction(nameof(Details), new { id = form.ProductId });
        }

        // ------------------------------------------------------------ bulk update (rows selected in the list)

        [RoleRequirement(2)] //2 => Maker or above
        public async Task<IActionResult> BulkUpdate(List<long> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                TempData["errorMessage"] = "Select one or more products in the list, then choose Bulk update.";
                return RedirectToAction(nameof(Index));
            }
            var page = await _productMaster.GetBulkEditAsync(ids, UserLevel);
            if (page.Form.Rows.Count == 0)
            {
                TempData["errorMessage"] = "None of the selected products can be updated now. " + string.Join(" ", page.Skipped);
                return RedirectToAction(nameof(Index));
            }
            return View(page);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RoleRequirement(2)]
        [RequestFormLimits(ValueCountLimit = 10000)]   // about 20 fields per row, up to ProductBulk.MaxRows rows
        public async Task<IActionResult> BulkUpdate(ProductBulkEditForm form)
        {
            var result = await _productMaster.SubmitBulkEditAsync(form, UserName, UserLevel, RoleDesc);
            if (!result.Success)
            {
                var page = await _productMaster.GetBulkEditAsync(form.Rows.Select(r => r.ProductId), UserLevel, form, result.RowErrors);
                if (page.Form.Rows.Count == 0) return RedirectToAction(nameof(Index));
                ViewBag.FormError = result.Message;
                return View(page);
            }

            _logger.LogInformation("{User} (level {Level}) bulk-updated {Count} product(s): {Message}", UserName, UserLevel, form.Rows.Count, result.Message);
            SetBulkMessage(result);
            return UserLevel >= 3
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(Index), new { approval = ProductApproval.Pending });
        }

        // ------------------------------------------------------------ approval (checker)

        [RoleRequirement(3)] //3 => Checker or above
        public async Task<IActionResult> Approvals()
        {
            ViewBag.UserName = UserName;
            return View(await _productMaster.GetPendingAsync());
        }

        [RoleRequirement(3)]
        public async Task<IActionResult> Review(long id)
        {
            var review = await _productMaster.GetReviewAsync(id, UserName);
            return review == null ? NotFound() : View(review);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RoleRequirement(3)]
        public async Task<IActionResult> Approve(long auditId, string? remarks, string? returnTo)
        {
            var result = await _productMaster.ApproveAsync(auditId, UserName, UserLevel, RoleDesc, remarks);
            return AfterDecision(result, auditId, "approved", returnTo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RoleRequirement(3)]
        public async Task<IActionResult> Reject(long auditId, string? remarks)
        {
            var result = await _productMaster.RejectAsync(auditId, UserName, UserLevel, RoleDesc, remarks);
            return AfterDecision(result, auditId, "rejected", null);
        }

        // Bulk approve / reject of the requests ticked on the Product Approval page or the product list.
        // returnUrl: the page to go back to (the product list, with its filters); default is Product Approval.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RoleRequirement(3)]
        public async Task<IActionResult> ApproveSelected(List<long> auditIds, string? remarks, string? returnUrl)
        {
            var result = await _productMaster.ApproveManyAsync(auditIds, UserName, UserLevel, RoleDesc, remarks);
            return AfterBulkDecision(result, "approved", auditIds, returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RoleRequirement(3)]
        public async Task<IActionResult> RejectSelected(List<long> auditIds, string? remarks, string? returnUrl)
        {
            var result = await _productMaster.RejectManyAsync(auditIds, UserName, UserLevel, RoleDesc, remarks);
            return AfterBulkDecision(result, "rejected", auditIds, returnUrl);
        }

        private IActionResult AfterBulkDecision(ProductBulkResult result, string verb, List<long> auditIds, string? returnUrl)
        {
            _logger.LogInformation("{User} bulk {Verb} {Count} product change request(s): {Message}", UserName, verb, auditIds?.Count ?? 0, result.Message);
            SetBulkMessage(result);
            return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction(nameof(Approvals));
        }

        // Shown by _Alert as a banner that stays until closed: bulk results can list several skipped rows
        private void SetBulkMessage(ProductBulkResult result)
        {
            TempData["bulkMessage"] = result.Message;
            TempData["bulkOk"] = result.Success;
        }

        private IActionResult AfterDecision(ProductWorkflowResult result, long auditId, string verb, string? returnTo)
        {
            if (!result.Success)
            {
                TempData["errorMessage"] = result.Message;
                return returnTo == "list" ? RedirectToAction(nameof(Approvals)) : RedirectToAction(nameof(Review), new { id = auditId });
            }
            _logger.LogInformation("{User} {Verb} product change request {AuditId}", UserName, verb, auditId);
            TempData["alertMessage"] = result.Message;
            return RedirectToAction(nameof(Approvals));
        }
    }
}
