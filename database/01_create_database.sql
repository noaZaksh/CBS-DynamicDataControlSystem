/* =====================================================================
   01 - יצירת בסיס הנתונים והסכמות
   hr  : נתונים עסקיים (הנתונים שעליהם רצות הבקרות)
   ctl : מנוע הבקרות (מילון נתונים עסקי, הגדרות בקרות, היסטוריית הרצות)
   ===================================================================== */
IF DB_ID(N'DataControlsDb') IS NULL
BEGIN
    CREATE DATABASE DataControlsDb COLLATE Hebrew_CI_AS;
END
GO

USE DataControlsDb;
GO

IF SCHEMA_ID(N'hr') IS NULL EXEC(N'CREATE SCHEMA hr AUTHORIZATION dbo;');
GO
IF SCHEMA_ID(N'ctl') IS NULL EXEC(N'CREATE SCHEMA ctl AUTHORIZATION dbo;');
GO
