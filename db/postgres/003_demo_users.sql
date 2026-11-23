-- =====================================================================
-- Gradian MDM - demo sign-in accounts. DEMO / TEST ONLY: never run this in production.
-- Every account's password is: Demo@123   (PBKDF2-SHA256, 100,000 iterations)
-- Levels: 1 = Viewer, 2 = Maker, 3 = Checker, 4 = Admin
-- Re-runnable: resets these five accounts.
-- =====================================================================
INSERT INTO mdm.user_account (login, level_no, level_desc, display_name, password_hash, is_active) VALUES
 ('viewer1',  1, 'Viewer',  'Vera Viewer',   'PBKDF2$100000$P3d1vzVxLvng9hYudyplvA==$1WHiw4YuRYSIkvVV3hAyVFkLbu+uA0Zwu1BYt29fBE0=', true),
 ('maker1',   2, 'Maker',   'Mark Maker',    'PBKDF2$100000$f+ftMIyeJb3Ar0ttXnBS3w==$zyVRaIQUY4KbNSQI3ptTjWRyhJkhUZ+3azhSGooOd4k=', true),
 ('maker2',   2, 'Maker',   'Maya Maker',    'PBKDF2$100000$AG2GcLcedFpCybyI8gIDrg==$Iwczv5GiZ7TmyEZTScTKoImH7HQnOrN4FmCM95URmEg=', true),
 ('checker1', 3, 'Checker', 'Chris Checker', 'PBKDF2$100000$8HKX53XWMqDzs1bJmEb0UA==$FLrCGIlEVOw+bebUiO7uFRcqnerdd+tceZXOzIA23r0=', true),
 ('admin1',   4, 'Admin',   'Ada Admin',     'PBKDF2$100000$J1s7dzfsRJf6hPuqSILbPg==$u4mmE1IplqEkIzk17RIePp8aR2bapGRchb+NxT01IEI=', true)
ON CONFLICT (login) DO UPDATE SET level_no = EXCLUDED.level_no, level_desc = EXCLUDED.level_desc,
    display_name = EXCLUDED.display_name, password_hash = EXCLUDED.password_hash, is_active = true;
