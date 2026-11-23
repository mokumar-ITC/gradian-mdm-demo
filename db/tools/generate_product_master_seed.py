"""Generate the product master seed scripts from the source workbook.

Writes, from 'data/Product_Master v2.0.xlsx':
    db/postgres/002_product_master_seed.sql
    db/fabric/002_product_master_seed.sql

Both scripts load identical data with the same ids, so the app behaves the same on either engine.

Usage (from the repository root):
    pip install openpyxl
    python db/tools/generate_product_master_seed.py ["path/to/workbook.xlsx"]

Cleaning rules for the hierarchy columns (Product_Family, Product_Type, Model, Size):
  * leading/trailing spaces and non-breaking spaces are trimmed;
  * a blank level at the end of the path is dropped, so the product hangs off the deepest level it has
    (for example, Commissioning records have no Size);
  * a blank level in the middle of the path (RDHx has a Size but no Model) becomes "(Not specified)",
    so the deeper level still has a parent.
"""
import sys
from pathlib import Path

import openpyxl

REPO = Path(__file__).resolve().parents[2]
SOURCE = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "data" / "Product_Master v2.0.xlsx"
POSTGRES_TARGET = REPO / "db" / "postgres" / "002_product_master_seed.sql"
FABRIC_TARGET = REPO / "db" / "fabric" / "002_product_master_seed.sql"
NOT_SPECIFIED = "(Not specified)"
SEED_USER = "product_master_seed"
FABRIC_ROWS_PER_INSERT = 500  # T-SQL allows at most 1000 rows in one VALUES list

HEADERS = ["Product_Family", "Product_Type", "Model", "Size", "SIOP_Model", "SIOP_Tier", "Product_Name",
           "Product_Code", "Sections_Per_Unit", "Sale_Price_Per_Unit", "Sale_Price_Per_Hour",
           "Default_Supply_Source", "Est_Fab_Hours", "Est_Assembly_Hours", "Fab_Lbr_Hrs_%", "Assmb_Lbr_Hrs_%",
           "Qty_Fans", "Qty_Coils", "Qty_Panels", "Standard_Unit_Multiplier", "Status", "Capacity Group"]

# workbook header -> column in mdm.product_master (hierarchy fields map to their level)
COLUMN_FOR = {
    "Product_Family": "product_hierarchy (level 1)", "Product_Type": "product_hierarchy (level 2)",
    "Model": "product_hierarchy (level 3)", "Size": "product_hierarchy (level 4)",
    "Fab_Lbr_Hrs_%": "fab_lbr_hrs_pct", "Assmb_Lbr_Hrs_%": "assmb_lbr_hrs_pct", "Capacity Group": "capacity_group",
}

PRODUCT_COLUMNS = ["product_id", "node_id", "product_name", "product_code", "siop_model", "siop_tier",
                   "sections_per_unit", "sale_price_per_unit", "sale_price_per_hour", "default_supply_source",
                   "est_fab_hours", "est_assembly_hours", "fab_lbr_hrs_pct", "assmb_lbr_hrs_pct", "qty_fans",
                   "qty_coils", "qty_panels", "standard_unit_multiplier", "status", "capacity_group", "source_row"]


def clean(value):
    """Trim spaces and non-breaking spaces; blank becomes None."""
    if value is None:
        return None
    text = str(value).replace("\xa0", " ").strip()
    return text or None


def number(value):
    """Workbook number (or numeric text) as int when whole, else float; blank becomes None."""
    value = clean(value)
    if value is None:
        return None
    n = float(value)
    return int(n) if n.is_integer() else n


def hierarchy(row):
    """The product's hierarchy path (1 to 4 names) after the cleaning rules above."""
    levels = [clean(row[i]) for i in range(4)]
    while levels and levels[-1] is None:
        levels.pop()
    return [lvl if lvl is not None else NOT_SPECIFIED for lvl in levels]


