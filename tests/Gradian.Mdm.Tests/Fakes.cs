using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Core.Interfaces;

namespace Gradian.Mdm.Tests
{
    /// <summary>In-memory unit of work for service tests.</summary>
    internal sealed class FakeUnitOfWork : IUnitOfWork
    {
        public FakeProductRepository Products { get; } = new();
        public FakeColumnSettingRepository ColumnSettings { get; } = new();
        public FakeUserRepository Users { get; } = new();

        public IProductMasterRepository ProductMasterRepository => Products;
        public IColumnEditSettingRepository ColumnEditSettingRepository => ColumnSettings;
        public IUserAccountRepository UserAccountRepository => Users;

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FakeProductRepository : IProductMasterRepository
    {
        public List<ProductMasterRecord> Items { get; } = new();
        public List<ProductMasterAudit> Audits { get; } = new();

        public static ProductMasterRecord Product(long id, string name) => new()
        {
            ProductId = id, NodeId = 1, NodePath = "Air Cooling > AHU", ProductFamily = "Air Cooling", ProductType = "AHU",
            ProductName = name, Status = "Active", SalePricePerUnit = 85000m, QtyFans = 2,
        };

        public Task<IReadOnlyList<ProductHierarchyNode>> GetHierarchyAsync() =>
            Task.FromResult<IReadOnlyList<ProductHierarchyNode>>(new[]
            {
                new ProductHierarchyNode { NodeId = 1, LevelNo = 1, LevelName = "Product Family", NodeName = "Air Cooling", NodePath = "Air Cooling", ProductCount = Items.Count },
            });

        public Task<IReadOnlyList<ProductMasterRecord>> GetProductsAsync() => Task.FromResult<IReadOnlyList<ProductMasterRecord>>(Items.ToList());
        public Task<ProductMasterRecord?> GetProductAsync(long productId) => Task.FromResult(Items.FirstOrDefault(p => p.ProductId == productId));
        public Task<IReadOnlyList<ProductMasterField>> GetFieldsAsync() => Task.FromResult<IReadOnlyList<ProductMasterField>>(Array.Empty<ProductMasterField>());

        public Task<bool> NameExistsAsync(string productName, long productId) =>
            Task.FromResult(Items.Any(p => p.ProductId != productId && string.Equals(p.ProductName, productName.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task UpdateDirectlyAsync(long productId, ProductMasterValues values, string userName, string role, string? remarks)
        {
            var p = Items.Single(i => i.ProductId == productId);
            p.CopyFrom(values);
            p.UpdateStatus = "UpdateDirectly";
            Audits.Add(NewAudit(productId, values, "UpdateDirectly", userName));
            return Task.CompletedTask;
        }

        public Task<long> SubmitForApprovalAsync(long productId, ProductMasterValues values, string makerId, string role, string? remarks)
        {
            if (Audits.Any(a => a.ProductId == productId && a.ApprovalStatus == "PendingApproval"))
                throw new InvalidOperationException("This product already has a change waiting for approval.");
            Items.Single(i => i.ProductId == productId).UpdateStatus = "PendingApproval";
            var audit = NewAudit(productId, values, "PendingApproval", makerId);
            Audits.Add(audit);
            return Task.FromResult(audit.AuditId);
        }

        public Task<IReadOnlyList<ProductMasterAudit>> GetAuditsAsync(string? approvalStatus = null, long? productId = null) =>
            Task.FromResult<IReadOnlyList<ProductMasterAudit>>(Audits
                .Where(a => (approvalStatus == null || a.ApprovalStatus == approvalStatus) && (productId == null || a.ProductId == productId))
                .ToList());

        public Task<ProductMasterAudit?> GetAuditAsync(long auditId) => Task.FromResult(Audits.FirstOrDefault(a => a.AuditId == auditId));

        public Task<bool> ApproveAsync(long auditId, ProductMasterValues values, string checkerId, string role, string? remarks)
        {
            var audit = Audits.Single(a => a.AuditId == auditId);
            if (audit.ApprovalStatus != "PendingApproval") return Task.FromResult(false);
            audit.ApprovalStatus = "Approved";
            audit.CheckerId = checkerId;
            var p = Items.Single(i => i.ProductId == audit.ProductId);
            p.CopyFrom(values);
            p.UpdateStatus = "Approved";
            return Task.FromResult(true);
        }

        public Task<bool> RejectAsync(long auditId, string checkerId, string role, string remarks)
        {
            var audit = Audits.Single(a => a.AuditId == auditId);
            if (audit.ApprovalStatus != "PendingApproval") return Task.FromResult(false);
            audit.ApprovalStatus = "Rejected";
            Items.Single(i => i.ProductId == audit.ProductId).UpdateStatus = "Rejected";
            return Task.FromResult(true);
        }

        private ProductMasterAudit NewAudit(long productId, ProductMasterValues values, string status, string maker)
        {
            var audit = new ProductMasterAudit { AuditId = Audits.Count + 1, ProductId = productId, ApprovalStatus = status, MakerId = maker };
            audit.CopyFrom(values);
            return audit;
        }
    }

    internal sealed class FakeColumnSettingRepository : IColumnEditSettingRepository
    {
        public List<ColumnEditSetting> Rows { get; } = new();

        public Task<IReadOnlyList<ColumnEditSetting>> GetAllAsync() => Task.FromResult<IReadOnlyList<ColumnEditSetting>>(Rows.ToList());

        public Task<IReadOnlyList<ColumnEditSetting>> GetByTableAsync(string tableKey) =>
            Task.FromResult<IReadOnlyList<ColumnEditSetting>>(Rows.Where(r => r.TableKey == tableKey).ToList());

        public Task SaveEditableAsync(string tableKey, string columnKey, bool isEditable, string userName)
        {
            Row(tableKey, columnKey).IsEditable = isEditable;
            return Task.CompletedTask;
        }

        public Task SaveDisplayNameAsync(string tableKey, string columnKey, string? displayName, string userName)
        {
            Row(tableKey, columnKey).DisplayName = displayName;
            return Task.CompletedTask;
        }

        private ColumnEditSetting Row(string tableKey, string columnKey)
        {
            var row = Rows.FirstOrDefault(r => r.TableKey == tableKey && r.ColumnKey == columnKey);
            if (row == null) Rows.Add(row = new ColumnEditSetting { TableKey = tableKey, ColumnKey = columnKey });
            return row;
        }
    }

    internal sealed class FakeUserRepository : IUserAccountRepository
    {
        public List<UserAccount> Rows { get; } = new();

        public Task<IReadOnlyList<UserAccount>> GetAllAsync() => Task.FromResult<IReadOnlyList<UserAccount>>(Rows.ToList());

        public Task<UserAccount?> GetAsync(string login) =>
            Task.FromResult(Rows.FirstOrDefault(r => string.Equals(r.Login, login, StringComparison.OrdinalIgnoreCase)));

        public Task CreateAsync(UserAccount account) { Rows.Add(account); return Task.CompletedTask; }
        public Task UpdateAsync(UserAccount account) => Task.CompletedTask;   // GetAsync hands out the stored instance

        public Task SetPasswordAsync(string login, string passwordHash)
        {
            Rows.Single(r => r.Login == login).PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task TouchLastLoginAsync(string login)
        {
            Rows.Single(r => r.Login == login).LastLogin = DateTime.UtcNow;
            return Task.CompletedTask;
        }
    }
}
