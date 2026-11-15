/* =====================================================================
   05 - מילון הנתונים העסקי
   זהו "החוזה" בין המשתמש העסקי לבין בסיס הנתונים.
   כדי לחשוף טבלה חדשה לבקרות: מוסיפים ישות, שדות וקשרים - ללא שינוי קוד.
   (ראו גם POST /api/metadata/discover שמציע רשומות כאלה אוטומטית מתוך ה-DB)
   ===================================================================== */
USE DataControlsDb;
GO
SET NOCOUNT ON;

DELETE FROM ctl.Relations;
DELETE FROM ctl.Fields;
DELETE FROM ctl.Entities;
GO

INSERT INTO ctl.Entities (EntityKey, DisplayName, DisplayNamePlural, Description, SourceSchema, SourceObject, PrimaryKeyColumn, TitleFieldKey, Synonyms) VALUES
 ('employee',   N'עובד',             N'עובדים',             N'כל העובדים בארגון, כולל עובדים שסיימו את עבודתם', N'hr', N'vw_EmployeesEnriched', N'EmployeeId',        'fullName',       N'עובד,עובדים,עובדת,עובדות,איש צוות,אנשי צוות,מועסק,מועסקים'),
 ('department', N'מחלקה',            N'מחלקות',             N'יחידות ארגוניות',                                  N'hr', N'Departments',          N'DepartmentId',      'departmentName', N'מחלקה,מחלקות,יחידה,יחידות,אגף'),
 ('project',    N'פרויקט',           N'פרויקטים',           N'פרויקטים בארגון',                                   N'hr', N'Projects',             N'ProjectId',         'projectName',    N'פרויקט,פרויקטים,פרוייקט,פרוייקטים,משימה'),
 ('assignment', N'שיבוץ לפרויקט',    N'שיבוצים לפרויקטים',  N'שיוך של עובד לפרויקט, כולל תפקיד ואחוז משרה',     N'hr', N'EmployeeProjects',     N'EmployeeProjectId', 'roleInProject',  N'שיבוץ,שיבוצים,הקצאה,הקצאות,שיוך');
GO

;WITH f (EntityKey, FieldKey, ColumnName, DisplayName, Description, DataType, IsBusinessRequired, ShowInResults, AllowedValues, Synonyms, SortOrder) AS
(
    SELECT * FROM (VALUES
    -- עובד
    ('employee','employeeNumber', N'EmployeeId',     N'מספר עובד',        N'מזהה העובד במערכת',                 'number',  0, 1, NULL, N'מספר עובד,מזהה', 1),
    ('employee','fullName',       N'FullName',       N'שם מלא',           NULL,                                  'string',  0, 1, NULL, N'שם,שם מלא', 2),
    ('employee','firstName',      N'FirstName',      N'שם פרטי',          NULL,                                  'string',  1, 0, NULL, N'שם פרטי', 3),
    ('employee','lastName',       N'LastName',       N'שם משפחה',         NULL,                                  'string',  1, 0, NULL, N'שם משפחה', 4),
    ('employee','idNumber',       N'IdNumber',       N'תעודת זהות',       N'מספר ת"ז, 9 ספרות',                 'string',  1, 0, NULL, N'ת"ז,תז,תעודת זהות,מספר זהות', 5),
    ('employee','birthDate',      N'BirthDate',      N'תאריך לידה',       NULL,                                  'date',    1, 0, NULL, N'תאריך לידה,נולד', 6),
    ('employee','age',            N'Age',            N'גיל',              N'מחושב מתאריך הלידה',                'number',  0, 0, NULL, N'גיל,גילם,גילו,גילה,בן,בת', 7),
    ('employee','email',          N'Email',          N'דואר אלקטרוני',    NULL,                                  'string',  1, 0, NULL, N'מייל,אימייל,דוא"ל,דואל,דואר אלקטרוני', 8),
    ('employee','phone',          N'Phone',          N'טלפון',            NULL,                                  'string',  0, 0, NULL, N'טלפון,נייד,פלאפון', 9),
    ('employee','city',           N'City',           N'עיר מגורים',       NULL,                                  'string',  0, 0, NULL, N'עיר,מגורים,ישוב', 10),
    ('employee','jobTitle',       N'JobTitle',       N'תפקיד',            NULL,                                  'string',  0, 0, NULL, N'תפקיד,משרה', 11),
    ('employee','employmentType', N'EmploymentType', N'סוג העסקה',        NULL,                                  'string',  0, 0, N'["משרה מלאה","משרה חלקית","שעתי"]', N'סוג העסקה,היקף משרה', 12),
    ('employee','startDate',      N'StartDate',      N'תאריך תחילת העסקה',NULL,                                  'date',    1, 0, NULL, N'תאריך תחילה,תחילת העסקה,תחילת עבודה,התחיל,תאריך התחלה,תחילת העסקתם', 13),
    ('employee','endDate',        N'EndDate',        N'תאריך סיום העסקה', NULL,                                  'date',    0, 0, NULL, N'תאריך סיום,סיום העסקה,סיום עבודה,עזב,סיום העסקתם', 14),
    ('employee','seniorityYears', N'SeniorityYears', N'ותק (שנים)',       N'מחושב מתאריך תחילת ההעסקה',         'number',  0, 0, NULL, N'ותק,שנות ותק', 15),
    ('employee','monthlySalary',  N'MonthlySalary',  N'שכר חודשי',        N'בש"ח, ברוטו',                       'number',  1, 0, NULL, N'שכר,משכורת,משכורתם,שכרם,שכרו,שכרה,משכורתו,משכורתה,הכנסה', 16),
    ('employee','isActive',       N'IsActive',       N'עובד פעיל',        N'האם העובד מועסק כיום',              'boolean', 0, 1, NULL, N'פעיל,פעילים,פעילה,פעילות', 17),

    -- מחלקה
    ('department','departmentCode', N'DepartmentCode', N'קוד מחלקה',       NULL,                                'string',  1, 1, NULL, N'קוד מחלקה', 1),
    ('department','departmentName', N'DepartmentName', N'שם מחלקה',        NULL,                                'string',  1, 1, NULL, N'שם מחלקה', 2),
    ('department','isActive',       N'IsActive',       N'מחלקה פעילה',     NULL,                                'boolean', 0, 1, NULL, N'פעילה,פעיל,פעילות', 3),
    ('department','annualBudget',   N'AnnualBudget',   N'תקציב שנתי',      N'בש"ח',                             'number',  1, 0, NULL, N'תקציב', 4),

    -- פרויקט
    ('project','projectName', N'ProjectName', N'שם פרויקט',     NULL, 'string', 1, 1, NULL, N'שם פרויקט', 1),
    ('project','status',      N'Status',      N'סטטוס פרויקט',  NULL, 'string', 1, 1, N'["פעיל","הסתיים","מוקפא"]', N'סטטוס,מצב', 2),
    ('project','startDate',   N'StartDate',   N'תאריך התחלה',   NULL, 'date',   1, 0, NULL, N'תאריך התחלה,התחלה', 3),
    ('project','endDate',     N'EndDate',     N'תאריך סיום',    NULL, 'date',   0, 0, NULL, N'תאריך סיום,סיום', 4),
    ('project','budget',      N'Budget',      N'תקציב',         N'בש"ח', 'number', 0, 0, NULL, N'תקציב', 5),

    -- שיבוץ
    ('assignment','roleInProject',     N'RoleInProject',     N'תפקיד בפרויקט', NULL, 'string', 0, 1, NULL, N'תפקיד בפרויקט', 1),
    ('assignment','allocationPercent', N'AllocationPercent', N'אחוז שיבוץ',    N'אחוז מהמשרה שמוקדש לפרויקט', 'number', 1, 1, NULL, N'אחוז,אחוז שיבוץ,אחוז משרה,הקצאה', 2),
    ('assignment','assignedDate',      N'AssignedDate',      N'תאריך שיבוץ',   NULL, 'date',   0, 0, NULL, N'תאריך שיבוץ', 3)
    ) v (EntityKey, FieldKey, ColumnName, DisplayName, Description, DataType, IsBusinessRequired, ShowInResults, AllowedValues, Synonyms, SortOrder)
)
INSERT INTO ctl.Fields (EntityId, FieldKey, ColumnName, DisplayName, Description, DataType, IsBusinessRequired, ShowInResults, AllowedValues, Synonyms, SortOrder)
SELECT e.EntityId, f.FieldKey, f.ColumnName, f.DisplayName, f.Description, f.DataType, f.IsBusinessRequired, f.ShowInResults, f.AllowedValues, f.Synonyms, f.SortOrder
FROM f JOIN ctl.Entities e ON e.EntityKey = f.EntityKey;
GO