def read_workbook():
    """Returns (nodes, products, fields) with ids assigned."""
    wb = openpyxl.load_workbook(SOURCE, data_only=True)
    ws = wb["Product Master"]
    header = [clean(c.value) for c in ws[1]]
    if header != HEADERS:
        sys.exit(f"Unexpected headers in 'Product Master': {header}")

    rows = []
    for row_no, row in enumerate(ws.iter_rows(min_row=2, values_only=True), start=2):
        if any(clean(v) for v in row):
            rows.append((row_no, row))

    # Hierarchy nodes keyed by path; ids are assigned by level, then path, so they are stable between runs
    paths = {}
    for _, row in rows:
        levels = hierarchy(row)
        for depth in range(1, len(levels) + 1):
            paths.setdefault(tuple(levels[:depth]), None)
    ordered = sorted(paths, key=lambda p: (len(p), [s.lower() for s in p]))
    node_ids = {path: i for i, path in enumerate(ordered, start=1)}
    nodes = [{
        "node_id": node_ids[path],
        "parent_node_id": node_ids[path[:-1]] if len(path) > 1 else None,
        "level_no": len(path),
        "node_name": path[-1],
        "node_path": " > ".join(path),
    } for path in ordered]

    # Sibling names must be unique ignoring case (PostgreSQL also has a unique index for this)
    seen = set()
    for n in nodes:
        key = (n["parent_node_id"], n["node_name"].lower())
        if key in seen:
            sys.exit(f"Duplicate hierarchy name under one parent: {n['node_path']}")
        seen.add(key)

    products = []
    for product_id, (row_no, row) in enumerate(rows, start=1):
        products.append({
            "product_id": product_id,
            "node_id": node_ids[tuple(hierarchy(row))],
            "product_name": clean(row[6]),
            "product_code": clean(row[7]),
            "siop_model": clean(row[4]),
            "siop_tier": clean(row[5]),
            "sections_per_unit": number(row[8]),
            "sale_price_per_unit": number(row[9]),
            "sale_price_per_hour": number(row[10]),
            "default_supply_source": clean(row[11]),
            "est_fab_hours": number(row[12]),
            "est_assembly_hours": number(row[13]),
            "fab_lbr_hrs_pct": number(row[14]),
            "assmb_lbr_hrs_pct": number(row[15]),
            "qty_fans": number(row[16]),
            "qty_coils": number(row[17]),
            "qty_panels": number(row[18]),
            "standard_unit_multiplier": number(row[19]),
            "status": clean(row[20]),
            "capacity_group": clean(row[21]),
            "source_row": row_no,
        })

    names = [p["product_name"].lower() for p in products if p["product_name"]]
    if len(names) != len(products) or len(set(names)) != len(names):
        sys.exit("Every product needs a Product_Name, and names must be unique ignoring case")

    fields = []
    in_table = False
    for name, description in wb["Product Master Fields"].iter_rows(min_col=1, max_col=2, values_only=True):
        name = clean(name)
        if name == "Header Field":
            in_table = True
            continue
        if in_table and name in HEADERS:
            fields.append({"field_order": HEADERS.index(name) + 1, "source_field": name,
                           "column_name": COLUMN_FOR.get(name, name.lower()), "description": clean(description)})
    return nodes, products, fields


def literal(value, unicode_prefix):
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "1" if value else "0"
    if isinstance(value, (int, float)):
        return repr(value)
    return ("N" if unicode_prefix else "") + "'" + str(value).replace("'", "''") + "'"


def values_rows(items, columns, extra, unicode_prefix):
    return ["    (" + ", ".join([literal(i[c], unicode_prefix) for c in columns] + extra) + ")" for i in items]


def header(engine, nodes, products):
    return [
        "-- =====================================================================",
        f"-- Gradian Product Master - seed data for {engine}",
        f"-- Generated from '{SOURCE.name}' by db/tools/generate_product_master_seed.py. Do not edit by hand.",
        f"-- {len(products)} products, {len(nodes)} hierarchy nodes. Re-runnable: replaces all product master data",
        "-- (including change requests). Run after 001_schema.sql.",
        "-- =====================================================================",
    ]


