using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Service;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gradian.Mdm.Tests
{
    public class ProductMasterServiceTests
    {
        private readonly FakeUnitOfWork _uow = new();
        private readonly ProductMasterService _service;

        public ProductMasterServiceTests()
        {
            _uow.Products.Items.Add(FakeProductRepository.Product(1, "Air Cooling – AHU – Indoor"));
            _uow.Products.Items.Add(FakeProductRepository.Product(2, "Air Cooling – AHU – Outdoor"));
            _service = new ProductMasterService(_uow, new ColumnEditService(_uow, NullLogger<ColumnEditService>.Instance));
        }

        private ProductEditForm Form(long id, Action<ProductEditForm> change)
        {
            var form = new ProductEditForm { ProductId = id };
            form.CopyFrom(_uow.Products.Items.Single(p => p.ProductId == id));
            change(form);
            return form;
        }

        [Fact]
        public async Task Viewer_cannot_change_products()
        {
            var result = await _service.SubmitEditAsync(Form(1, f => f.QtyFans = 3), "viewer1", AccessLevel.Viewer, "Viewer");
            Assert.False(result.Success);
            Assert.Empty(_uow.Products.Audits);
        }

        [Fact]
        public async Task Maker_change_waits_for_approval()
        {
            var result = await _service.SubmitEditAsync(Form(1, f => f.QtyFans = 3), "maker1", AccessLevel.Maker, "Maker");

            Assert.True(result.Success);
            Assert.Equal(2, _uow.Products.Items[0].QtyFans);   // not applied yet
            Assert.Equal(ProductApproval.Pending, Assert.Single(_uow.Products.Audits).ApprovalStatus);
        }

        [Fact]
        public async Task Checker_change_is_applied_directly()
        {
            var result = await _service.SubmitEditAsync(Form(1, f => f.QtyFans = 3), "checker1", AccessLevel.Checker, "Checker");

            Assert.True(result.Success);
            Assert.Equal(3, _uow.Products.Items[0].QtyFans);
            Assert.Equal(ProductApproval.Direct, Assert.Single(_uow.Products.Audits).ApprovalStatus);
        }

        [Theory]
        [InlineData("QtyCoils", "can't be negative")]
        [InlineData("ProductName", "is required")]
        [InlineData("FabLbrHrsPct", "more than 100")]
        [InlineData("Status", "must be Active, Inactive or Archived")]
        public async Task Invalid_values_are_refused(string field, string expectedMessage)
        {
            var form = Form(1, f =>
            {
                switch (field)
                {
                    case "QtyCoils": f.QtyCoils = -1; break;
                    case "ProductName": f.ProductName = "  "; break;
                    case "FabLbrHrsPct": f.FabLbrHrsPct = 101; break;
                    case "Status": f.Status = "Deleted"; break;
                }
            });

            var result = await _service.SubmitEditAsync(form, "admin1", AccessLevel.Admin, "Admin");

            Assert.False(result.Success);
            Assert.Contains(expectedMessage, result.Message);
        }

        [Fact]
        public async Task Duplicate_name_is_refused_ignoring_case()
        {
            var result = await _service.SubmitEditAsync(Form(1, f => f.ProductName = "AIR COOLING – AHU – OUTDOOR"), "admin1", AccessLevel.Admin, "Admin");
            Assert.False(result.Success);
            Assert.Contains("already named", result.Message);
        }

        [Fact]
        public async Task Saving_without_changes_is_refused()
        {
            var result = await _service.SubmitEditAsync(Form(1, _ => { }), "admin1", AccessLevel.Admin, "Admin");
            Assert.False(result.Success);
            Assert.Contains("Nothing was changed", result.Message);
        }

        [Fact]
        public async Task Checker_cannot_decide_own_request()
        {
            await _service.SubmitEditAsync(Form(1, f => f.QtyFans = 5), "checker1", AccessLevel.Maker, "Maker");
            var auditId = _uow.Products.Audits.Single().AuditId;

            var result = await _service.ApproveAsync(auditId, "checker1", AccessLevel.Checker, "Checker", null);

            Assert.False(result.Success);
            Assert.Contains("your own change", result.Message);
        }

        [Fact]
        public async Task Approval_applies_values_and_reject_needs_a_comment()
        {
            await _service.SubmitEditAsync(Form(1, f => f.QtyFans = 5), "maker1", AccessLevel.Maker, "Maker");
            var auditId = _uow.Products.Audits.Single().AuditId;

            Assert.False((await _service.RejectAsync(auditId, "checker1", AccessLevel.Checker, "Checker", " ")).Success);
            Assert.True((await _service.ApproveAsync(auditId, "checker1", AccessLevel.Checker, "Checker", "ok")).Success);
            Assert.Equal(5, _uow.Products.Items[0].QtyFans);
            Assert.False((await _service.ApproveAsync(auditId, "checker1", AccessLevel.Checker, "Checker", "again")).Success);
        }

        [Fact]
        public async Task Locked_column_keeps_its_stored_value()
        {
            _uow.ColumnSettings.Rows.Add(new ColumnEditSetting { TableKey = ColumnEditCatalog.Product, ColumnKey = "SalePricePerUnit", IsEditable = false });

            var result = await _service.SubmitEditAsync(Form(1, f => { f.SalePricePerUnit = 1m; f.QtyFans = 9; }), "admin1", AccessLevel.Admin, "Admin");

            Assert.True(result.Success);
            Assert.Equal(85000m, _uow.Products.Items[0].SalePricePerUnit);   // locked: unchanged
            Assert.Equal(9, _uow.Products.Items[0].QtyFans);                  // not locked: changed
        }

        [Fact]
        public async Task Bulk_update_saves_nothing_when_one_row_is_invalid()
        {
            var form = new ProductBulkEditForm
            {
                Rows = { Form(1, f => f.QtyFans = 7), Form(2, f => f.QtyPanels = -3) },
                ChangeRemarks = "batch",
            };

            var result = await _service.SubmitBulkEditAsync(form, "admin1", AccessLevel.Admin, "Admin");

            Assert.False(result.Success);
            Assert.True(result.RowErrors!.ContainsKey(2));
            Assert.Equal(2, _uow.Products.Items[0].QtyFans);
            Assert.Empty(_uow.Products.Audits);
        }
    }

    public class ColumnEditServiceTests
    {
        private readonly FakeUnitOfWork _uow = new();
        private ColumnEditService NewService() => new(_uow, NullLogger<ColumnEditService>.Instance);

        [Fact]
        public async Task Display_names_are_trimmed_and_used_as_labels()
        {
            var (changed, error) = await NewService().SaveDisplayNamesAsync(ColumnEditCatalog.Product,
                new Dictionary<string, string?> { ["QtyFans"] = "  Fan   count " }, "admin1");

            Assert.Null(error);
            Assert.Equal(1, changed);
            var labels = await NewService().GetLabelsAsync(ColumnEditCatalog.Product);
            Assert.Equal("Fan count", labels["QtyFans"]);
            Assert.Equal("Qty Coils", labels["QtyCoils"]);   // built-in label when not renamed
        }

        [Fact]
        public async Task Duplicate_display_name_saves_nothing()
        {
            var (changed, error) = await NewService().SaveDisplayNamesAsync(ColumnEditCatalog.Product,
                new Dictionary<string, string?> { ["QtyFans"] = "Qty Coils" }, "admin1");

            Assert.Equal(0, changed);
            Assert.NotNull(error);
            Assert.Empty(_uow.ColumnSettings.Rows);
        }

        [Fact]
        public async Task Too_long_display_name_is_refused()
        {
            var (_, error) = await NewService().SaveDisplayNamesAsync(ColumnEditCatalog.Product,
                new Dictionary<string, string?> { ["QtyFans"] = new string('x', ColumnEditService.MaxDisplayNameLength + 1) }, "admin1");
            Assert.Contains("at most", error);
        }

        [Fact]
        public async Task Save_locks_every_column_not_listed_as_editable()
        {
            var editable = ColumnEditCatalog.Find(ColumnEditCatalog.Product)!.Columns.Select(c => c.Key).Where(k => k != "Status");

            var changed = await NewService().SaveAsync(ColumnEditCatalog.Product, editable, "admin1");

            Assert.Equal(1, changed);
            Assert.Equal(new[] { "Status" }, await NewService().GetLockedColumnsAsync(ColumnEditCatalog.Product));
        }
    }

    public class UserAccountServiceTests
    {
        private readonly FakeUnitOfWork _uow = new();

        [Fact]
        public async Task Short_password_is_refused()
        {
            var service = new UserAccountService(_uow);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("new.user", "New", AccessLevel.Viewer, "short"));
            Assert.Contains("12", ex.Message);
        }

        [Fact]
        public async Task Created_user_can_sign_in_until_disabled()
        {
            var service = new UserAccountService(_uow);
            await service.CreateAsync("New.User", "New", AccessLevel.Maker, "a-long-password-123");

            Assert.NotNull(await service.ValidateAsync("new.user", "a-long-password-123"));   // login ignores case
            Assert.Null(await service.ValidateAsync("new.user", "wrong-password-123"));

            await service.UpdateAsync("New.User", "New", AccessLevel.Maker, isActive: false, newPassword: null);
            Assert.Null(await service.ValidateAsync("new.user", "a-long-password-123"));
        }

        [Fact]
        public async Task Unknown_level_is_refused()
        {
            var service = new UserAccountService(_uow);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("x.user", null, 9, "a-long-password-123"));
        }
    }
}
