# Security

## Reporting a vulnerability

Please do not open a public issue. Use GitHub's **private vulnerability reporting** (Security > Report a vulnerability) on this repository, or contact the repository owners directly. Include the steps to reproduce and the affected version or commit. You can expect an acknowledgement within five working days.

## Controls built into the application

| Area | Control |
|---|---|
| Authentication | Cookie sign-in against `mdm.user_account`. Passwords are stored only as PBKDF2-HMAC-SHA256 hashes (600,000 iterations, 16-byte random salt) and compared in constant time. |
| Brute force | Sign-in is rate-limited per client address (`Security:LoginAttemptsPerMinute`, default 10; extra attempts get HTTP 429). Wrong password and unknown user produce the same message and take about the same time. |
| Password policy | New and reset passwords need at least 12 characters. Rejected passwords are never echoed back into the page. |
| Authorization | Every page requires a signed-in user unless explicitly marked anonymous (sign-in, error pages, `/health`). Each action also requires an access level (Viewer, Maker, Checker, Admin). The level is read from the database, not trusted from the cookie, and is cached for at most 5 minutes; a disabled account or level change applies on the user's next request after an admin edit. |
| Maker / checker | A Checker can't approve or reject their own change. A product can't have two open change requests. Locked columns keep their stored value whatever the browser posts. |
| CSRF | Antiforgery tokens are validated on every POST (global filter). Cookies are `HttpOnly`, `Secure` outside Development, and `SameSite`. |
| Open redirect | Only local return URLs are followed after sign-in or bulk actions. |
| SQL injection | All SQL is parameterised. Table and column names come only from constants in the code. |
| Browser hardening | Content-Security-Policy (no third-party origins, `frame-ancestors 'none'`, `object-src 'none'`), `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, HSTS outside Development. All scripts, styles and fonts are served from the app itself; there are no CDN dependencies. |
| Errors | Outside Development, unexpected errors show a generic page with a request id; details go only to the log. `/health` reports only the status. |
| Secrets | No secrets in the repository. Connection strings come from user-secrets (local), environment variables, or App Service settings / Key Vault references. Fabric Warehouse uses Microsoft Entra ID (managed identity) instead of passwords. CI/CD signs in to Azure with OpenID Connect; no publish profiles or passwords are stored in GitHub. |
| Dependencies | NuGet audit fails the restore on any known vulnerability (direct or transitive). Dependabot proposes package and GitHub Actions updates weekly. Actions are pinned to commit SHAs. CodeQL (`security-extended`) runs on every pull request and weekly. |
| Database access | `db/postgres/004_app_role.sql` and `db/fabric/004_app_permissions.sql` give the app only SELECT / INSERT / UPDATE on its own schemas: no DELETE, no schema changes. |

## Operating the demo safely

- `db/*/003_demo_users.sql` creates five accounts with a published password (`Demo@123`). **Never run it in production.** Create real accounts under Administration > Users & Access instead, and disable the demo accounts in any shared environment.
- Keep `Demo:ShowDemoAccounts` set to `false` anywhere people outside the demo can reach the app: it lists the accounts on the sign-in page. The app logs a warning when it is on outside Development.
- `data/Product_Master v2.0.xlsx` and the generated seed scripts contain Gradian business data (product codes, price assumptions). Copy or publish them elsewhere only with Gradian's approval.
- Turn on GitHub secret scanning and push protection for the repository, and require pull requests with passing CI for `main`.
- Add required reviewers to the `production` environment so production deployments wait for an approval.
