-- =====================================================================
-- Gradian MDM - schema for Microsoft Fabric Warehouse (T-SQL)
-- Product hierarchy: Product Family -> Product Type -> Model -> Size -> Product
-- Idempotent: safe to run again. Runs as a single batch (no GO), so it works in the
-- Fabric SQL query editor, SSMS, Azure Data Studio / VS Code mssql and sqlcmd.
-- Next: 002_product_master_seed.sql (data), 003_demo_users.sql (demo / test only).
--
-- Fabric Warehouse differences from the PostgreSQL script, all handled by the app:
--   * no IDENTITY / sequences  -> the app takes new keys from mdm.key_counter inside its write transaction
--   * no DEFAULT, CHECK or FOREIGN KEY constraints, and PRIMARY KEY / UNIQUE are NOT ENFORCED
--                              -> the app always writes every column and checks uniqueness itself
--   * varchar uses a UTF-8 collation (no nvarchar), datetime2(6) instead of datetime / timestamptz
--   * no recursive CTEs        -> vw_product_master uses fixed joins (the tree is four levels deep)
-- All timestamps are written by the app in UTC.
-- =====================================================================

IF SCHEMA_ID('mdm') IS NULL EXEC('CREATE SCHEMA mdm');
IF SCHEMA_ID('publish') IS NULL EXEC('CREATE SCHEMA publish');

-- ---------------------------------------------------------------------
-- 1. Sign-in accounts. Levels: 1 = Viewer, 2 = Maker, 3 = Checker, 4 = Admin
-- ---------------------------------------------------------------------
IF OBJECT_ID('mdm.user_account', 'U') IS NULL
CREATE TABLE mdm.user_account (
    login          varchar(100) NOT NULL,
    display_name   varchar(255) NULL,
    level_no       smallint     NOT NULL,
    level_desc     varchar(50)  NULL,
    password_hash  varchar(255) NULL,
    is_active      bit          NOT NULL,
    last_login     datetime2(6) NULL,
    created_date   datetime2(6) NOT NULL
);

-- ---------------------------------------------------------------------
-- 2. Admin column settings (Administration > Column Editing)
-- ---------------------------------------------------------------------
IF OBJECT_ID('mdm.column_edit_setting', 'U') IS NULL
CREATE TABLE mdm.column_edit_setting (
    table_key     varchar(50)  NOT NULL,
    column_key    varchar(100) NOT NULL,
    is_editable   bit          NOT NULL,
    display_name  varchar(100) NULL,
    updated_by    varchar(255) NULL,
    updated_date  datetime2(6) NOT NULL
);

-- ---------------------------------------------------------------------
-- 3. Hierarchy levels
-- ---------------------------------------------------------------------
IF OBJECT_ID('mdm.product_hierarchy_level', 'U') IS NULL
CREATE TABLE mdm.product_hierarchy_level (
    level_no      smallint    NOT NULL,
    level_name    varchar(50) NOT NULL,
    source_field  varchar(50) NOT NULL
);

DELETE FROM mdm.product_hierarchy_level;
INSERT INTO mdm.product_hierarchy_level (level_no, level_name, source_field) VALUES
    (1, 'Product Family', 'Product_Family'),
    (2, 'Product Type',   'Product_Type'),
    (3, 'Model',          'Model'),
    (4, 'Size',           'Size');

-- ---------------------------------------------------------------------
-- 4. Hierarchy nodes
-- ---------------------------------------------------------------------
IF OBJECT_ID('mdm.product_hierarchy', 'U') IS NULL
CREATE TABLE mdm.product_hierarchy (
    node_id         bigint        NOT NULL,
    parent_node_id  bigint        NULL,
    level_no        smallint      NOT NULL,
    node_name       varchar(255)  NOT NULL,
    node_path       varchar(1000) NOT NULL,
    isactive        bit           NOT NULL,
    createdby       varchar(255)  NULL,
    updatedby       varchar(255)  NULL,
    createddate     datetime2(6)  NULL,
    updateddate     datetime2(6)  NULL
);

-- ---------------------------------------------------------------------
-- 5. Products (updatestatus: PendingApproval | Approved | Rejected | UpdateDirectly)
-- ---------------------------------------------------------------------
IF OBJECT_ID('mdm.product_master', 'U') IS NULL
CREATE TABLE mdm.product_master (
    product_id                bigint         NOT NULL,
    node_id                   bigint         NOT NULL,
    product_name              varchar(255)   NOT NULL,
    product_code              varchar(50)    NULL,
    siop_model                varchar(50)    NULL,
    siop_tier                 varchar(50)    NULL,
    sections_per_unit         int            NULL,
    sale_price_per_unit       decimal(14,2)  NULL,
    sale_price_per_hour       decimal(12,2)  NULL,
    default_supply_source     varchar(20)    NULL,
    est_fab_hours             decimal(10,2)  NULL,
    est_assembly_hours        decimal(10,2)  NULL,
    fab_lbr_hrs_pct           decimal(7,2)   NULL,
    assmb_lbr_hrs_pct         decimal(7,2)   NULL,
    qty_fans                  int            NULL,
    qty_coils                 int            NULL,
    qty_panels                int            NULL,
    standard_unit_multiplier  decimal(7,2)   NULL,
    status                    varchar(20)    NOT NULL,
    capacity_group            varchar(100)   NULL,
    source_row                int            NULL,
    isactive                  bit            NOT NULL,
    createdby                 varchar(255)   NULL,
    updatedby                 varchar(255)   NULL,
    createddate               datetime2(6)   NULL,
    updateddate               datetime2(6)   NULL,
    updatestatus              varchar(50)    NULL,
    lastmodifiedbyrole        varchar(50)    NULL,
    remarks                   varchar(4000)  NULL
);

