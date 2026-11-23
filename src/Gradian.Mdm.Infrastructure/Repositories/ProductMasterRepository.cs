using System.Data;
using System.Data.Common;
using Gradian.Mdm.Core.Entities;
using Gradian.Mdm.Core.Interfaces;
using Gradian.Mdm.Infrastructure.Data;
using static Gradian.Mdm.Infrastructure.Data.DbSession;

namespace Gradian.Mdm.Infrastructure.Repositories
{
    /// <summary>
    /// Product master tables (see <c>db/postgres/001_schema.sql</c> and <c>db/fabric/001_schema.sql</c>).
    /// The SQL is shared by PostgreSQL and Fabric Warehouse. Every write runs in one transaction that first updates the
    /// product row: that serialises concurrent writers on both engines (a row lock in PostgreSQL, a write-write conflict
    /// under Fabric's snapshot isolation), so the checks that follow it inside the transaction are reliable.
    /// </summary>
    public sealed class ProductMasterRepository : IProductMasterRepository
    {
        private const string Products = "mdm.product_master";
        private const string Audits = "mdm.product_master_audit";
        private const string Hierarchy = "mdm.product_hierarchy";

        private const string NameInUse = "Another product already uses this Product Name.";
        private const string AlreadyPending = "This product already has a change waiting for approval.";

        /// <summary>Editable columns: (column, property, type). The same order is used everywhere they are written or read.</summary>
        private static readonly (string Column, string Property, DbType Type)[] ValueColumns =
        {
            ("product_name", nameof(ProductMasterValues.ProductName), DbType.String),
            ("product_code", nameof(ProductMasterValues.ProductCode), DbType.String),
            ("siop_model", nameof(ProductMasterValues.SiopModel), DbType.String),
            ("siop_tier", nameof(ProductMasterValues.SiopTier), DbType.String),
            ("sections_per_unit", nameof(ProductMasterValues.SectionsPerUnit), DbType.Int32),
            ("sale_price_per_unit", nameof(ProductMasterValues.SalePricePerUnit), DbType.Decimal),
            ("sale_price_per_hour", nameof(ProductMasterValues.SalePricePerHour), DbType.Decimal),
            ("default_supply_source", nameof(ProductMasterValues.DefaultSupplySource), DbType.String),
            ("est_fab_hours", nameof(ProductMasterValues.EstFabHours), DbType.Decimal),
            ("est_assembly_hours", nameof(ProductMasterValues.EstAssemblyHours), DbType.Decimal),
            ("fab_lbr_hrs_pct", nameof(ProductMasterValues.FabLbrHrsPct), DbType.Decimal),
            ("assmb_lbr_hrs_pct", nameof(ProductMasterValues.AssmbLbrHrsPct), DbType.Decimal),
            ("qty_fans", nameof(ProductMasterValues.QtyFans), DbType.Int32),
            ("qty_coils", nameof(ProductMasterValues.QtyCoils), DbType.Int32),
            ("qty_panels", nameof(ProductMasterValues.QtyPanels), DbType.Int32),
            ("standard_unit_multiplier", nameof(ProductMasterValues.StandardUnitMultiplier), DbType.Decimal),
            ("status", nameof(ProductMasterValues.Status), DbType.String),
            ("capacity_group", nameof(ProductMasterValues.CapacityGroup), DbType.String),
        };

        private static readonly string ValueColumnList = string.Join(", ", ValueColumns.Select(c => c.Column));
        private static readonly string ValuePlaceholders = string.Join(", ", ValueColumns.Select(c => "@v_" + c.Column));
        private static readonly string ValueAssignments = string.Join(", ", ValueColumns.Select(c => $"{c.Column} = @v_{c.Column}"));

        private const string ProductColumns =
            "product_id, node_id, node_path, product_family, product_type, model, size, product_name, product_code, " +
            "siop_model, siop_tier, sections_per_unit, sale_price_per_unit, sale_price_per_hour, default_supply_source, " +
            "est_fab_hours, est_assembly_hours, fab_lbr_hrs_pct, assmb_lbr_hrs_pct, qty_fans, qty_coils, qty_panels, " +
            "standard_unit_multiplier, status, capacity_group, source_row, createdby, createddate, " +
            "updatedby, updateddate, updatestatus, lastmodifiedbyrole, remarks";

