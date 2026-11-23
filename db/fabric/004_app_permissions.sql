-- =====================================================================
-- Gradian MDM - least-privilege access for the web app on Fabric Warehouse.
--
-- The app signs in with Microsoft Entra ID (no SQL passwords): the App Service's managed identity
-- in Azure, or a service principal / your own account locally. Steps:
--   1. In the Fabric workspace, share the warehouse with that identity with the "Read" permission only
--      (do NOT give it a workspace role such as Contributor - that would allow schema changes).
--   2. Run the statements below in the warehouse, replacing <app-identity-name> with the
--      App Service name (system-assigned identity) or the service principal's display name.
-- The app needs to read everything and insert / update rows; it never deletes rows or changes the schema.
-- =====================================================================

-- GRANT SELECT, INSERT, UPDATE ON SCHEMA::mdm TO [<app-identity-name>];
-- GRANT SELECT ON SCHEMA::publish TO [<app-identity-name>];

-- Read-only access for report authors / downstream pipelines:
-- GRANT SELECT ON SCHEMA::publish TO [<reader-group-name>];