-- ---------------------------------------------------------------------
-- 6. Change requests (maker / checker)
-- ---------------------------------------------------------------------
IF OBJECT_ID('mdm.product_master_audit', 'U') IS NULL
CREATE TABLE mdm.product_master_audit (
    auditid                   bigint         NOT NULL,
    product_id                bigint         NOT NULL,
    product_name              varchar(255)   NOT NULL,
    product_code              varchar(50)    NULL,
    siop_model                varchar(50)    NULL,
    siop_tier                 varchar(50)    NULL,
    sections_per_unit         int            NULL,
    sale_price_per_unit       decimal(14,2)  NULL,
    sale_price_per_hour       decimal(12,2)  NULL,
    default_supply_source     varchar(20)    NULL,
    est_fab_hours             decimal(10,2)  NULL,
    est_assembly_hours        decimal(10,2)  NULL,
    fab_lbr_hrs_pct           decimal(7,2)   NULL,
    assmb_lbr_hrs_pct         decimal(7,2)   NULL,
    qty_fans                  int            NULL,
    qty_coils                 int            NULL,
    qty_panels                int            NULL,
    standard_unit_multiplier  decimal(7,2)   NULL,
    status                    varchar(20)    NOT NULL,
    capacity_group            varchar(100)   NULL,
    changetype                varchar(20)    NOT NULL,
    approvalstatus            varchar(50)    NOT NULL,
    makerid                   varchar(255)   NULL,
    checkerid                 varchar(255)   NULL,
    changetimestamp           datetime2(6)   NOT NULL,
    approvaltimestamp         datetime2(6)   NULL,
    lastmodifiedbyrole        varchar(50)    NULL,
    remarks                   varchar(4000)  NULL,
    checker_remarks           varchar(4000)  NULL
);

-- ---------------------------------------------------------------------
-- 7. Field dictionary
-- ---------------------------------------------------------------------
IF OBJECT_ID('mdm.product_master_field', 'U') IS NULL
CREATE TABLE mdm.product_master_field (
    field_order   smallint      NOT NULL,
    source_field  varchar(100)  NOT NULL,
    column_name   varchar(100)  NOT NULL,
    description   varchar(4000) NULL
);

-- ---------------------------------------------------------------------
-- 7b. Key counters (Fabric has no IDENTITY). One row per table that needs new keys;
--     the app increments the row in the same transaction as the insert, so keys never repeat.
-- ---------------------------------------------------------------------
IF OBJECT_ID('mdm.key_counter', 'U') IS NULL
CREATE TABLE mdm.key_counter (
    counter_name  varchar(100) NOT NULL,
    last_value    bigint       NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM mdm.key_counter WHERE counter_name = 'product_master_audit')
    INSERT INTO mdm.key_counter (counter_name, last_value)
    SELECT 'product_master_audit', COALESCE(MAX(auditid), 0) FROM mdm.product_master_audit;

-- ---------------------------------------------------------------------
-- Informational keys (NOT ENFORCED). Fabric uses them for query planning and
-- Power BI relationships; uniqueness is enforced by the app.
-- ---------------------------------------------------------------------
IF OBJECT_ID('mdm.pk_user_account', 'PK') IS NULL
    ALTER TABLE mdm.user_account ADD CONSTRAINT pk_user_account PRIMARY KEY NONCLUSTERED (login) NOT ENFORCED;
IF OBJECT_ID('mdm.pk_column_edit_setting', 'PK') IS NULL
    ALTER TABLE mdm.column_edit_setting ADD CONSTRAINT pk_column_edit_setting PRIMARY KEY NONCLUSTERED (table_key, column_key) NOT ENFORCED;
IF OBJECT_ID('mdm.pk_product_hierarchy_level', 'PK') IS NULL
    ALTER TABLE mdm.product_hierarchy_level ADD CONSTRAINT pk_product_hierarchy_level PRIMARY KEY NONCLUSTERED (level_no) NOT ENFORCED;
IF OBJECT_ID('mdm.pk_product_hierarchy', 'PK') IS NULL
    ALTER TABLE mdm.product_hierarchy ADD CONSTRAINT pk_product_hierarchy PRIMARY KEY NONCLUSTERED (node_id) NOT ENFORCED;
