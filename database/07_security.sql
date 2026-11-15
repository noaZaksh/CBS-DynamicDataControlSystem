/* =====================================================================
   07 - הרשאות (Least Privilege)

   dc_app    : משתמש האפליקציה - קורא/כותב רק בסכמת ctl (הגדרות והיסטוריה).
               אין לו שום הרשאה על הנתונים העסקיים.
   dc_reader : משתמש הרצת הבקרות - SELECT בלבד על סכמת hr.
               גם אם (בתרחיש תיאורטי) תיווצר שאילתה זדונית, ה-DB עצמו
               ימנע כל שינוי נתונים. זו שכבת ההגנה האחרונה.

   הסיסמאות מועברות כמשתני sqlcmd:  -v AppPassword="..." ReaderPassword="..."
   ===================================================================== */
USE master;
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'dc_app')
    CREATE LOGIN dc_app WITH PASSWORD = N'$(AppPassword)', CHECK_POLICY = ON;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'dc_reader')
    CREATE LOGIN dc_reader WITH PASSWORD = N'$(ReaderPassword)', CHECK_POLICY = ON;
GO

USE DataControlsDb;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'dc_app')
    CREATE USER dc_app FOR LOGIN dc_app WITH DEFAULT_SCHEMA = ctl;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'dc_reader')
    CREATE USER dc_reader FOR LOGIN dc_reader WITH DEFAULT_SCHEMA = hr;
GO

-- אפליקציה: ניהול הגדרות בלבד + קריאת קטלוג המערכת (לגילוי סכמה אוטומטי)
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::ctl TO dc_app;
GRANT VIEW DEFINITION ON SCHEMA::hr TO dc_app;
DENY  SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::hr TO dc_app;

-- מריץ בקרות: קריאה בלבד מהנתונים העסקיים, שום דבר אחר
GRANT SELECT ON SCHEMA::hr TO dc_reader;
DENY  INSERT, UPDATE, DELETE, EXECUTE, ALTER, REFERENCES ON SCHEMA::hr  TO dc_reader;
DENY  SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER ON SCHEMA::ctl TO dc_reader;
GO
