using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Core.Interfaces;
using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Interfaces;

namespace Gradian.Mdm.Services.Service
{
    /// <summary>
    /// Product master browse and maker / checker workflow. The product master is a few hundred rows, so the tree and
    /// filters are built in memory per request.
    /// </summary>
    public class ProductMasterService : IProductMasterService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IColumnEditService _columnEdit;

        public ProductMasterService(IUnitOfWork unitOfWork, IColumnEditService columnEdit)
        {
            _unitOfWork = unitOfWork;
            _columnEdit = columnEdit;
        }

        private IProductMasterRepository Repo => _unitOfWork.ProductMasterRepository;

        // ================================================================ browse

        public async Task<ProductMasterBrowse> BrowseAsync(long? nodeId, string? search, string? status, string? approval = null)
        {
            var nodes = await Repo.GetHierarchyAsync();
            var products = await Repo.GetProductsAsync();
            var fields = await Repo.GetFieldsAsync();

            var (roots, byId) = BuildTree(nodes, products);
            byId.TryGetValue(nodeId ?? 0, out var selected);

            IEnumerable<ProductMasterRecord> scope = products;
            if (selected != null)
            {
                var inScope = Descendants(selected).Select(n => n.Node.NodeId).ToHashSet();
                scope = scope.Where(p => inScope.Contains(p.NodeId));
            }
            var scoped = scope.ToList();

            IEnumerable<ProductMasterRecord> filtered = scoped;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                filtered = filtered.Where(p =>
                    Contains(p.ProductName, term) || Contains(p.ProductCode, term) ||
                    Contains(p.NodePath, term) || Contains(p.CapacityGroup, term) || Contains(p.SiopModel, term));
            }
            if (!string.IsNullOrWhiteSpace(status))
                filtered = filtered.Where(p => string.Equals(p.Status, status, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(approval))
                filtered = filtered.Where(p => ApprovalKey(p) == approval);

            return new ProductMasterBrowse
            {
                Roots = roots,
                Selected = selected,
                Breadcrumb = selected == null ? Array.Empty<ProductHierarchyNode>() : Breadcrumb(selected.Node, byId),
                Products = filtered.ToList(),
                ProductsInScope = scoped.Count,
                StatusCounts = scoped.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count()),
                ApprovalCounts = scoped.GroupBy(ApprovalKey).ToDictionary(g => g.Key, g => g.Count()),
                PendingTotal = products.Count(p => p.UpdateStatus == ProductApproval.Pending),
                PendingRequests = (await Repo.GetAuditsAsync(ProductApproval.Pending))
                    .GroupBy(a => a.ProductId).ToDictionary(g => g.Key, g => g.First()),
                NodesPerLevel = nodes.GroupBy(n => n.LevelNo).ToDictionary(g => g.Key, g => g.Count()),
                FieldDescriptions = fields.ToDictionary(f => f.SourceField, f => f.Description),
                Search = search,
                Status = status,
                Approval = approval,
                NodeId = selected?.Node.NodeId,
            };
        }

        public async Task<ProductMasterDetail?> GetDetailAsync(long productId)
        {
            var product = await Repo.GetProductAsync(productId);
            if (product == null) return null;

            var siblings = (await Repo.GetProductsAsync())
                .Where(p => p.NodeId == product.NodeId && p.ProductId != product.ProductId).ToList();

            return new ProductMasterDetail
            {
                Product = product,
                Breadcrumb = await BreadcrumbAsync(product.NodeId),
                FieldDescriptions = await FieldDescriptionsAsync(),
                Siblings = siblings,
                History = await Repo.GetAuditsAsync(productId: productId),
            };
        }

        public Task<IReadOnlyList<ProductMasterField>> GetFieldsAsync() => Repo.GetFieldsAsync();

        // ================================================================ edit (maker / checker)

        public async Task<ProductEditPage?> GetEditAsync(long productId, int userLevel, ProductEditForm? posted = null)
        {
            var product = await Repo.GetProductAsync(productId);
            if (product == null) return null;

            var form = posted ?? new ProductEditForm { ProductId = product.ProductId };
            if (posted == null) form.CopyFrom(product);

            var all = await Repo.GetProductsAsync();
            static IReadOnlyList<string> Distinct(IEnumerable<string?> values) =>
                values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase)
                      .OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

            return new ProductEditPage
            {
                Product = product,
                Form = form,
                Breadcrumb = await BreadcrumbAsync(product.NodeId),
                FieldDescriptions = await FieldDescriptionsAsync(),
                CapacityGroups = Distinct(all.Select(p => p.CapacityGroup)),
                SupplySources = Distinct(all.Select(p => p.DefaultSupplySource)),
                SiopModels = Distinct(all.Select(p => p.SiopModel)),
                SiopTiers = Distinct(all.Select(p => p.SiopTier)),
                GoesToApproval = userLevel < 3,
            };
        }

        public async Task<ProductWorkflowResult> SubmitEditAsync(ProductEditForm form, string userName, int userLevel, string? roleDesc)
        {
            if (userLevel < 2) return Fail("You need the Maker role or above to change products.");

            var product = await Repo.GetProductAsync(form.ProductId);
            if (product == null) return Fail("Product not found.");
            if (product.UpdateStatus == ProductApproval.Pending)
                return Fail("This product already has a change waiting for approval. It can be edited again once that is approved or rejected.");

            var values = form.CopyValues();
            await _columnEdit.KeepLockedValuesAsync(ColumnEditCatalog.Product, values, product);

            var error = Validate(values, await LabelsAsync()) ?? (await Repo.NameExistsAsync(values.ProductName, product.ProductId)
                ? $"Another product is already named \"{values.ProductName.Trim()}\"." : null);
            if (error != null) return Fail(error);

            var changed = values.ChangedFrom(product);
            if (changed.Count == 0) return Fail("Nothing was changed.");

            var role = RoleName(userLevel, roleDesc);
            var remarks = Clean(form.ChangeRemarks);
            try
            {
                if (userLevel >= 3)
                {
                    await Repo.UpdateDirectlyAsync(product.ProductId, values, userName, role, remarks);
                    return new(true, $"Product updated ({changed.Count} field(s) changed).", product.ProductId);
                }
                await Repo.SubmitForApprovalAsync(product.ProductId, values, userName, role, remarks);
                return new(true, $"Your change to \"{product.ProductName}\" ({changed.Count} field(s)) is now pending approval. A Checker will review it.", product.ProductId);
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
        }

        public Task<IReadOnlyList<ProductMasterAudit>> GetPendingAsync() => Repo.GetAuditsAsync(ProductApproval.Pending);

        public async Task<ProductReview?> GetReviewAsync(long auditId, string userName)
        {
            var request = await Repo.GetAuditAsync(auditId);
            if (request == null) return null;
            var current = await Repo.GetProductAsync(request.ProductId);
            if (current == null) return null;

            return new ProductReview
            {
                Current = current,
                Request = request,
                Changed = request.ChangedFrom(current).ToHashSet(),
                Locked = await _columnEdit.GetLockedColumnsAsync(ColumnEditCatalog.Product),
                Labels = await LabelsAsync(),
                IsOwnRequest = string.Equals(request.MakerId, userName, StringComparison.OrdinalIgnoreCase),
            };
        }

        public async Task<ProductWorkflowResult> ApproveAsync(long auditId, string checkerId, int userLevel, string? roleDesc, string? remarks)
        {
            var (request, current, error) = await LoadForDecisionAsync(auditId, checkerId, userLevel);
            if (error != null) return Fail(error);

            // Columns locked since the request was made keep the product's current value
            var values = request!.CopyValues();
            await _columnEdit.KeepLockedValuesAsync(ColumnEditCatalog.Product, values, current!);
            if (await Repo.NameExistsAsync(values.ProductName, current!.ProductId))
                return Fail($"Can't approve: another product is now named \"{values.ProductName}\". Reject the request instead.");

            try
            {
                if (!await Repo.ApproveAsync(auditId, values, checkerId, RoleName(userLevel, roleDesc), Clean(remarks)))
                    return Fail("This request was already approved or rejected.");
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
            return new(true, $"Approved. \"{values.ProductName}\" now has the new values."
                + await _columnEdit.DescribeLockedForApprovalAsync(ColumnEditCatalog.Product), current.ProductId);
        }

        public async Task<ProductWorkflowResult> RejectAsync(long auditId, string checkerId, int userLevel, string? roleDesc, string? remarks)
        {
            if (string.IsNullOrWhiteSpace(remarks)) return Fail("Enter a comment to reject the request, so the Maker knows why.");
            var (request, current, error) = await LoadForDecisionAsync(auditId, checkerId, userLevel);
            if (error != null) return Fail(error);

            if (!await Repo.RejectAsync(auditId, checkerId, RoleName(userLevel, roleDesc), remarks.Trim()))
                return Fail("This request was already approved or rejected.");
            return new(true, $"Rejected. \"{current!.ProductName}\" keeps its current values.", current.ProductId);
        }

        private async Task<(ProductMasterAudit? Request, ProductMasterRecord? Current, string? Error)> LoadForDecisionAsync(
            long auditId, string checkerId, int userLevel)
        {
            if (userLevel < 3) return (null, null, "You need the Checker role or above to approve or reject.");
            var request = await Repo.GetAuditAsync(auditId);
            if (request == null) return (null, null, "Request not found.");
            if (request.ApprovalStatus != ProductApproval.Pending) return (null, null, "This request was already approved or rejected.");
            if (string.Equals(request.MakerId, checkerId, StringComparison.OrdinalIgnoreCase))
                return (null, null, "You can't approve or reject your own change. Another Checker must review it.");
            var current = await Repo.GetProductAsync(request.ProductId);
            return current == null ? (null, null, "Product not found.") : (request, current, null);
        }

        // ================================================================ bulk update and bulk decisions

        public async Task<ProductBulkEditPage> GetBulkEditAsync(IEnumerable<long> productIds, int userLevel, ProductBulkEditForm? posted = null,
            IReadOnlyDictionary<long, string>? rowErrors = null)
        {
            var all = await Repo.GetProductsAsync();
            var byId = all.ToDictionary(p => p.ProductId);
            var ids = (productIds ?? Enumerable.Empty<long>()).Distinct().ToList();

            var editable = new List<ProductMasterRecord>();
            var skipped = new List<string>();
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var p)) { skipped.Add($"Product #{id} was not found."); continue; }
                // A redisplayed batch keeps its rows, so the row error can explain what changed
                if (posted == null && p.UpdateStatus == ProductApproval.Pending) { skipped.Add($"\"{p.ProductName}\" has a change waiting for approval."); continue; }
                editable.Add(p);
            }
            if (editable.Count > ProductBulk.MaxRows)
            {
                skipped.Add($"Only {ProductBulk.MaxRows} products can be updated at once, so {editable.Count - ProductBulk.MaxRows} were left out.");
                editable = editable.Take(ProductBulk.MaxRows).ToList();
            }

            var form = posted ?? new ProductBulkEditForm();
            if (posted == null)
            {
                foreach (var p in editable)
                {
                    var row = new ProductEditForm { ProductId = p.ProductId };
                    row.CopyFrom(p);
                    form.Rows.Add(row);
                }
            }
            form.Rows = form.Rows.Where(r => byId.ContainsKey(r.ProductId)).ToList();

            static IReadOnlyList<string> Distinct(IEnumerable<string?> values) =>
                values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase)
                      .OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

            return new ProductBulkEditPage
            {
                Form = form,
                Products = form.Rows.Select(r => byId[r.ProductId]).ToDictionary(p => p.ProductId),
                Skipped = skipped,
                RowErrors = rowErrors ?? new Dictionary<long, string>(),
                Labels = await LabelsAsync(),
                Locked = await _columnEdit.GetLockedColumnsAsync(ColumnEditCatalog.Product),
                CapacityGroups = Distinct(all.Select(p => p.CapacityGroup)),
                SupplySources = Distinct(all.Select(p => p.DefaultSupplySource)),
                SiopModels = Distinct(all.Select(p => p.SiopModel)),
                SiopTiers = Distinct(all.Select(p => p.SiopTier)),
                GoesToApproval = userLevel < 3,
            };
        }

        public async Task<ProductBulkResult> SubmitBulkEditAsync(ProductBulkEditForm form, string userName, int userLevel, string? roleDesc)
        {
            if (userLevel < 2) return new(false, "You need the Maker role or above to change products.");
            var rows = (form.Rows ?? new()).GroupBy(r => r.ProductId).Select(g => g.Last()).ToList();
            if (rows.Count == 0) return new(false, "Select at least one product to update.");
            if (rows.Count > ProductBulk.MaxRows) return new(false, $"You can update at most {ProductBulk.MaxRows} products at once.");

            var byId = (await Repo.GetProductsAsync()).ToDictionary(p => p.ProductId);
            var labels = await LabelsAsync();
            var errors = new Dictionary<long, string>();
            var changes = new List<(ProductMasterRecord Product, ProductMasterValues Values)>();

            // Check every row first: the batch is saved only when all rows are valid
            foreach (var row in rows)
            {
                if (!byId.TryGetValue(row.ProductId, out var product)) { errors[row.ProductId] = "Product not found."; continue; }
                if (product.UpdateStatus == ProductApproval.Pending)
                {
                    errors[row.ProductId] = "This product now has a change waiting for approval. Remove it from the batch.";
                    continue;
                }

                var values = row.CopyValues();
                await _columnEdit.KeepLockedValuesAsync(ColumnEditCatalog.Product, values, product);
                var error = Validate(values, labels);
                if (error != null) { errors[row.ProductId] = error; continue; }

                if (values.ChangedFrom(product).Count > 0) changes.Add((product, values));
            }

            // Names must stay unique: within the batch, and against every other product
            foreach (var dup in changes.GroupBy(c => c.Values.ProductName.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var c in dup)
                    errors.TryAdd(c.Product.ProductId, $"{dup.Count()} products in this batch would be named \"{dup.Key}\".");
            foreach (var c in changes.Where(c => !errors.ContainsKey(c.Product.ProductId)
                         && !string.Equals(c.Values.ProductName.Trim(), c.Product.ProductName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                if (await Repo.NameExistsAsync(c.Values.ProductName, c.Product.ProductId))
                    errors[c.Product.ProductId] = $"Another product is already named \"{c.Values.ProductName.Trim()}\".";
            }

            if (errors.Count > 0)
                return new(false, $"Nothing was saved: {errors.Count} row(s) need fixing. They are highlighted below.", errors);
            if (changes.Count == 0) return new(false, "Nothing was changed.");

            var role = RoleName(userLevel, roleDesc);
            var remarks = Clean(form.ChangeRemarks);
            var direct = userLevel >= 3;
            var failed = new List<string>();
            foreach (var (product, values) in changes)
            {
                try
                {
                    if (direct) await Repo.UpdateDirectlyAsync(product.ProductId, values, userName, role, remarks);
                    else await Repo.SubmitForApprovalAsync(product.ProductId, values, userName, role, remarks);
                }
                catch (InvalidOperationException ex)
                {
                    failed.Add($"\"{product.ProductName}\": {ex.Message}");
                }
            }

            var saved = changes.Count - failed.Count;
            var unchanged = rows.Count - changes.Count;
            var message = direct
                ? $"{saved} product(s) updated."
                : $"{saved} product change(s) are now pending approval. A Checker will review them.";
            if (unchanged > 0) message += $" {unchanged} selected product(s) had no changes and were left as they are.";
            if (failed.Count > 0) message += $" {failed.Count} could not be saved: {string.Join("; ", failed)}";
            return new(saved > 0, message);
        }

        public Task<ProductBulkResult> ApproveManyAsync(IEnumerable<long> auditIds, string checkerId, int userLevel, string? roleDesc, string? remarks) =>
            DecideManyAsync(auditIds, userLevel, "approve", "approved",
                id => ApproveAsync(id, checkerId, userLevel, roleDesc, remarks));

        public Task<ProductBulkResult> RejectManyAsync(IEnumerable<long> auditIds, string checkerId, int userLevel, string? roleDesc, string? remarks)
        {
            if (string.IsNullOrWhiteSpace(remarks))
                return Task.FromResult(new ProductBulkResult(false, "Enter a comment to reject the requests, so the Makers know why."));
            return DecideManyAsync(auditIds, userLevel, "reject", "rejected",
                id => RejectAsync(id, checkerId, userLevel, roleDesc, remarks));
        }

        // Each request is decided on its own, so one that can't be decided doesn't block the others
        private async Task<ProductBulkResult> DecideManyAsync(IEnumerable<long> auditIds, int userLevel, string verb, string pastTense,
            Func<long, Task<ProductWorkflowResult>> decide)
        {
            if (userLevel < 3) return new(false, "You need the Checker role or above to approve or reject.");
            var ids = (auditIds ?? Enumerable.Empty<long>()).Distinct().ToList();
            if (ids.Count == 0) return new(false, $"Select at least one request to {verb}.");

            var done = 0;
            var failed = new List<string>();
            foreach (var id in ids)
            {
                var result = await decide(id);
                if (result.Success) done++;
                else failed.Add($"#{id}: {result.Message}");
            }

            var message = $"{done} request(s) {pastTense}.";
            if (done > 0 && verb == "approve") message += await _columnEdit.DescribeLockedForApprovalAsync(ColumnEditCatalog.Product);
            if (failed.Count > 0) message += $" {failed.Count} not {pastTense}: {string.Join(" ", failed)}";
            return new(done > 0, message);
        }

        // ================================================================ helpers

        public Task<IReadOnlyDictionary<string, string>> LabelsAsync() => _columnEdit.GetLabelsAsync(ColumnEditCatalog.Product);


        private static string? Validate(ProductMasterValues v, IReadOnlyDictionary<string, string> labels)
        {
            string L(string property) => ProductFieldLabels.Label(labels, property);

            if (string.IsNullOrWhiteSpace(v.ProductName)) return $"{L("ProductName")} is required.";
            if (v.ProductName.Trim().Length > 255) return $"{L("ProductName")} can be at most 255 characters.";
            if (!ProductMasterValues.Statuses.Contains(v.Status)) return $"{L("Status")} must be Active, Inactive or Archived.";

            var tooLong = new (string? Value, int Max, string Property)[]
            {
                (v.ProductCode, 50, "ProductCode"), (v.SiopModel, 50, "SiopModel"), (v.SiopTier, 50, "SiopTier"),
                (v.DefaultSupplySource, 20, "DefaultSupplySource"), (v.CapacityGroup, 100, "CapacityGroup"),
            }.FirstOrDefault(f => f.Value != null && f.Value.Trim().Length > f.Max);
            if (tooLong.Property != null) return $"{L(tooLong.Property)} can be at most {tooLong.Max} characters.";

            var negative = new (decimal? Value, string Property)[]
            {
                (v.SectionsPerUnit, "SectionsPerUnit"), (v.SalePricePerUnit, "SalePricePerUnit"), (v.SalePricePerHour, "SalePricePerHour"),
                (v.EstFabHours, "EstFabHours"), (v.EstAssemblyHours, "EstAssemblyHours"),
                (v.FabLbrHrsPct, "FabLbrHrsPct"), (v.AssmbLbrHrsPct, "AssmbLbrHrsPct"),
                (v.QtyFans, "QtyFans"), (v.QtyCoils, "QtyCoils"), (v.QtyPanels, "QtyPanels"),
                (v.StandardUnitMultiplier, "StandardUnitMultiplier"),
            }.FirstOrDefault(f => f.Value < 0);
            if (negative.Property != null) return $"{L(negative.Property)} can't be negative.";

            if (v.FabLbrHrsPct > 100) return $"{L("FabLbrHrsPct")} can't be more than 100.";
            if (v.AssmbLbrHrsPct > 100) return $"{L("AssmbLbrHrsPct")} can't be more than 100.";
            if (v.SalePricePerUnit >= 1_000_000_000_000m) return $"{L("SalePricePerUnit")} is too large.";
            if (v.SalePricePerHour >= 10_000_000_000m) return $"{L("SalePricePerHour")} is too large.";
            if (v.EstFabHours >= 100_000_000m) return $"{L("EstFabHours")} is too large.";
            if (v.EstAssemblyHours >= 100_000_000m) return $"{L("EstAssemblyHours")} is too large.";
            if (v.StandardUnitMultiplier >= 100_000m) return $"{L("StandardUnitMultiplier")} is too large.";
            return null;
        }

        private static string ApprovalKey(ProductMasterRecord p) =>
            string.IsNullOrEmpty(p.UpdateStatus) ? ProductApproval.Unchanged : p.UpdateStatus;

        private static string RoleName(int level, string? roleDesc) =>
            !string.IsNullOrWhiteSpace(roleDesc) ? roleDesc : level switch { >= 4 => "Admin", 3 => "Checker", 2 => "Maker", _ => "Viewer" };

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static ProductWorkflowResult Fail(string message) => new(false, message);

        private async Task<IReadOnlyDictionary<string, string?>> FieldDescriptionsAsync() =>
            (await Repo.GetFieldsAsync()).ToDictionary(f => f.SourceField, f => f.Description);

        private async Task<IReadOnlyList<ProductHierarchyNode>> BreadcrumbAsync(long nodeId)
        {
            var byId = (await Repo.GetHierarchyAsync()).ToDictionary(n => n.NodeId, n => new ProductTreeNode { Node = n });
            return byId.TryGetValue(nodeId, out var node) ? Breadcrumb(node.Node, byId) : Array.Empty<ProductHierarchyNode>();
        }

        private static (List<ProductTreeNode> Roots, Dictionary<long, ProductTreeNode> ById) BuildTree(
            IReadOnlyList<ProductHierarchyNode> nodes, IReadOnlyList<ProductMasterRecord> products)
        {
            var byId = nodes.ToDictionary(n => n.NodeId, n => new ProductTreeNode { Node = n });
            var roots = new List<ProductTreeNode>();
            foreach (var tn in byId.Values.OrderBy(t => t.Node.LevelNo).ThenBy(t => t.Node.NodeName, StringComparer.OrdinalIgnoreCase))
            {
                if (tn.Node.ParentNodeId is long parentId && byId.TryGetValue(parentId, out var parent)) parent.Children.Add(tn);
                else roots.Add(tn);
            }

            var active = products.Where(p => p.Status == "Active").GroupBy(p => p.NodeId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var root in roots) RollUp(root, active);
            return (roots, byId);
        }

        private static void RollUp(ProductTreeNode tn, IReadOnlyDictionary<long, int> activeByNode)
        {
            tn.TotalProducts = tn.Node.ProductCount;
            tn.ActiveProducts = activeByNode.GetValueOrDefault(tn.Node.NodeId);
            foreach (var child in tn.Children)
            {
                RollUp(child, activeByNode);
                tn.TotalProducts += child.TotalProducts;
                tn.ActiveProducts += child.ActiveProducts;
            }
        }

        private static IEnumerable<ProductTreeNode> Descendants(ProductTreeNode tn)
        {
            yield return tn;
            foreach (var d in tn.Children.SelectMany(Descendants)) yield return d;
        }

        private static List<ProductHierarchyNode> Breadcrumb(ProductHierarchyNode node, IReadOnlyDictionary<long, ProductTreeNode> byId)
        {
            var path = new List<ProductHierarchyNode>();
            for (ProductHierarchyNode? n = node; n != null;
                 n = n.ParentNodeId is long p && byId.TryGetValue(p, out var parent) ? parent.Node : null)
                path.Insert(0, n);
            return path;
        }

        private static bool Contains(string? value, string term) =>
            value != null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
