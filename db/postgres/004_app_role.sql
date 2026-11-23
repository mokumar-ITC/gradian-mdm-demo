-- =====================================================================
-- Gradian MDM - least-privilege login for the web app (optional, recommended outside local dev).
-- The app needs to read everything and insert / update rows; it never deletes rows or changes the schema.
-- Run as the database owner, passing the password as a psql variable so it is not stored in this file:
--   psql -d gradian_mdm -v app_password='<strong password>' -f db/postgres/004_app_role.sql
-- =====================================================================
SELECT format('CREATE ROLE gradian_app LOGIN PASSWORD %L', :'app_password')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'gradian_app') \gexec
ALTER ROLE gradian_app PASSWORD :'app_password';

GRANT CONNECT ON DATABASE gradian_mdm TO gradian_app;
GRANT USAGE ON SCHEMA mdm, publish TO gradian_app;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA mdm TO gradian_app;
GRANT SELECT ON ALL TABLES IN SCHEMA publish TO gradian_app;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA mdm TO gradian_app;

-- Read-only role for downstream consumers of the publish views
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'gradian_reader') THEN CREATE ROLE gradian_reader NOLOGIN; END IF;
END $$;
GRANT USAGE ON SCHEMA publish TO gradian_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA publish TO gradian_reader;