        private readonly DbSession _db;

        public ProductMasterRepository(DbSession db) => _db = db;

        private string True => _db.Dialect.True;

        // ================================================================ reads

        public Task<IReadOnlyList<ProductHierarchyNode>> GetHierarchyAsync() =>
            _db.QueryAsync(
                "SELECT h.node_id, h.parent_node_id, h.level_no, l.level_name, h.node_name, h.node_path, " +
                $"       (SELECT COUNT(*) FROM {Products} p WHERE p.node_id = h.node_id AND p.isactive = {True}) " +
                $"  FROM {Hierarchy} h JOIN mdm.product_hierarchy_level l ON l.level_no = h.level_no " +
                $" WHERE h.isactive = {True} ORDER BY h.level_no, lower(h.node_name)",
                r => new ProductHierarchyNode
                {
                    NodeId = Convert.ToInt64(r.GetValue(0)),
                    ParentNodeId = r.IsDBNull(1) ? null : Convert.ToInt64(r.GetValue(1)),
                    LevelNo = Convert.ToInt32(r.GetValue(2)),
                    LevelName = r.GetString(3),
                    NodeName = r.GetString(4),
                    NodePath = r.GetString(5),
                    ProductCount = Convert.ToInt32(r.GetValue(6)),
                });

        public Task<IReadOnlyList<ProductMasterRecord>> GetProductsAsync() =>
            _db.QueryAsync($"SELECT {ProductColumns} FROM mdm.vw_product_master WHERE isactive = {True} ORDER BY lower(product_name)", MapProduct);

        public async Task<ProductMasterRecord?> GetProductAsync(long productId) =>
            (await _db.QueryAsync($"SELECT {ProductColumns} FROM mdm.vw_product_master WHERE product_id = @id", MapProduct, P("id", productId)))
                .FirstOrDefault();

        public Task<IReadOnlyList<ProductMasterField>> GetFieldsAsync() =>
            _db.QueryAsync("SELECT field_order, source_field, column_name, description FROM mdm.product_master_field ORDER BY field_order",
                r => new ProductMasterField
                {
                    FieldOrder = Convert.ToInt32(r.GetValue(0)),
                    SourceField = r.GetString(1),
                    ColumnName = r.GetString(2),
                    Description = Str(r, 3),
                });

        public async Task<bool> NameExistsAsync(string productName, long productId) =>
            Convert.ToInt64(await _db.ScalarAsync(
                $"SELECT COUNT(*) FROM {Products} WHERE lower(product_name) = lower(@name) AND product_id <> @id",
                P("name", productName.Trim(), DbType.String), P("id", productId))) > 0;

        public Task<IReadOnlyList<ProductMasterAudit>> GetAuditsAsync(string? approvalStatus = null, long? productId = null)
        {
            var where = new List<string>();
            var args = new List<SqlParam>();
            if (approvalStatus != null) { where.Add("a.approvalstatus = @status"); args.Add(P("status", approvalStatus)); }
            if (productId != null) { where.Add("a.product_id = @id"); args.Add(P("id", productId.Value)); }
            var filter = where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where);
            return _db.QueryAsync(AuditSelect + filter + " ORDER BY a.changetimestamp DESC, a.auditid DESC", MapAudit, args.ToArray());
        }

        public async Task<ProductMasterAudit?> GetAuditAsync(long auditId) =>
            (await _db.QueryAsync(AuditSelect + " WHERE a.auditid = @id", MapAudit, P("id", auditId))).FirstOrDefault();

