-- =====================================================================
-- Gradian MDM - demo sign-in accounts for Fabric Warehouse. DEMO / TEST ONLY: never run this in production.
-- Every account's password is: Demo@123   (PBKDF2-SHA256, 100,000 iterations)
-- Levels: 1 = Viewer, 2 = Maker, 3 = Checker, 4 = Admin
-- Re-runnable: resets these five accounts.
-- =====================================================================
DELETE FROM mdm.user_account WHERE login IN ('viewer1', 'maker1', 'maker2', 'checker1', 'admin1');

INSERT INTO mdm.user_account (login, level_no, level_desc, display_name, password_hash, is_active, created_date) VALUES
 ('viewer1',  1, 'Viewer',  'Vera Viewer',   'PBKDF2$100000$P3d1vzVxLvng9hYudyplvA==$1WHiw4YuRYSIkvVV3hAyVFkLbu+uA0Zwu1BYt29fBE0=', 1, SYSUTCDATETIME()),
 ('maker1',   2, 'Maker',   'Mark Maker',    'PBKDF2$100000$f+ftMIyeJb3Ar0ttXnBS3w==$zyVRaIQUY4KbNSQI3ptTjWRyhJkhUZ+3azhSGooOd4k=', 1, SYSUTCDATETIME()),
 ('maker2',   2, 'Maker',   'Maya Maker',    'PBKDF2$100000$AG2GcLcedFpCybyI8gIDrg==$Iwczv5GiZ7TmyEZTScTKoImH7HQnOrN4FmCM95URmEg=', 1, SYSUTCDATETIME()),
 ('checker1', 3, 'Checker', 'Chris Checker', 'PBKDF2$100000$8HKX53XWMqDzs1bJmEb0UA==$FLrCGIlEVOw+bebUiO7uFRcqnerdd+tceZXOzIA23r0=', 1, SYSUTCDATETIME()),
 ('admin1',   4, 'Admin',   'Ada Admin',     'PBKDF2$100000$J1s7dzfsRJf6hPuqSILbPg==$u4mmE1IplqEkIzk17RIePp8aR2bapGRchb+NxT01IEI=', 1, SYSUTCDATETIME());
