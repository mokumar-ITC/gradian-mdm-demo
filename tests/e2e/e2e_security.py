"""Security and access-control checks, end to end, through the web app.

Covers: health endpoint, sign-in required, security headers, antiforgery, uniform sign-in errors, open-redirect
protection, role boundaries, password policy, disabling an account, and the sign-in rate limit.
Run it LAST: the rate-limit check uses up the sign-in allowance of this client address for a minute.

    python tests/e2e/e2e_security.py [base_url] [login_attempts_per_minute]
"""
import http.cookiejar, re, sys, urllib.error, urllib.parse, urllib.request, uuid

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5081"
LIMIT = int(sys.argv[2]) if len(sys.argv) > 2 else 10
fails = 0


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


def session():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), NoRedirect)


def req(op, path, data=None):
    body = urllib.parse.urlencode(data, doseq=True).encode() if data is not None else None
    try:
        r = op.open(BASE + path, body, timeout=30)
        return r.status, r.read().decode("utf-8"), r.headers
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), e.headers


def token(page):
    return re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page).group(1)


def try_login(user, password, return_url=None):
    op = session()
    _, page, _ = req(op, "/Account/Login")
    data = {"login": user, "password": password, "__RequestVerificationToken": token(page)}
    if return_url:
        data["returnUrl"] = return_url
    s, body, headers = req(op, "/Account/Login", data)
    return op, s, body, headers


def login(user, password="Demo@123"):
    op, s, _, _ = try_login(user, password)
    assert s == 302, f"login {user} failed ({s})"
    return op


def check(cond, msg):
    global fails
    print(("PASS " if cond else "FAIL ") + msg)
    if not cond:
        fails += 1


# --- anonymous
anon = session()
s, body, _ = req(anon, "/health")
check(s == 200 and "Healthy" in body, f"/health reports Healthy ({s} {body[:40]})")
s, _, h = req(anon, "/ProductMaster")
check(s == 302 and "/Account/Login" in (h.get("Location") or ""), "Anonymous user is sent to the sign-in page")
s, _, h = req(anon, "/Users")
check(s == 302 and "/Account/Login" in (h.get("Location") or ""), "Anonymous user can't open user admin")

s, page, h = req(anon, "/Account/Login")
csp = h.get("Content-Security-Policy") or ""
check("frame-ancestors 'none'" in csp and "object-src 'none'" in csp, "Content-Security-Policy is sent")
check(h.get("X-Content-Type-Options") == "nosniff" and h.get("X-Frame-Options") == "DENY", "nosniff and X-Frame-Options are sent")
check("cdn.jsdelivr" not in page and "raw.github" not in page, "Sign-in page loads no third-party scripts")

# --- antiforgery and sign-in errors
s, _, _ = req(session(), "/Account/Login", {"login": "admin1", "password": "Demo@123"})
check(s == 400, f"Sign-in without an antiforgery token is rejected ({s})")
_, s1, wrong_pw, _ = try_login("admin1", "wrong-password")
_, s2, no_user, _ = try_login("no-such-user-" + uuid.uuid4().hex[:6], "wrong-password")
check(s1 == 200 and s2 == 200 and "Invalid user name or password" in wrong_pw and "Invalid user name or password" in no_user,
      "Wrong password and unknown user get the same message")

_, s, _, h = try_login("viewer1", "Demo@123", "https://evil.example/phish")
check(s == 302 and not (h.get("Location") or "").startswith("http"), f"External returnUrl is ignored ({h.get('Location')})")

# --- role boundaries
viewer = login("viewer1")
s, _, h = req(viewer, "/Users")
check(s == 302 and "AccessDenied" in (h.get("Location") or ""), "Viewer can't open user admin")
maker = login("maker1")
s, _, h = req(maker, "/ColumnSettings")
check(s == 302 and "AccessDenied" in (h.get("Location") or ""), "Maker can't open column settings")
s, _, h = req(maker, "/ProductMaster/Approve", {"auditId": "1", "remarks": "x", "__RequestVerificationToken": "forged"})
check(s == 302 and "AccessDenied" in (h.get("Location") or ""), "Maker can't post an approval")
checker = login("checker1")
s, _, _ = req(checker, "/ProductMaster/Approve", {"auditId": "1", "remarks": "x", "__RequestVerificationToken": "forged"})
check(s == 400, f"Checker's POST with a forged antiforgery token is rejected ({s})")

# --- user administration: password policy, create, disable
admin = login("admin1")
new_user = "sec" + uuid.uuid4().hex[:6]
_, page, _ = req(admin, "/Users/Edit")
s, body, _ = req(admin, "/Users/Edit", {"IsNew": "true", "Login": new_user, "DisplayName": "Short Pw", "LevelNo": "1",
                                        "IsActive": "true", "Password": "short", "__RequestVerificationToken": token(page)})
check(s == 200 and "at least 12 characters" in body, "Admin can't create a user with a short password")
check('value="short"' not in body, "Rejected password is not echoed back into the page")
strong = "Str0ng-" + uuid.uuid4().hex
s, _, _ = req(admin, "/Users/Edit", {"IsNew": "true", "Login": new_user, "DisplayName": "Security Test", "LevelNo": "1",
                                     "IsActive": "true", "Password": strong, "__RequestVerificationToken": token(page)})
check(s == 302, "Admin creates a user with a strong password")
user = login(new_user, strong)
s, _, _ = req(user, "/ProductMaster")
check(s == 200, "New user can sign in and browse")

_, page, _ = req(admin, f"/Users/Edit?id={new_user}")
s, _, _ = req(admin, "/Users/Edit", {"IsNew": "false", "Login": new_user, "DisplayName": "Security Test", "LevelNo": "1",
                                     "IsActive": "false", "Password": "", "__RequestVerificationToken": token(page)})
check(s == 302, "Admin disables the user")
s, _, h = req(user, "/ProductMaster")
check(s == 302 and "AccessDenied" in (h.get("Location") or ""), "Disabled user loses access on the next request, without signing out")
_, s, _, _ = try_login(new_user, strong)
check(s == 200, "Disabled user can't sign in again")

_, page, _ = req(admin, "/Users/Edit?id=admin1")
s, body, _ = req(admin, "/Users/Edit", {"IsNew": "false", "Login": "admin1", "DisplayName": "Ada Admin", "LevelNo": "3",
                                        "IsActive": "true", "Password": "", "__RequestVerificationToken": token(page)})
check(s == 200 and "cannot remove your own Admin access" in body, "Admin can't remove their own Admin access")

# --- rate limit (last: uses up this address's sign-in allowance for a minute)
statuses = [try_login("admin1", "wrong-password")[1] for _ in range(LIMIT + 1)]
check(429 in statuses, f"Too many sign-in attempts get HTTP 429 (limit {LIMIT}/min)")

print(f"\n{'ALL PASSED' if fails == 0 else str(fails) + ' FAILED'}")
sys.exit(1 if fails else 0)
