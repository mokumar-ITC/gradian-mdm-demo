"""Product master maker / checker workflow, end to end, through the web app.

Signs in as the demo users (db/*/003_demo_users.sql) and walks one product through:
Maker submits -> Checker approves -> Maker submits -> Checker rejects -> Admin edits directly,
plus the access and validation rules. Changes product data, so run it against freshly seeded data
(002_product_master_seed.sql), as tests/e2e/run_all.py --reset does.

    python tests/e2e/e2e_product_master.py [base_url] [product_id]
"""
import html, http.cookiejar, re, sys, urllib.error, urllib.parse, urllib.request

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5081"
PID = int(sys.argv[2]) if len(sys.argv) > 2 else 2
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


def token(page):
    return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page).group(1)


def login(user):
    op = session()
    _, page, _ = req(op, "/Account/Login")
    s, _, loc = req(op, "/Account/Login", {"login": user, "password": "Demo@123", "__RequestVerificationToken": token(page)})
    assert s == 302, f"login {user} failed"
    return op


def check(cond, msg):
    global fails
    print(("PASS " if cond else "FAIL ") + msg)
    if not cond:
        fails += 1


def form_fields(page):
    """Current Edit form values: Form.X -> value"""
    fields = {}
    for m in re.finditer(r'<input[^>]*name="(Form\.[^"]+)"[^>]*>', page):
        tag = m.group(0)
        v = re.search(r'value="([^"]*)"', tag)
        fields[m.group(1)] = html.unescape(v.group(1)) if v else ""
    for m in re.finditer(r'<select[^>]*name="(Form\.[^"]+)"[^>]*>(.*?)</select>', page, re.S):
        sel = re.search(r'<option[^>]*value="([^"]*)"[^>]*selected', m.group(2))
        fields[m.group(1)] = sel.group(1) if sel else ""
    fields["Form.ChangeRemarks"] = ""
    return fields


def submit_edit(op, changes, remarks=""):
    s, page, _ = req(op, f"/ProductMaster/Edit?id={PID}")
    if s != 200:
        return s, page, None
    data = form_fields(page)
    data.update(changes)
    data["Form.ChangeRemarks"] = remarks
    data["__RequestVerificationToken"] = token(page)
    return req(op, "/ProductMaster/Edit", data)


def details(op):
    return req(op, f"/ProductMaster/Details?id={PID}")[1]


def pending_audit_id(op):
    _, page, _ = req(op, "/ProductMaster/Approvals")
    ids = re.findall(r'name="auditId" value="(\d+)"', page)
    return ids, page


# --- viewer can't edit
viewer = login("viewer1")
s, _, loc = req(viewer, f"/ProductMaster/Edit?id={PID}")
check(s == 302 and "AccessDenied" in (loc or ""), "Viewer cannot open Edit")
check("bi-pencil-square" not in req(viewer, "/ProductMaster")[1], "Viewer sees no Edit buttons in the list")

# --- maker submits -> pending
maker = login("maker1")
s, _, loc = submit_edit(maker, {"Form.SalePricePerUnit": "91000", "Form.QtyFans": "4"}, "Price update from Q4 list")
check(s == 302 and "Details" in (loc or ""), f"Maker submits change -> {s} {loc}")
page = details(maker)
check("Pending approval:" in page and "pending approval" in page.lower(), "Product shows Pending Approval banner")
check("85,000" in page, "Product still has old price until approved")
s, _, loc = req(maker, f"/ProductMaster/Edit?id={PID}")
check(s == 302, "Maker cannot edit again while pending (redirected)")
lst = req(maker, "/ProductMaster?approval=PendingApproval")[1]
check("Pending Approval</span>" in lst, "List filtered to Pending Approval shows the product with a status badge")
s, _, loc = req(maker, "/ProductMaster/Approvals")
check(s == 302 and "AccessDenied" in (loc or ""), "Maker cannot open the approval queue")

# --- checker approves
checker = login("checker1")
ids, page = pending_audit_id(checker)
check(len(ids) >= 1, f"Checker sees request in Product Approval ({ids})")
aid = ids[0]
_, review, _ = req(checker, f"/ProductMaster/Review?id={aid}")
check(review.count('badge bg-primary ms-1">changed') == 2, "Review highlights exactly 2 changed fields")
check("Price update from Q4 list" in review, "Review shows the Maker's reason")
s, _, loc = req(checker, "/ProductMaster/Approve", {"auditId": aid, "remarks": "ok", "__RequestVerificationToken": token(review)})
check(s == 302 and "Approvals" in (loc or ""), f"Checker approves -> {s} {loc}")
page = details(checker)
check("91,000" in page and ">Approved</span>" in page, "Product now has new price and Approved status")
s, _, loc = req(checker, "/ProductMaster/Approve", {"auditId": aid, "remarks": "again", "__RequestVerificationToken": token(review)})
_, q, _ = req(checker, "/ProductMaster/Approvals")
check("already approved or rejected" in q or "already approved or rejected" in req(checker, loc.replace(BASE, "") if loc else "/")[1],
      "Approving the same request twice is refused")

# --- maker submits again, checker rejects (comment required)
s, _, _ = submit_edit(maker, {"Form.Status": "Archived"}, "Retire this model")
check(s == 302, "Maker submits second change")
ids, page = pending_audit_id(checker)
aid2 = ids[0]
_, review, _ = req(checker, f"/ProductMaster/Review?id={aid2}")
s, _, loc = req(checker, "/ProductMaster/Reject", {"auditId": aid2, "remarks": "", "__RequestVerificationToken": token(review)})
_, after, _ = req(checker, f"/ProductMaster/Review?id={aid2}")
check("Enter a comment to reject" in after, "Reject without a comment is refused")
s, _, loc = req(checker, "/ProductMaster/Reject", {"auditId": aid2, "remarks": "Still sold in APAC", "__RequestVerificationToken": token(review)})
check(s == 302, "Checker rejects with a comment")
page = details(maker)
check(">Rejected</span>" in page and "Still sold in APAC" in page, "Maker sees Rejected status and the reason")
check(re.search(r'badge bg-success">Active', page) is not None, "Rejected change did not apply (still Active)")

# --- admin edits directly
admin = login("admin1")
s, _, loc = submit_edit(admin, {"Form.CapacityGroup": "AHU-X"}, "")
page = details(admin)
check(s == 302 and "AHU-X" in page and "Updated Directly" in page, "Admin change is applied directly (Updated Directly)")

# --- validation
s, body, _ = submit_edit(admin, {"Form.ProductName": "Air Cooling – AHU – Indoor – Low Complexity"})
check(s == 200 and "already named" in body, "Duplicate product name is refused")
s, body, _ = submit_edit(admin, {"Form.QtyCoils": "-1"})
check(s == 200 and "can&#x27;t be negative" in body, "Negative quantity is refused")
s, body, _ = submit_edit(admin, {})
check(s == 200 and "Nothing was changed" in body, "Saving with no changes is refused")

# --- history
page = details(admin)
check(page.count('rounded-pill bg-') >= 3 and "Change history" in page, "Change history lists the requests")
check("maker1 <span class=\"text-muted\">(Maker)</span>" in page, "History keeps the Maker's role on decided requests")

print(f"\n{'ALL PASSED' if fails == 0 else str(fails) + ' FAILED'}")
sys.exit(1 if fails else 0)