        private static readonly string AuditSelect =
            $"SELECT a.auditid, a.product_id, h.node_path, {string.Join(", ", ValueColumns.Select(c => "a." + c.Column))}, " +
            "       a.changetype, a.approvalstatus, a.makerid, a.checkerid, a.changetimestamp, a.approvaltimestamp, " +
            "       a.lastmodifiedbyrole, a.remarks, a.checker_remarks " +
            $"  FROM {Audits} a " +
            $"  JOIN {Products} p ON p.product_id = a.product_id " +
            $"  JOIN {Hierarchy} h ON h.node_id = p.node_id";

        // ================================================================ writes

        public Task UpdateDirectlyAsync(long productId, ProductMasterValues values, string userName, string role, string? remarks) =>
            WriteAsync(NameInUse, async now =>
            {
                var updated = await _db.ExecuteAsync(
                    $"UPDATE {Products} SET {ValueAssignments}, updatestatus = 'UpdateDirectly', lastmodifiedbyrole = @role, " +
                    "       updatedby = @user, updateddate = @now, remarks = @remarks WHERE product_id = @id",
                    ValueParams(values, P("role", role), P("user", userName), P("now", now), P("remarks", remarks, DbType.String), P("id", productId)));
                if (updated == 0) throw new InvalidOperationException("Product not found.");

                await EnsureNameUniqueAsync(productId, values.ProductName);
                return await InsertAuditAsync(productId, values, "UpdateDirectly", userName, userName, now, now, role, remarks);
            });

        public Task<long> SubmitForApprovalAsync(long productId, ProductMasterValues values, string makerId, string role, string? remarks) =>
            WriteAsync(AlreadyPending, async now =>
            {
                var updated = await _db.ExecuteAsync(
                    $"UPDATE {Products} SET updatestatus = 'PendingApproval', lastmodifiedbyrole = @role, " +
                    "       updatedby = @user, updateddate = @now, remarks = @remarks WHERE product_id = @id",
                    P("role", role), P("user", makerId), P("now", now), P("remarks", remarks, DbType.String), P("id", productId));
                if (updated == 0) throw new InvalidOperationException("Product not found.");

                // At most one open request per product (PostgreSQL also enforces this with a partial unique index)
                var pending = Convert.ToInt64(await _db.ScalarAsync(
                    $"SELECT COUNT(*) FROM {Audits} WHERE product_id = @id AND approvalstatus = 'PendingApproval'", P("id", productId)));
                if (pending > 0) throw new InvalidOperationException(AlreadyPending);

                return await InsertAuditAsync(productId, values, "PendingApproval", makerId, null, now, null, role, remarks);
            });

        public Task<bool> ApproveAsync(long auditId, ProductMasterValues values, string checkerId, string role, string? remarks) =>
            WriteAsync(NameInUse, async now =>
            {
                var productId = await DecideAsync(auditId, "Approved", checkerId, remarks, now);
                if (productId == null) return false;

                await _db.ExecuteAsync(
                    $"UPDATE {Products} SET {ValueAssignments}, updatestatus = 'Approved', lastmodifiedbyrole = @role, " +
                    "       updatedby = @user, updateddate = @now, remarks = @remarks WHERE product_id = @id",
                    ValueParams(values, P("role", role), P("user", checkerId), P("now", now), P("remarks", remarks, DbType.String), P("id", productId.Value)));
                await EnsureNameUniqueAsync(productId.Value, values.ProductName);
                return true;
            });

        public Task<bool> RejectAsync(long auditId, string checkerId, string role, string remarks) =>
            WriteAsync(NameInUse, async now =>
            {
                var productId = await DecideAsync(auditId, "Rejected", checkerId, remarks, now);
                if (productId == null) return false;

                await _db.ExecuteAsync(
                    $"UPDATE {Products} SET updatestatus = 'Rejected', lastmodifiedbyrole = @role, " +
                    "       updatedby = @user, updateddate = @now, remarks = @remarks WHERE product_id = @id",
                    P("role", role), P("user", checkerId), P("now", now), P("remarks", remarks, DbType.String), P("id", productId.Value));
                return true;
            });

        // ================================================================ write helpers

