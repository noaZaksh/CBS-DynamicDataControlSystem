using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;

namespace DataControls.Tests;

/// <summary>מילון נתונים לבדיקות - משקף את המבנה ב-database/05_metadata_seed.sql.</summary>
internal static class TestModel
{
    public static DataModel Create()
    {
        static Dictionary<string, FieldMeta> Fields(params FieldMeta[] fields) =>
            fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

        static FieldMeta F(string key, string column, string name, FieldDataType type, bool show = false) =>
            new() { Key = key, ColumnName = column, DisplayName = name, DataType = type, ShowInResults = show };

        static Dictionary<string, RelationMeta> Rels(params RelationMeta[] rels) =>
            rels.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);

        static RelationMeta R(string from, string key, string to, string fromCol, string toCol, RelationCardinality card, string name) =>
            new() { FromEntityKey = from, Key = key, ToEntityKey = to, FromColumn = fromCol, ToColumn = toCol, Cardinality = card, DisplayName = name };

        var employee = new EntityMeta
        {
            Key = "employee", DisplayName = "עובד", DisplayNamePlural = "עובדים",
            SourceSchema = "hr", SourceObject = "vw_EmployeesEnriched", PrimaryKeyColumn = "EmployeeId",
            TitleFieldKey = "fullName",
            Fields = Fields(
                F("fullName", "FullName", "שם מלא", FieldDataType.String, show: true),
                F("age", "Age", "גיל", FieldDataType.Number),
                F("monthlySalary", "MonthlySalary", "שכר חודשי", FieldDataType.Number),
                F("email", "Email", "דואר אלקטרוני", FieldDataType.String),
                F("startDate", "StartDate", "תאריך תחילת העסקה", FieldDataType.Date),
                F("endDate", "EndDate", "תאריך סיום העסקה", FieldDataType.Date),
                F("isActive", "IsActive", "עובד פעיל", FieldDataType.Boolean, show: true)),
            Relations = Rels(
                R("employee", "department", "department", "DepartmentId", "DepartmentId", RelationCardinality.ManyToOne, "המחלקה של העובד"),
                R("employee", "manager", "employee", "ManagerId", "EmployeeId", RelationCardinality.ManyToOne, "המנהל הישיר"),
                R("employee", "assignments", "assignment", "EmployeeId", "EmployeeId", RelationCardinality.OneToMany, "השיבוצים של העובד"))
        };

        var department = new EntityMeta
        {
            Key = "department", DisplayName = "מחלקה", DisplayNamePlural = "מחלקות",
            SourceSchema = "hr", SourceObject = "Departments", PrimaryKeyColumn = "DepartmentId",
            TitleFieldKey = "departmentName",
            Fields = Fields(
                F("departmentName", "DepartmentName", "שם מחלקה", FieldDataType.String, show: true),
                F("isActive", "IsActive", "מחלקה פעילה", FieldDataType.Boolean, show: true)),
            Relations = Rels(
                R("department", "employees", "employee", "DepartmentId", "DepartmentId", RelationCardinality.OneToMany, "העובדים במחלקה"),
                R("department", "manager", "employee", "ManagerEmployeeId", "EmployeeId", RelationCardinality.ManyToOne, "מנהל המחלקה"))
        };

        var project = new EntityMeta
        {
            Key = "project", DisplayName = "פרויקט", DisplayNamePlural = "פרויקטים",
            SourceSchema = "hr", SourceObject = "Projects", PrimaryKeyColumn = "ProjectId",
            Fields = Fields(F("status", "Status", "סטטוס פרויקט", FieldDataType.String))
        };

        var assignment = new EntityMeta
        {
            Key = "assignment", DisplayName = "שיבוץ", DisplayNamePlural = "שיבוצים",
            SourceSchema = "hr", SourceObject = "EmployeeProjects", PrimaryKeyColumn = "EmployeeProjectId",
            Fields = Fields(F("allocationPercent", "AllocationPercent", "אחוז שיבוץ", FieldDataType.Number)),
            Relations = Rels(R("assignment", "project", "project", "ProjectId", "ProjectId", RelationCardinality.ManyToOne, "הפרויקט"))
        };

        return new DataModel(new[] { employee, department, project, assignment });
    }

    public static ConditionNode Cond(string id, string field, ComparisonOperator op, ValueSpec? value = null, ValueSpec? valueTo = null) =>
        new() { Id = id, Field = field, Operator = op, Value = value, ValueTo = valueTo };

    public static ControlDefinition Def(string root, LogicalOperator logic, params FilterNode[] children) => new()
    {
        RootEntity = root,
        Filter = new GroupNode { Id = "g1", Logic = logic, Children = children.ToList() }
    };
}
