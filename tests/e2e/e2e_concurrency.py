"""Concurrent writes, end to end: change-request ids stay unique and a product never gets two open requests.

Two Makers submit changes in parallel threads:
  * to different products at once  -> every request is saved with its own id;
  * to the same product at once    -> exactly one request is accepted.
Changes product data, so run it against freshly seeded data, as tests/e2e/run_all.py --reset does.

    python tests/e2e/e2e_concurrency.py [base_url]
"""
import html, http.cookiejar, re, sys, threading, urllib.error, urllib.parse, urllib.request

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5081"
PRODUCTS = list(range(30, 42))   # products this test changes (other suites use 2 and 10-13)
SAME_PRODUCT = 45
fails = 0


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


def session():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), NoRedirect)


def req(op, path, data=None):
    body = urllib.parse.urlencode(data, doseq=True).encode() if data is not None else None
    try:
        r = op.open(BASE + path, body, timeout=60)
        return r.status, r.read().decode("utf-8"), r.headers.get("Location")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), e.headers.get("Location")


def token(page):
    return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page).group(1)


def login(user):
    op = session()
    _, page, _ = req(op, "/Account/Login")
    s, _, _ = req(op, "/Account/Login", {"login": user, "password": "Demo@123", "__RequestVerificationToken": token(page)})
    assert s == 302, f"login {user} failed"
    return op


def edit_form(op, product_id):
    """The Edit form's fields for a product, ready to post."""
    _, page, _ = req(op, f"/ProductMaster/Edit?id={product_id}")
    fields = {}
    for m in re.finditer(r'<input[^>]*name="(Form\.[^"]+)"[^>]*>', page):
        v = re.search(r'value="([^"]*)"', m.group(0))
        fields[m.group(1)] = html.unescape(v.group(1)) if v else ""
    for m in re.finditer(r'<select[^>]*name="(Form\.[^"]+)"[^>]*>(.*?)</select>', page, re.S):
        sel = re.search(r'<option[^>]*value="([^"]*)"[^>]*selected', m.group(2))
        fields[m.group(1)] = sel.group(1) if sel else ""
    fields["__RequestVerificationToken"] = token(page)
    return fields


def check(cond, msg):
    global fails
    print(("PASS " if cond else "FAIL ") + msg)
    if not cond:
        fails += 1


def submit_all(jobs):
    """Posts every (session, form) at the same moment; returns the HTTP statuses."""
    start = threading.Barrier(len(jobs))
    results = [None] * len(jobs)

    def run(i, op, form):
        start.wait()
        results[i] = req(op, "/ProductMaster/Edit", form)[0]

    threads = [threading.Thread(target=run, args=(i, op, form)) for i, (op, form) in enumerate(jobs)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    return results


makers = [login("maker1"), login("maker2")]

# --- different products at once
jobs = []
for i, pid in enumerate(PRODUCTS):
    op = makers[i % 2]
    form = edit_form(op, pid)
    form["Form.QtyPanels"] = str(7 + i)
    form["Form.ChangeRemarks"] = "concurrency test"
    jobs.append((op, form))
statuses = submit_all(jobs)
check(all(s == 302 for s in statuses), f"{len(PRODUCTS)} parallel submits for different products all succeed ({statuses})")

checker = login("checker1")
_, queue, _ = req(checker, "/ProductMaster/Approvals")
ids = re.findall(r'name="auditId" value="(\d+)"', queue)
check(len(ids) == len(PRODUCTS) and len(set(ids)) == len(ids), f"Every request got its own id ({sorted(map(int, ids))})")

# --- same product at once
jobs = []
for i, op in enumerate(makers * 2):
    form = edit_form(op, SAME_PRODUCT)
    form["Form.QtyPanels"] = str(20 + i)
    form["Form.ChangeRemarks"] = "race"
    jobs.append((op, form))
statuses = submit_all(jobs)
accepted = statuses.count(302)
check(accepted == 1, f"Only one of {len(jobs)} parallel submits for the same product is accepted ({statuses})")
_, page, _ = req(checker, f"/ProductMaster/Details?id={SAME_PRODUCT}")
check(page.count("Pending Approval</span>") >= 1 and "race" in page, "That product has one pending request")
_, queue, _ = req(checker, "/ProductMaster/Approvals")
check(len(re.findall(r'name="auditId" value="(\d+)"', queue)) == len(PRODUCTS) + 1, "Approval queue has exactly one request for it")

print(f"\n{'ALL PASSED' if fails == 0 else str(fails) + ' FAILED'}")
sys.exit(1 if fails else 0)
