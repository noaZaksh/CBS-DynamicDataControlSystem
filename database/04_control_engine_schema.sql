/* =====================================================================
   04 - סכמת מנוע הבקרות (ctl)

   שכבה סמנטית (מילון נתונים עסקי):
     ctl.Entities   - "ישויות עסקיות" (עובד, מחלקה...) ממופות לטבלה/View
     ctl.Fields     - שדות עסקיים עם שם בעברית, סוג נתון ומילים נרדפות
     ctl.Relations  - קשרים עסקיים בין ישויות ("המחלקה של העובד")

   הגדרות והרצות:
     ctl.Controls     - הגדרת בקרה כ-JSON מובנה (עץ תנאים), לא SQL
     ctl.ControlRuns  - היסטוריית הרצות

   המנוע מתרגם שמות עסקיים לשמות טכניים *רק* דרך הטבלאות האלה,
   ולכן הוספת טבלה/שדה חדש לבקרות = הוספת שורות כאן, ללא שינוי קוד.
   ===================================================================== */
USE DataControlsDb;
GO

IF OBJECT_ID(N'ctl.ControlRuns', N'U') IS NOT NULL DROP TABLE ctl.ControlRuns;
IF OBJECT_ID(N'ctl.Controls',    N'U') IS NOT NULL DROP TABLE ctl.Controls;
IF OBJECT_ID(N'ctl.Relations',   N'U') IS NOT NULL DROP TABLE ctl.Relations;
IF OBJECT_ID(N'ctl.Fields',      N'U') IS NOT NULL DROP TABLE ctl.Fields;
IF OBJECT_ID(N'ctl.Entities',    N'U') IS NOT NULL DROP TABLE ctl.Entities;
GO

CREATE TABLE ctl.Entities
(
    EntityId           INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Entities PRIMARY KEY,
    EntityKey          VARCHAR(50)    NOT NULL CONSTRAINT UQ_Entities_Key UNIQUE,   -- מזהה יציב שבו משתמשות הגדרות הבקרות
    DisplayName        NVARCHAR(100)  NOT NULL,                                       -- "עובד"
    DisplayNamePlural  NVARCHAR(100)  NOT NULL,                                       -- "עובדים"
    Description        NVARCHAR(500)  NULL,
    SourceSchema       SYSNAME        NOT NULL,
    SourceObject       SYSNAME        NOT NULL,                                       -- טבלה או View
    PrimaryKeyColumn   SYSNAME        NOT NULL,
    TitleFieldKey      VARCHAR(50)    NULL,                                           -- השדה שמזהה רשומה בעיני המשתמש (שם מלא...)
    Synonyms           NVARCHAR(400)  NULL,                                           -- מילים נרדפות, מופרדות בפסיק (לשימוש ה-AI)
    IsActive           BIT            NOT NULL CONSTRAINT DF_Entities_IsActive DEFAULT (1),
    CONSTRAINT CK_Entities_Key CHECK (EntityKey NOT LIKE '%[^a-zA-Z0-9_]%')
);
GO

CREATE TABLE ctl.Fields
(
    FieldId              INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Fields PRIMARY KEY,
    EntityId             INT            NOT NULL CONSTRAINT FK_Fields_Entities REFERENCES ctl.Entities(EntityId),
    FieldKey             VARCHAR(50)    NOT NULL,
    ColumnName           SYSNAME        NOT NULL,
    DisplayName          NVARCHAR(100)  NOT NULL,
    Description          NVARCHAR(500)  NULL,
    DataType             VARCHAR(20)    NOT NULL,   -- string | number | date | boolean
    IsBusinessRequired   BIT            NOT NULL CONSTRAINT DF_Fields_IsBusinessRequired DEFAULT (0),  -- שדה חובה לפי כללים עסקיים
    ShowInResults        BIT            NOT NULL CONSTRAINT DF_Fields_ShowInResults DEFAULT (0),       -- מוצג תמיד בתוצאות
    AllowedValues        NVARCHAR(1000) NULL,       -- JSON array לערכים סגורים (לבחירה מרשימה)
    Synonyms             NVARCHAR(400)  NULL,
    SortOrder            INT            NOT NULL CONSTRAINT DF_Fields_SortOrder DEFAULT (100),
    IsActive             BIT            NOT NULL CONSTRAINT DF_Fields_IsActive DEFAULT (1),
    CONSTRAINT UQ_Fields_EntityKey UNIQUE (EntityId, FieldKey),
    CONSTRAINT CK_Fields_Key CHECK (FieldKey NOT LIKE '%[^a-zA-Z0-9_]%'),
    CONSTRAINT CK_Fields_DataType CHECK (DataType IN ('string', 'number', 'date', 'boolean')),
    CONSTRAINT CK_Fields_AllowedValues CHECK (AllowedValues IS NULL OR ISJSON(AllowedValues) = 1)
);
GO

