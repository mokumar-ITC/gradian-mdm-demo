"""Product master column display names, bulk update and bulk approve / reject, end to end, through the web app.

Signs in as the demo users (db/*/003_demo_users.sql). Admin renames columns; Maker bulk-updates several
selected products; Checker bulk-approves and bulk-rejects the resulting requests. Changes product data, so run it
against freshly seeded data (002_product_master_seed.sql), as tests/e2e/run_all.py --reset does.

    python tests/e2e/e2e_product_bulk.py [base_url]
"""
import html, http.cookiejar, re, sys, urllib.error, urllib.parse, urllib.request

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5081"
P = [10, 11, 12, 13]   # products this test changes (e2e_product_master.py uses product 2)
fails = 0


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


def session():
    jar = http.cookiejar.CookieJar()
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirect)


def req(op, path, data=None):
    body = urllib.parse.urlencode(data, doseq=True).encode() if data is not None else None
    try:
        r = op.open(BASE + path, body, timeout=30)
        return r.status, r.read().decode("utf-8"), r.headers.get("Location")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), e.headers.get("Location")


def follow(op, path, data=None):
    s, page, loc = req(op, path, data)
    while s == 302 and loc:
        s, page, loc = req(op, loc.replace(BASE, ""))
    return s, page


def token(page):
    return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page).group(1)


def login(user):
    op = session()
    _, page, _ = req(op, "/Account/Login")
    s, _, _ = req(op, "/Account/Login", {"login": user, "password": "Demo@123", "__RequestVerificationToken": token(page)})
    assert s == 302, f"login {user} failed"
    return op


def check(cond, msg):
    global fails
    print(("PASS " if cond else "FAIL ") + msg)
    if not cond:
        fails += 1


def grid_fields(page):
    """Bulk Update grid values as a list of (name, value) pairs, in page order"""
    pairs = []
    for m in re.finditer(r'<input[^>]*name="(Form\.Rows[^"]*)"[^>]*>|<select[^>]*name="(Form\.Rows[^"]*)"[^>]*>(.*?)</select>', page, re.S):
        if m.group(1):
            v = re.search(r'value="([^"]*)"', m.group(0))
            pairs.append((m.group(1), html.unescape(v.group(1)) if v else ""))
        else:
            sel = re.search(r'<option[^>]*value="([^"]*)"[^>]*selected', m.group(3))
            if 'disabled' not in m.group(0).split('>')[0]:
                pairs.append((m.group(2), sel.group(1) if sel else ""))
    return pairs


def row_index(page, pid):
    return re.search(r'name="Form\.Rows\[(\d+)\]\.ProductId" value="%d"' % pid, page).group(1)


def bulk_submit(op, ids, changes, remarks=""):
    """Open Bulk Update for ids, apply {(pid, field): value}, post. Returns (status, page/location, grid page)"""
    s, page, _ = req(op, "/ProductMaster/BulkUpdate?" + urllib.parse.urlencode([("ids", i) for i in ids]))
    if s != 200:
        return s, page, None
    pairs = dict(grid_fields(page))
    rows = [(n, v) for n, v in grid_fields(page) if n == "Form.Rows.Index"]
    for (pid, field), value in changes.items():
        pairs[f"Form.Rows[{row_index(page, pid)}].{field}"] = value
    data = [(n, v) for n, v in pairs.items() if n != "Form.Rows.Index"] + rows
    data += [("Form.ChangeRemarks", remarks), ("__RequestVerificationToken", token(page))]
    s, body, loc = req(op, "/ProductMaster/BulkUpdate", data)
    return s, body if s != 302 else loc, page


def details(op, pid):
    return req(op, f"/ProductMaster/Details?id={pid}")[1]


def pending_ids(op):
    _, page, _ = req(op, "/ProductMaster/Approvals")
    return re.findall(r'name="auditId" value="(\d+)"', page), page


# ======================================================= 1. Admin renames columns
admin = login("admin1")
s, page, _ = req(admin, "/ColumnSettings")
check(s == 200 and 'name="displayNames[ProductCode]"' in page, "Admin sees display name inputs on Column Editing")
tok = token(page)
# product columns whose Editable switch is on, so saving names keeps the locks as they are
editable = re.findall(r'id="product-[^"]+"[^>]*name="editable" value="([^"]+)"[^>]*checked', page)

s, _, loc = req(admin, "/ColumnSettings/Save", [("tableKey", "product"), ("displayNames[ProductCode]", "SKU"),
                                                ("displayNames[SalePricePerUnit]", "  List   Price "),
                                                ("__RequestVerificationToken", tok)] + [("editable", e) for e in editable])
_, page = follow(admin, "/ColumnSettings")
check("2 column name(s) updated" in page, "Admin renames two product columns")
check('value="SKU"' in page and 'value="List Price"' in page, "Display names are saved (and extra spaces trimmed)")
check("Last changed" in page and "admin1" in page, "Rename is recorded with who changed it")