IF OBJECT_ID('mdm.pk_product_master', 'PK') IS NULL
    ALTER TABLE mdm.product_master ADD CONSTRAINT pk_product_master PRIMARY KEY NONCLUSTERED (product_id) NOT ENFORCED;
IF OBJECT_ID('mdm.pk_product_master_audit', 'PK') IS NULL
    ALTER TABLE mdm.product_master_audit ADD CONSTRAINT pk_product_master_audit PRIMARY KEY NONCLUSTERED (auditid) NOT ENFORCED;
IF OBJECT_ID('mdm.pk_product_master_field', 'PK') IS NULL
    ALTER TABLE mdm.product_master_field ADD CONSTRAINT pk_product_master_field PRIMARY KEY NONCLUSTERED (field_order) NOT ENFORCED;
IF OBJECT_ID('mdm.pk_key_counter', 'PK') IS NULL
    ALTER TABLE mdm.key_counter ADD CONSTRAINT pk_key_counter PRIMARY KEY NONCLUSTERED (counter_name) NOT ENFORCED;

-- ---------------------------------------------------------------------
-- 8. Each product with its full hierarchy path (same definition as PostgreSQL)
-- ---------------------------------------------------------------------
EXEC('CREATE OR ALTER VIEW mdm.vw_product_master AS
WITH chain AS (
    SELECT h.node_id, h.node_path,
           h.level_no  AS l0, h.node_name  AS n0,
           p1.level_no AS l1, p1.node_name AS n1,
           p2.level_no AS l2, p2.node_name AS n2,
           p3.level_no AS l3, p3.node_name AS n3
      FROM mdm.product_hierarchy h
      LEFT JOIN mdm.product_hierarchy p1 ON p1.node_id = h.parent_node_id
      LEFT JOIN mdm.product_hierarchy p2 ON p2.node_id = p1.parent_node_id
      LEFT JOIN mdm.product_hierarchy p3 ON p3.node_id = p2.parent_node_id
)
SELECT p.product_id, p.node_id, c.node_path,
       CASE 1 WHEN c.l0 THEN c.n0 WHEN c.l1 THEN c.n1 WHEN c.l2 THEN c.n2 WHEN c.l3 THEN c.n3 END AS product_family,
       CASE 2 WHEN c.l0 THEN c.n0 WHEN c.l1 THEN c.n1 WHEN c.l2 THEN c.n2 END                     AS product_type,
       CASE 3 WHEN c.l0 THEN c.n0 WHEN c.l1 THEN c.n1 END                                          AS model,
       CASE 4 WHEN c.l0 THEN c.n0 END                                                              AS size,
       p.product_name, p.product_code, p.siop_model, p.siop_tier, p.sections_per_unit,
       p.sale_price_per_unit, p.sale_price_per_hour, p.default_supply_source,
       p.est_fab_hours, p.est_assembly_hours, p.fab_lbr_hrs_pct, p.assmb_lbr_hrs_pct,
       p.qty_fans, p.qty_coils, p.qty_panels, p.standard_unit_multiplier,
       p.status, p.capacity_group, p.source_row, p.isactive,
       p.createdby, p.createddate, p.updatedby, p.updateddate,
       p.updatestatus, p.lastmodifiedbyrole, p.remarks
  FROM mdm.product_master p
  JOIN chain c ON c.node_id = p.node_id');

-- ---------------------------------------------------------------------
-- 9. Publish view for downstream systems (Power BI, pipelines), using the workbook's headers
-- ---------------------------------------------------------------------
EXEC('CREATE OR ALTER VIEW publish.product_master_vw AS
SELECT product_family           AS [Product_Family],
       product_type             AS [Product_Type],
       model                    AS [Model],
       size                     AS [Size],
       siop_model               AS [SIOP_Model],
       siop_tier                AS [SIOP_Tier],
       product_name             AS [Product_Name],
       product_code             AS [Product_Code],
       sections_per_unit        AS [Sections_Per_Unit],
       sale_price_per_unit      AS [Sale_Price_Per_Unit],
       sale_price_per_hour      AS [Sale_Price_Per_Hour],
       default_supply_source    AS [Default_Supply_Source],
       est_fab_hours            AS [Est_Fab_Hours],
       est_assembly_hours       AS [Est_Assembly_Hours],
       fab_lbr_hrs_pct          AS [Fab_Lbr_Hrs_%],
       assmb_lbr_hrs_pct        AS [Assmb_Lbr_Hrs_%],
       qty_fans                 AS [Qty_Fans],
       qty_coils                AS [Qty_Coils],
       qty_panels               AS [Qty_Panels],
       standard_unit_multiplier AS [Standard_Unit_Multiplier],
       status                   AS [Status],
       capacity_group           AS [Capacity Group]
  FROM mdm.vw_product_master
 WHERE isactive = 1');
