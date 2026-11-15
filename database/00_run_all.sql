/* =====================================================================
   הרצת כל הסקריפטים לפי הסדר (במצב SQLCMD).
   מתוך תיקיית database:
     sqlcmd -S localhost -U sa -P "<sa password>" -C -f 65001 -i 00_run_all.sql ^
            -v AppPassword="DcApp#2026!" ReaderPassword="DcReader#2026!"
   ב-SSMS: Query > SQLCMD Mode, ואז להגדיר את המשתנים בראש הקובץ (:setvar).
   ===================================================================== */
:on error exit
:r ./01_create_database.sql
:r ./02_business_schema.sql
:r ./03_business_seed.sql
:r ./04_control_engine_schema.sql
:r ./05_metadata_seed.sql
:r ./06_sample_controls.sql
:r ./07_security.sql
PRINT N'בסיס הנתונים DataControlsDb הוקם בהצלחה';