lst = req(admin, "/ProductMaster")[1]
check(">SKU</th>" in lst and ">List Price</th>" in lst, "Product list uses the new column names")
check(">Code</th>" not in lst, "Product list no longer shows the old header")
check("SKU" in details(admin, P[0]), "Product details use the new column name")

s, _, _ = req(admin, "/ColumnSettings/Save", [("tableKey", "product"), ("displayNames[ProductCode]", "Product Name"),
                                              ("__RequestVerificationToken", tok)] + [("editable", e) for e in editable])
_, page = follow(admin, "/ColumnSettings")
check("would both be named" in page and 'value="SKU"' in page, "A name already used by another column is refused, nothing saved")

maker = login("maker1")
s, _, loc = req(maker, "/ColumnSettings/Save", [("tableKey", "product"), ("displayNames[ProductCode]", "X"), ("__RequestVerificationToken", tok)])
check(s == 400 or "AccessDenied" in (loc or ""), "Maker cannot rename columns")

# ======================================================= 2. access rules
viewer = login("viewer1")
s, _, loc = req(viewer, f"/ProductMaster/BulkUpdate?ids={P[0]}")
check(s == 302 and "AccessDenied" in (loc or ""), "Viewer cannot open Bulk Update")
check('id="pmBulkBtn"' not in req(viewer, "/ProductMaster")[1], "Viewer sees no Bulk update button")
check('id="pmBulkBtn"' in req(maker, "/ProductMaster")[1] and 'data-checkbox="true"' in req(maker, "/ProductMaster")[1],
      "Maker sees row checkboxes and the Bulk update button")

# ======================================================= 3. Maker bulk update
s, grid, _ = req(maker, "/ProductMaster/BulkUpdate?" + urllib.parse.urlencode([("ids", i) for i in P[:3]]))
check(s == 200 and grid.count('class="pm-bulk-row') == 3, "Bulk Update grid has one row per selected product")
check(re.search(r'pm-col-ProductCode">\s*SKU', grid) and re.search(r'pm-col-SalePricePerUnit">\s*List Price', grid), "Bulk Update grid uses the renamed column headers")

s, body, _ = bulk_submit(maker, P[:3], {(P[0], "QtyCoils"): "-2", (P[1], "QtyFans"): "7"})
check(s == 200 and "Nothing was saved" in body and "Qty Coils can&#x27;t be negative" in body, "Invalid row refuses the whole batch and names the problem")
check("Pending approval:" not in details(maker, P[1]), "Valid row in a refused batch was not saved")

s, loc, _ = bulk_submit(maker, P[:3], {(P[0], "QtyFans"): "9", (P[1], "QtyFans"): "9", (P[1], "SiopTier"): "T9"}, "Fan count correction")
check(s == 302 and "approval=PendingApproval" in (loc or ""), f"Maker submits the batch -> {s} {loc}")
_, lst = follow(maker, loc.replace(BASE, "") if loc else "/")
check("2 product change(s) are now pending approval" in lst and "1 selected product(s) had no changes" in lst,
      "Maker sees how many went to approval and how many were unchanged")
check("Pending approval:" in details(maker, P[0]) and "Pending approval:" in details(maker, P[1]), "Both changed products are pending")
check("Pending approval:" not in details(maker, P[2]), "Unchanged product is not pending")

s, grid, _ = req(maker, "/ProductMaster/BulkUpdate?" + urllib.parse.urlencode([("ids", i) for i in P[:3]]))
check(s == 200 and grid.count('class="pm-bulk-row') == 1 and "has a change waiting for approval" in grid,
      "Products waiting for approval are left out of a new batch")

maker2 = login("maker2")
s, loc, _ = bulk_submit(maker2, [P[2], P[3]], {(P[2], "QtyPanels"): "3", (P[3], "QtyPanels"): "3"}, "Panel update")
check(s == 302, "Second Maker submits another batch")

# ======================================================= 4. Checker bulk approve / reject
s, _, loc = req(maker, "/ProductMaster/ApproveSelected", {"auditIds": "1"})
check(s == 400 or "AccessDenied" in (loc or ""), "Maker cannot bulk-approve")

checker = login("checker1")
ids, page = pending_ids(checker)
check(len(ids) == 4 and 'id="pmApproveSelected"' in page, f"Checker sees 4 pending requests with bulk buttons ({ids})")
tok = token(page)
audit_for = {aid: req(checker, f"/ProductMaster/Review?id={aid}")[1] for aid in ids}
first_batch = [a for a in ids if "Fan count correction" in audit_for[a]]
second_batch = [a for a in ids if "Panel update" in audit_for[a]]
check(len(first_batch) == 2 and len(second_batch) == 2, "Requests carry the batch's reason")

s, _, _ = req(checker, "/ProductMaster/RejectSelected", [("auditIds", a) for a in second_batch] + [("remarks", ""), ("__RequestVerificationToken", tok)])
_, page = follow(checker, "/ProductMaster/Approvals")
check("Enter a comment to reject" in page and len(re.findall(r'name="auditId" value="(\d+)"', page)) == 4, "Bulk reject without a comment is refused")

