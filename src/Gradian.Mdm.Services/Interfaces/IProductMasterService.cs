using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Services.DTO;

namespace Gradian.Mdm.Services.Interfaces
{
    /// <summary>Product master: hierarchy browse, details, single and bulk edits, and approvals.</summary>
    public interface IProductMasterService
    {
        // nodeId: limit to that hierarchy node and everything below it; search: name, code, path or capacity group;
        // approval: a ProductApproval value (PendingApproval, Approved, Rejected, UpdateDirectly, Unchanged)
        Task<ProductMasterBrowse> BrowseAsync(long? nodeId, string? search, string? status, string? approval = null);
        Task<ProductMasterDetail?> GetDetailAsync(long productId);
        Task<IReadOnlyList<ProductMasterField>> GetFieldsAsync();

        // Maker / checker workflow:
        //   level 2 (Maker)  -> the change waits for approval
        //   level 3+ (Checker, Admin) -> the change is applied directly
        Task<ProductEditPage?> GetEditAsync(long productId, int userLevel, ProductEditForm? posted = null);
        Task<ProductWorkflowResult> SubmitEditAsync(ProductEditForm form, string userName, int userLevel, string? roleDesc);
        Task<IReadOnlyList<ProductMasterAudit>> GetPendingAsync();
        Task<ProductReview?> GetReviewAsync(long auditId, string userName);
        Task<ProductWorkflowResult> ApproveAsync(long auditId, string checkerId, int userLevel, string? roleDesc, string? remarks);
        Task<ProductWorkflowResult> RejectAsync(long auditId, string checkerId, int userLevel, string? roleDesc, string? remarks);

        // Bulk update: the selected products in one grid, same maker / checker rules as Edit. Products with a change
        // waiting for approval are left out of the grid. The batch is saved only when every row is valid.
        Task<ProductBulkEditPage> GetBulkEditAsync(IEnumerable<long> productIds, int userLevel, ProductBulkEditForm? posted = null,
            IReadOnlyDictionary<long, string>? rowErrors = null);
        Task<ProductBulkResult> SubmitBulkEditAsync(ProductBulkEditForm form, string userName, int userLevel, string? roleDesc);

        // Bulk decision on several pending requests. Each request is decided on its own, with the same rules as
        // Approve / Reject (for example a Checker can't decide their own request); the message lists any that were skipped.
        Task<ProductBulkResult> ApproveManyAsync(IEnumerable<long> auditIds, string checkerId, int userLevel, string? roleDesc, string? remarks);
        Task<ProductBulkResult> RejectManyAsync(IEnumerable<long> auditIds, string checkerId, int userLevel, string? roleDesc, string? remarks);

        // Column labels for the product table, including any an admin renamed (column key -> label)
        Task<IReadOnlyDictionary<string, string>> LabelsAsync();
    }
}
