/* =====================================================================
   02 - סכמת הנתונים העסקיים (hr)
   שימו לב: חלק מהאילוצים מכוונים להיות "רכים" (NULL מותר וכו')
   כדי שאפשר יהיה לטעון נתונים שגויים ולהדגים את הבקרות.
   ===================================================================== */
USE DataControlsDb;
GO

IF OBJECT_ID(N'hr.EmployeeProjects', N'U') IS NOT NULL DROP TABLE hr.EmployeeProjects;
IF OBJECT_ID(N'hr.Projects', N'U')         IS NOT NULL DROP TABLE hr.Projects;
IF OBJECT_ID(N'hr.FK_Departments_Manager', N'F') IS NOT NULL
    ALTER TABLE hr.Departments DROP CONSTRAINT FK_Departments_Manager;
IF OBJECT_ID(N'hr.Employees', N'U')        IS NOT NULL DROP TABLE hr.Employees;
IF OBJECT_ID(N'hr.Departments', N'U')      IS NOT NULL DROP TABLE hr.Departments;
GO

CREATE TABLE hr.Departments
(
    DepartmentId       INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
    DepartmentCode     NVARCHAR(10)   NOT NULL CONSTRAINT UQ_Departments_Code UNIQUE,
    DepartmentName     NVARCHAR(100)  NOT NULL,
    IsActive           BIT            NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT (1),
    AnnualBudget       DECIMAL(14,2)  NULL,
    ManagerEmployeeId  INT            NULL,
    CreatedAt          DATETIME2(0)   NOT NULL CONSTRAINT DF_Departments_CreatedAt DEFAULT (SYSUTCDATETIME())
);
GO

CREATE TABLE hr.Employees
(
    EmployeeId      INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    IdNumber        NVARCHAR(9)    NULL,             -- ת"ז (חובה עסקית, לא חובה טכנית)
    FirstName       NVARCHAR(50)   NOT NULL,
    LastName        NVARCHAR(50)   NOT NULL,
    BirthDate       DATE           NULL,
    Email           NVARCHAR(200)  NULL,
    Phone           NVARCHAR(20)   NULL,
    City            NVARCHAR(50)   NULL,
    JobTitle        NVARCHAR(100)  NULL,
    EmploymentType  NVARCHAR(20)   NULL,             -- משרה מלאה / משרה חלקית / שעתי
    DepartmentId    INT            NULL CONSTRAINT FK_Employees_Departments REFERENCES hr.Departments(DepartmentId),
    ManagerId       INT            NULL CONSTRAINT FK_Employees_Manager     REFERENCES hr.Employees(EmployeeId),
    StartDate       DATE           NULL,
    EndDate         DATE           NULL,
    MonthlySalary   DECIMAL(12,2)  NULL,
    IsActive        BIT            NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT (1)
);
GO

ALTER TABLE hr.Departments ADD CONSTRAINT FK_Departments_Manager
    FOREIGN KEY (ManagerEmployeeId) REFERENCES hr.Employees(EmployeeId);
GO

CREATE TABLE hr.Projects
(
    ProjectId     INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
    ProjectName   NVARCHAR(100)  NOT NULL,
    DepartmentId  INT            NULL CONSTRAINT FK_Projects_Departments REFERENCES hr.Departments(DepartmentId),
    Status        NVARCHAR(20)   NOT NULL,           -- פעיל / הסתיים / מוקפא
    StartDate     DATE           NULL,
    EndDate       DATE           NULL,
    Budget        DECIMAL(14,2)  NULL
);
GO

CREATE TABLE hr.EmployeeProjects
(
    EmployeeProjectId  INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeProjects PRIMARY KEY,
    EmployeeId         INT           NOT NULL CONSTRAINT FK_EmployeeProjects_Employees REFERENCES hr.Employees(EmployeeId),
    ProjectId          INT           NOT NULL CONSTRAINT FK_EmployeeProjects_Projects  REFERENCES hr.Projects(ProjectId),
    RoleInProject      NVARCHAR(50)  NULL,
    AllocationPercent  INT           NULL,
    AssignedDate       DATE          NULL
);
GO

/* אינדקסים על מפתחות זרים - חשובים לביצועי ה-JOIN / EXISTS שהמנוע מייצר */
CREATE INDEX IX_Employees_DepartmentId        ON hr.Employees(DepartmentId) INCLUDE (IsActive);
CREATE INDEX IX_Employees_ManagerId           ON hr.Employees(ManagerId);
CREATE INDEX IX_Projects_DepartmentId         ON hr.Projects(DepartmentId);
CREATE INDEX IX_EmployeeProjects_EmployeeId   ON hr.EmployeeProjects(EmployeeId);
CREATE INDEX IX_EmployeeProjects_ProjectId    ON hr.EmployeeProjects(ProjectId);
GO

/* ---------------------------------------------------------------------
   View עסקי: מוסיף שדות מחושבים (גיל, ותק, שם מלא).
   מנוע הבקרות עובד מול ה-View הזה דרך מילון הנתונים - כך שדות מחושבים
   מוגדרים פעם אחת ב-DB ולא בקוד.
   --------------------------------------------------------------------- */
CREATE OR ALTER VIEW hr.vw_EmployeesEnriched
AS
SELECT
    e.EmployeeId,
    e.IdNumber,
    e.FirstName,
    e.LastName,
    CONCAT(e.FirstName, N' ', e.LastName) AS FullName,
    e.BirthDate,
    CASE WHEN e.BirthDate IS NULL THEN NULL
         ELSE DATEDIFF(YEAR, e.BirthDate, CAST(GETDATE() AS DATE))
              - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, e.BirthDate, CAST(GETDATE() AS DATE)), e.BirthDate) > CAST(GETDATE() AS DATE)
                     THEN 1 ELSE 0 END
    END AS Age,
    e.Email,
    e.Phone,
    e.City,
    e.JobTitle,
    e.EmploymentType,
    e.DepartmentId,
    e.ManagerId,
    e.StartDate,
    e.EndDate,
    CASE WHEN e.StartDate IS NULL THEN NULL
         ELSE DATEDIFF(MONTH, e.StartDate, COALESCE(e.EndDate, CAST(GETDATE() AS DATE))) / 12
    END AS SeniorityYears,
    e.MonthlySalary,
    e.IsActive
FROM hr.Employees AS e;
GO