/* קשרים: ההצטרפות היא תמיד  To.ToColumn = From.FromColumn  */
;WITH r (FromKey, RelationKey, ToKey, FromColumn, ToColumn, Cardinality, DisplayName, Description) AS
(
    SELECT * FROM (VALUES
    ('employee',   'department',  'department', N'DepartmentId',      N'DepartmentId',      'ManyToOne', N'המחלקה של העובד',      NULL),
    ('employee',   'manager',     'employee',   N'ManagerId',         N'EmployeeId',        'ManyToOne', N'המנהל הישיר של העובד', NULL),
    ('employee',   'assignments', 'assignment', N'EmployeeId',        N'EmployeeId',        'OneToMany', N'השיבוצים של העובד לפרויקטים', NULL),
    ('department', 'employees',   'employee',   N'DepartmentId',      N'DepartmentId',      'OneToMany', N'העובדים במחלקה',       NULL),
    ('department', 'manager',     'employee',   N'ManagerEmployeeId', N'EmployeeId',        'ManyToOne', N'מנהל המחלקה',          NULL),
    ('department', 'projects',    'project',    N'DepartmentId',      N'DepartmentId',      'OneToMany', N'הפרויקטים של המחלקה',  NULL),
    ('project',    'department',  'department', N'DepartmentId',      N'DepartmentId',      'ManyToOne', N'המחלקה האחראית על הפרויקט', NULL),
    ('project',    'assignments', 'assignment', N'ProjectId',         N'ProjectId',         'OneToMany', N'השיבוצים לפרויקט',     NULL),
    ('assignment', 'employee',    'employee',   N'EmployeeId',        N'EmployeeId',        'ManyToOne', N'העובד המשובץ',         NULL),
    ('assignment', 'project',     'project',    N'ProjectId',         N'ProjectId',         'ManyToOne', N'הפרויקט',              NULL)
    ) v (FromKey, RelationKey, ToKey, FromColumn, ToColumn, Cardinality, DisplayName, Description)
)
INSERT INTO ctl.Relations (FromEntityId, RelationKey, ToEntityId, FromColumn, ToColumn, Cardinality, DisplayName, Description)
SELECT fe.EntityId, r.RelationKey, te.EntityId, r.FromColumn, r.ToColumn, r.Cardinality, r.DisplayName, r.Description
FROM r
JOIN ctl.Entities fe ON fe.EntityKey = r.FromKey
JOIN ctl.Entities te ON te.EntityKey = r.ToKey;
GO
