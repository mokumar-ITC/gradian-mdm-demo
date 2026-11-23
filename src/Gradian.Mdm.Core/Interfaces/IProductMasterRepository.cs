using Gradian.Mdm.Core.Entities;

namespace Gradian.Mdm.Core.Interfaces
{
    /// <summary>
    /// Product hierarchy, products and their change requests (maker / checker).
    /// Every write runs in one database transaction, so a product and its audit row always change together.
    /// </summary>
    public interface IProductMasterRepository
    {
        Task<IReadOnlyList<ProductHierarchyNode>> GetHierarchyAsync();
        Task<IReadOnlyList<ProductMasterRecord>> GetProductsAsync();
        Task<ProductMasterRecord?> GetProductAsync(long productId);
        Task<IReadOnlyList<ProductMasterField>> GetFieldsAsync();

        /// <summary>True when another product (not <paramref name="productId"/>) already uses this name, ignoring case.</summary>
        Task<bool> NameExistsAsync(string productName, long productId);

        /// <summary>Checker / Admin: writes the values straight to the product and logs an <c>UpdateDirectly</c> audit row.</summary>
        /// <exception cref="InvalidOperationException">The new name is already used by another product.</exception>
        Task UpdateDirectlyAsync(long productId, ProductMasterValues values, string userName, string role, string? remarks);

        /// <summary>Maker: stores a <c>PendingApproval</c> request and flags the product. Returns the new audit id.</summary>
        /// <exception cref="InvalidOperationException">The product already has an open request.</exception>
        Task<long> SubmitForApprovalAsync(long productId, ProductMasterValues values, string makerId, string role, string? remarks);

        Task<IReadOnlyList<ProductMasterAudit>> GetAuditsAsync(string? approvalStatus = null, long? productId = null);
        Task<ProductMasterAudit?> GetAuditAsync(long auditId);

        /// <summary>Approves a pending request and writes <paramref name="values"/> to the product.</summary>
        /// <returns><c>false</c> when the request is no longer pending (someone else already decided it).</returns>
        Task<bool> ApproveAsync(long auditId, ProductMasterValues values, string checkerId, string role, string? remarks);

        /// <summary>Rejects a pending request. The product keeps its current values.</summary>
        /// <returns><c>false</c> when the request is no longer pending.</returns>
        Task<bool> RejectAsync(long auditId, string checkerId, string role, string remarks);
    }
}