        /// <summary>
        /// Runs a write in a transaction with one timestamp (UTC) for every row it touches, and turns a unique-constraint
        /// violation into an <see cref="InvalidOperationException"/> carrying <paramref name="uniqueViolationMessage"/>.
        /// </summary>
        private async Task<T> WriteAsync<T>(string uniqueViolationMessage, Func<DateTime, Task<T>> write)
        {
            try
            {
                var now = DateTime.UtcNow;
                return await _db.InTransactionAsync(() => write(now));
            }
            catch (DbException ex) when (_db.Dialect.IsUniqueViolation(ex))
            {
                throw new InvalidOperationException(uniqueViolationMessage, ex);
            }
        }

        /// <summary>Marks a pending request as decided. Returns its product id, or null when it was no longer pending.</summary>
        private async Task<long?> DecideAsync(long auditId, string decision, string checkerId, string? remarks, DateTime now)
        {
            var updated = await _db.ExecuteAsync(
                $"UPDATE {Audits} SET approvalstatus = @decision, checkerid = @checker, approvaltimestamp = @now, checker_remarks = @remarks " +
                " WHERE auditid = @id AND approvalstatus = 'PendingApproval'",
                P("decision", decision), P("checker", checkerId), P("now", now), P("remarks", remarks, DbType.String), P("id", auditId));
            if (updated == 0) return null;

            return Convert.ToInt64(await _db.ScalarAsync($"SELECT product_id FROM {Audits} WHERE auditid = @id", P("id", auditId)));
        }

        /// <summary>
        /// Product names are unique ignoring case. PostgreSQL enforces it with an index; Fabric can't, so the check runs
        /// here too, after the product row was updated in the same transaction.
        /// </summary>
        private async Task EnsureNameUniqueAsync(long productId, string productName)
        {
            if (await NameExistsAsync(productName, productId)) throw new InvalidOperationException(NameInUse);
        }

        /// <summary>
        /// Next key for engines without identity columns (Fabric Warehouse), from <c>mdm.key_counter</c>. Every caller
        /// updates the same counter row, so two concurrent transactions can never get the same key: the second one waits
        /// (SQL Server) or is rolled back with a write conflict (Fabric snapshot isolation). Must run inside the write transaction.
        /// </summary>
        private async Task<long> NextKeyAsync(string counterName)
        {
            var updated = await _db.ExecuteAsync(
                "UPDATE mdm.key_counter SET last_value = last_value + 1 WHERE counter_name = @name", P("name", counterName));
            if (updated == 0)
                throw new InvalidOperationException($"mdm.key_counter has no \"{counterName}\" row. Run db/fabric/001_schema.sql.");
            return Convert.ToInt64(await _db.ScalarAsync("SELECT last_value FROM mdm.key_counter WHERE counter_name = @name", P("name", counterName)));
        }

        /// <summary>Writes one audit row and returns its id (engine-generated, or from <see cref="NextKeyAsync"/>).</summary>
        private async Task<long> InsertAuditAsync(long productId, ProductMasterValues values, string approvalStatus,
            string makerId, string? checkerId, DateTime changedAt, DateTime? approvedAt, string role, string? remarks)
        {
            var args = ValueParams(values,
                P("product", productId), P("status", approvalStatus), P("maker", makerId), P("checker", checkerId, DbType.String),
                P("changed", changedAt), P("approved", approvedAt, DbType.DateTime2), P("role", role), P("remarks", remarks, DbType.String));
            const string columns = "product_id, {0}, changetype, approvalstatus, makerid, checkerid, changetimestamp, approvaltimestamp, lastmodifiedbyrole, remarks";
            const string placeholders = "@product, {0}, 'Update', @status, @maker, @checker, @changed, @approved, @role, @remarks";

            if (_db.Dialect.GeneratesKeys)
            {
                return Convert.ToInt64(await _db.ScalarAsync(
                    $"INSERT INTO {Audits} ({string.Format(columns, ValueColumnList)}) " +
                    $"VALUES ({string.Format(placeholders, ValuePlaceholders)}) RETURNING auditid", args));
            }

            var auditId = await NextKeyAsync("product_master_audit");
            await _db.ExecuteAsync(
                $"INSERT INTO {Audits} (auditid, {string.Format(columns, ValueColumnList)}) " +
                $"VALUES (@auditid, {string.Format(placeholders, ValuePlaceholders)})",
                args.Append(P("auditid", auditId)).ToArray());
            return auditId;
        }