s, _, loc = req(checker, "/ProductMaster/ApproveSelected", [("auditIds", a) for a in first_batch] + [("remarks", "ok"), ("__RequestVerificationToken", tok)])
_, page = follow(checker, "/ProductMaster/Approvals")
check("2 request(s) approved." in page, "Checker bulk-approves two requests")
p0, p1 = details(checker, P[0]), details(checker, P[1])
check(">Approved</span>" in p0 and ">Approved</span>" in p1 and "T9" in p1, "Approved values are applied to both products")

s, _, _ = req(checker, "/ProductMaster/RejectSelected", [("auditIds", a) for a in second_batch] + [("remarks", "Panels unchanged"), ("__RequestVerificationToken", tok)])
_, page = follow(checker, "/ProductMaster/Approvals")
check("2 request(s) rejected." in page and "No product changes are waiting" in page, "Checker bulk-rejects the other two; queue is empty")
check(">Rejected</span>" in details(checker, P[3]) and "Panels unchanged" in details(checker, P[3]), "Rejected product shows the reason")

s, _, _ = req(checker, "/ProductMaster/ApproveSelected", [("auditIds", first_batch[0]), ("remarks", ""), ("__RequestVerificationToken", tok)])
_, page = follow(checker, "/ProductMaster/Approvals")
check("0 request(s) approved." in page and "already approved or rejected" in page, "Approving an already decided request is reported, not repeated")

# ======================================================= 5. Checker bulk update is direct; own requests
s, loc, _ = bulk_submit(checker, [P[2], P[3]], {(P[2], "SiopTier"): "T8", (P[3], "SiopTier"): "T8"})
_, lst = follow(checker, loc.replace(BASE, "") if loc else "/")
check(s == 302 and "2 product(s) updated." in lst, "Checker bulk update is applied directly")
check("Updated Directly" in details(checker, P[2]), "Direct bulk update is marked Updated Directly")

s, body, _ = bulk_submit(admin, [P[0], P[1]], {(P[0], "ProductName"): "Bulk dup", (P[1], "ProductName"): "bulk DUP"})
check(s == 200 and "would be named" in body, "Two rows given the same name are refused")

# ======================================================= 6. Checker approves pending rows straight from the product list
s, loc, _ = bulk_submit(maker, [P[0], P[1]], {(P[0], "QtyFans"): "5", (P[1], "QtyFans"): "5"}, "From the list")
check(s == 302, "Maker submits a batch to approve from the list")
lst = req(checker, "/ProductMaster?approval=PendingApproval")[1]
check('id="pmApproveSelected"' in lst and 'data-field="audit"' in lst, "Checker's product list has Approve / Reject selected")
list_ids = re.findall(r'<td>(\d+)</td>\s*<td></td>\s*<td><a href="/ProductMaster/Details', lst)
check(len(list_ids) == 2, f"Pending rows carry their request id for the Checker ({list_ids})")
check('id="pmApproveSelected"' not in req(maker, "/ProductMaster")[1], "Maker's product list has no approve buttons")
own = re.findall(r'<td>(\d+)</td>\s*<td>1</td>\s*<td><a href="/ProductMaster/Details', req(maker, "/ProductMaster?approval=PendingApproval")[1])
check(len(own) == 2, "The requester's own pending rows are marked as theirs (so they can't be ticked)")
s, _, loc = req(checker, "/ProductMaster/ApproveSelected", [("auditIds", a) for a in list_ids] +
                [("remarks", ""), ("returnUrl", "/ProductMaster?approval=PendingApproval"), ("__RequestVerificationToken", token(lst))])
check(s == 302 and "approval=PendingApproval" in (loc or ""), f"Approving from the list goes back to the list -> {loc}")
_, lst = follow(checker, loc.replace(BASE, "") if loc else "/")
check("2 request(s) approved." in lst, "Checker approves two pending rows from the product list")
s, _, loc = req(checker, "/ProductMaster/ApproveSelected", [("auditIds", list_ids[0]), ("returnUrl", "https://example.com/"),
                                                            ("__RequestVerificationToken", token(lst))])
check(s == 302 and "example.com" not in (loc or ""), "An outside return address is ignored")

# ======================================================= 7. reset the column names
s, _, _ = req(admin, "/ColumnSettings/Save", [("tableKey", "product"), ("displayNames[ProductCode]", ""), ("displayNames[SalePricePerUnit]", ""),
                                              ("__RequestVerificationToken", token(req(admin, "/ColumnSettings")[1]))] + [("editable", e) for e in editable])
lst = req(admin, "/ProductMaster")[1]
check(">Product Code</th>" in lst and ">SKU</th>" not in lst, "Clearing a display name goes back to the built-in name")

print(f"\n{'ALL PASSED' if fails == 0 else str(fails) + ' FAILED'}")
sys.exit(1 if fails else 0)