def postgres_script(nodes, products, fields):
    out = header("PostgreSQL", nodes, products) + [
        "SET client_encoding = 'UTF8';",
        "BEGIN;",
        "",
        "TRUNCATE mdm.product_master_audit, mdm.product_master, mdm.product_hierarchy, mdm.product_master_field RESTART IDENTITY;",
        "",
        "INSERT INTO mdm.product_hierarchy (node_id, parent_node_id, level_no, node_name, node_path, isactive, createdby, createddate) VALUES",
        ",\n".join(values_rows(nodes, ["node_id", "parent_node_id", "level_no", "node_name", "node_path"],
                                ["TRUE", f"'{SEED_USER}'", "now()"], False)) + ";",
        "",
        f"INSERT INTO mdm.product_master ({', '.join(PRODUCT_COLUMNS)}, isactive, createdby, createddate) VALUES",
        ",\n".join(values_rows(products, PRODUCT_COLUMNS, ["TRUE", f"'{SEED_USER}'", "now()"], False)) + ";",
        "",
        "INSERT INTO mdm.product_master_field (field_order, source_field, column_name, description) VALUES",
        ",\n".join(values_rows(fields, ["field_order", "source_field", "column_name", "description"], [], False)) + ";",
        "",
        "-- Ids were given explicitly, so move the identity sequences past them",
        "SELECT setval(pg_get_serial_sequence('mdm.product_hierarchy', 'node_id'), (SELECT max(node_id) FROM mdm.product_hierarchy));",
        "SELECT setval(pg_get_serial_sequence('mdm.product_master', 'product_id'), (SELECT max(product_id) FROM mdm.product_master));",
        "",
        "COMMIT;",
        "",
    ]
    return "\n".join(out)


def fabric_inserts(table, columns, items, extra_columns, extra_values):
    out = []
    for start in range(0, len(items), FABRIC_ROWS_PER_INSERT):
        chunk = items[start:start + FABRIC_ROWS_PER_INSERT]
        out.append(f"INSERT INTO {table} ({', '.join(columns + extra_columns)}) VALUES")
        out.append(",\n".join(values_rows(chunk, columns, extra_values, True)) + ";")
    return out


def fabric_script(nodes, products, fields):
    audit = ["isactive", "createdby", "createddate"]
    audit_values = ["1", f"'{SEED_USER}'", "SYSUTCDATETIME()"]
    out = header("Microsoft Fabric Warehouse (T-SQL)", nodes, products) + [
        "BEGIN TRANSACTION;",
        "",
        "DELETE FROM mdm.product_master_audit;",
        "DELETE FROM mdm.product_master;",
        "DELETE FROM mdm.product_hierarchy;",
        "DELETE FROM mdm.product_master_field;",
        "",
    ]
    out += fabric_inserts("mdm.product_hierarchy", ["node_id", "parent_node_id", "level_no", "node_name", "node_path"], nodes, audit, audit_values)
    out.append("")
    out += fabric_inserts("mdm.product_master", PRODUCT_COLUMNS, products, audit, audit_values)
    out.append("")
    out += fabric_inserts("mdm.product_master_field", ["field_order", "source_field", "column_name", "description"], fields, [], [])
    out += [
        "",
        "-- Change requests were deleted above, so new audit ids start again at 1",
        "UPDATE mdm.key_counter SET last_value = 0 WHERE counter_name = 'product_master_audit';",
        "",
        "COMMIT TRANSACTION;",
        "",
    ]
    return "\n".join(out)


def main():
    nodes, products, fields = read_workbook()
    FABRIC_TARGET.parent.mkdir(parents=True, exist_ok=True)
    POSTGRES_TARGET.write_text(postgres_script(nodes, products, fields), encoding="utf-8", newline="\n")
    FABRIC_TARGET.write_text(fabric_script(nodes, products, fields), encoding="utf-8", newline="\n")
    print(f"Wrote {POSTGRES_TARGET.relative_to(REPO)} and {FABRIC_TARGET.relative_to(REPO)}: "
          f"{len(products)} products, {len(nodes)} hierarchy nodes, {len(fields)} field descriptions")


if __name__ == "__main__":
    main()