        /// <summary>The <c>@v_*</c> parameters for the editable values (strings trimmed, blanks stored as null), plus any extra parameters.</summary>
        private static SqlParam[] ValueParams(ProductMasterValues values, params SqlParam[] extra) =>
            ValueColumns.Select(c =>
            {
                var v = typeof(ProductMasterValues).GetProperty(c.Property)!.GetValue(values);
                if (v is string s) v = string.IsNullOrWhiteSpace(s) ? null : s.Trim();
                return P("v_" + c.Column, v, c.Type);
            }).Concat(extra).ToArray();

        // ================================================================ mapping

        private static ProductMasterRecord MapProduct(DbDataReader r) => new()
        {
            ProductId = Convert.ToInt64(r.GetValue(0)),
            NodeId = Convert.ToInt64(r.GetValue(1)),
            NodePath = r.GetString(2),
            ProductFamily = r.GetString(3),
            ProductType = Str(r, 4) ?? "",
            Model = Str(r, 5),
            Size = Str(r, 6),
            ProductName = r.GetString(7),
            ProductCode = Str(r, 8),
            SiopModel = Str(r, 9),
            SiopTier = Str(r, 10),
            SectionsPerUnit = Int(r, 11),
            SalePricePerUnit = Dec(r, 12),
            SalePricePerHour = Dec(r, 13),
            DefaultSupplySource = Str(r, 14),
            EstFabHours = Dec(r, 15),
            EstAssemblyHours = Dec(r, 16),
            FabLbrHrsPct = Dec(r, 17),
            AssmbLbrHrsPct = Dec(r, 18),
            QtyFans = Int(r, 19),
            QtyCoils = Int(r, 20),
            QtyPanels = Int(r, 21),
            StandardUnitMultiplier = Dec(r, 22),
            Status = r.GetString(23),
            CapacityGroup = Str(r, 24),
            SourceRow = Int(r, 25),
            CreatedBy = Str(r, 26),
            CreatedDate = Date(r, 27),
            UpdatedBy = Str(r, 28),
            UpdatedDate = Date(r, 29),
            UpdateStatus = Str(r, 30),
            LastModifiedByRole = Str(r, 31),
            Remarks = Str(r, 32),
        };

        private static ProductMasterAudit MapAudit(DbDataReader r)
        {
            var a = new ProductMasterAudit
            {
                AuditId = Convert.ToInt64(r.GetValue(0)),
                ProductId = Convert.ToInt64(r.GetValue(1)),
                NodePath = r.GetString(2),
            };
            var i = 3;
            foreach (var c in ValueColumns)
            {
                object? v = c.Type switch
                {
                    DbType.Int32 => Int(r, i),
                    DbType.Decimal => Dec(r, i),
                    _ => Str(r, i),
                };
                // ProductName and Status are non-nullable strings
                typeof(ProductMasterValues).GetProperty(c.Property)!.SetValue(a, v ?? (c.Column is "product_name" or "status" ? "" : null));
                i++;
            }
            a.ChangeType = Str(r, i++) ?? "Update";
            a.ApprovalStatus = Str(r, i++) ?? "";
            a.MakerId = Str(r, i++);
            a.CheckerId = Str(r, i++);
            a.ChangeTimestamp = Date(r, i++) ?? DateTime.MinValue;
            a.ApprovalTimestamp = Date(r, i++);
            a.LastModifiedByRole = Str(r, i++);
            a.Remarks = Str(r, i++);
            a.CheckerRemarks = Str(r, i);
            return a;
        }

        private static string? Str(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i).ToString();
        private static int? Int(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
        private static decimal? Dec(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
        private static DateTime? Date(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    }
}
