"""Runs every end-to-end suite against the web app, in the right order. Used by CI and for local checks.

    python tests/e2e/run_all.py [--reset] [--start] [--base-url URL] [--dotnet-arg ARG ...]

  --reset   reload the PostgreSQL test database first (WIPES its data). Uses psql and the standard libpq
            environment variables: PGHOST, PGPORT, PGUSER, PGPASSWORD, PGDATABASE.
  --start   build-free start of the web app (`dotnet run --no-build`) for the duration of the tests, in the
            Development environment; the connection string must be set (user-secrets or ConnectionStrings__Mdm).
  --dotnet-arg  extra argument for `dotnet run`, e.g. --dotnet-arg=-p:MdmTargetFramework=net9.0

Only standard-library Python is needed.
"""
import argparse, os, shutil, subprocess, sys, time, urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
LOGIN_LIMIT = 40  # sign-in attempts per minute allowed while testing; the suites sign in about 20 times

RESET_SQL = [REPO / "db/postgres/001_schema.sql", REPO / "db/postgres/002_product_master_seed.sql", REPO / "db/postgres/003_demo_users.sql"]
CLEANUP_SQL = ("DELETE FROM mdm.column_edit_setting; "
               "DELETE FROM mdm.user_account WHERE login NOT IN ('viewer1','maker1','maker2','checker1','admin1');")


def reset_database():
    psql = shutil.which("psql") or r"C:\Program Files\PostgreSQL\18\bin\psql.exe"
    args = [psql, "-q", "-v", "ON_ERROR_STOP=1"]
    for script in RESET_SQL:
        args += ["-f", str(script)]
    args += ["-c", CLEANUP_SQL]
    print("== Reset test database", flush=True)
    subprocess.run(args, check=True, stdout=subprocess.DEVNULL)


def wait_for(url, seconds=90):
    deadline = time.time() + seconds
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(url + "/health", timeout=3) as r:
                if r.status == 200:
                    return True
        except OSError:
            time.sleep(1)
    return False


def start_app(base_url, dotnet_args):
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", Security__LoginAttemptsPerMinute=str(LOGIN_LIMIT))
    cmd = ["dotnet", "run", "--no-build", "--no-launch-profile", "--project", str(REPO / "src/Gradian.Mdm.Web"),
           *dotnet_args, "--", "--urls", base_url]
    print("== Start app: " + " ".join(cmd), flush=True)
    log = open(REPO / "e2e-app.log", "w", encoding="utf-8")
    return subprocess.Popen(cmd, env=env, stdout=log, stderr=subprocess.STDOUT), log


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://localhost:5081")
    parser.add_argument("--reset", action="store_true")
    parser.add_argument("--start", action="store_true")
    parser.add_argument("--dotnet-arg", action="append", default=[])
    opts = parser.parse_args()

    if opts.reset:
        reset_database()

    app = log = None
    if opts.start:
        app, log = start_app(opts.base_url, opts.dotnet_arg)
    try:
        if not wait_for(opts.base_url):
            print("App did not become healthy - see e2e-app.log")
            return 1
        suites = [  # order matters: the security suite uses up the sign-in rate limit, so it runs last
            ["e2e_product_master.py", opts.base_url, "2"],
            ["e2e_product_bulk.py", opts.base_url],
            ["e2e_concurrency.py", opts.base_url],
            ["e2e_security.py", opts.base_url, str(LOGIN_LIMIT)],
        ]
        failed = []
        for suite in suites:
            print(f"\n== {suite[0]}", flush=True)
            if subprocess.run([sys.executable, str(HERE / suite[0]), *suite[1:]]).returncode != 0:
                failed.append(suite[0])
        print("\n== RESULT: " + ("ALL SUITES PASSED" if not failed else "FAILED: " + ", ".join(failed)))
        return 1 if failed else 0
    finally:
        if app:
            app.terminate()
            try:
                app.wait(timeout=15)
            except subprocess.TimeoutExpired:
                app.kill()
            log.close()


if __name__ == "__main__":
    sys.exit(main())