CREATE TABLE ctl.Relations
(
    RelationId      INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_Relations PRIMARY KEY,
    FromEntityId    INT            NOT NULL CONSTRAINT FK_Relations_From REFERENCES ctl.Entities(EntityId),
    RelationKey     VARCHAR(50)    NOT NULL,      -- ייחודי בתוך ישות המקור ("department")
    ToEntityId      INT            NOT NULL CONSTRAINT FK_Relations_To   REFERENCES ctl.Entities(EntityId),
    FromColumn      SYSNAME        NOT NULL,
    ToColumn        SYSNAME        NOT NULL,
    Cardinality     VARCHAR(20)    NOT NULL,      -- ManyToOne (לכל עובד מחלקה אחת) | OneToMany (למחלקה הרבה עובדים)
    DisplayName     NVARCHAR(100)  NOT NULL,      -- "המחלקה של העובד"
    Description     NVARCHAR(500)  NULL,
    IsActive        BIT            NOT NULL CONSTRAINT DF_Relations_IsActive DEFAULT (1),
    CONSTRAINT UQ_Relations_Key UNIQUE (FromEntityId, RelationKey),
    CONSTRAINT CK_Relations_Key CHECK (RelationKey NOT LIKE '%[^a-zA-Z0-9_]%'),
    CONSTRAINT CK_Relations_Cardinality CHECK (Cardinality IN ('ManyToOne', 'OneToMany'))
);
GO

CREATE TABLE ctl.Controls
(
    ControlId       INT             IDENTITY(1,1) NOT NULL CONSTRAINT PK_Controls PRIMARY KEY,
    Name            NVARCHAR(150)   NOT NULL,
    Description     NVARCHAR(1000)  NULL,
    Category        NVARCHAR(50)    NULL,
    Severity        VARCHAR(10)     NOT NULL CONSTRAINT DF_Controls_Severity DEFAULT ('Medium'),
    RootEntityKey   VARCHAR(50)     NOT NULL,
    DefinitionJson  NVARCHAR(MAX)   NOT NULL,
    Source          VARCHAR(10)     NOT NULL CONSTRAINT DF_Controls_Source DEFAULT ('Manual'),  -- Manual | AI
    IsActive        BIT             NOT NULL CONSTRAINT DF_Controls_IsActive DEFAULT (1),
    Version         INT             NOT NULL CONSTRAINT DF_Controls_Version DEFAULT (1),
    CreatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_Controls_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_Controls_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Controls_Json     CHECK (ISJSON(DefinitionJson) = 1),
    CONSTRAINT CK_Controls_Severity CHECK (Severity IN ('Low', 'Medium', 'High')),
    CONSTRAINT CK_Controls_Source   CHECK (Source IN ('Manual', 'AI'))
);
GO

CREATE TABLE ctl.ControlRuns
(
    RunId           BIGINT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_ControlRuns PRIMARY KEY,
    ControlId       INT             NOT NULL CONSTRAINT FK_ControlRuns_Controls REFERENCES ctl.Controls(ControlId) ON DELETE CASCADE,
    ControlVersion  INT             NOT NULL,
    StartedAt       DATETIME2(3)    NOT NULL,
    DurationMs      INT             NOT NULL,
    Status          VARCHAR(10)     NOT NULL,     -- Success | Failed
    TotalRecords    INT             NULL,         -- כמה רשומות נבדקו
    ExceptionCount  INT             NULL,         -- כמה חריגות נמצאו
    ErrorMessage    NVARCHAR(1000)  NULL,
    CONSTRAINT CK_ControlRuns_Status CHECK (Status IN ('Success', 'Failed'))
);
GO

CREATE INDEX IX_ControlRuns_Control ON ctl.ControlRuns(ControlId, StartedAt DESC) INCLUDE (Status, ExceptionCount, TotalRecords);
GO
