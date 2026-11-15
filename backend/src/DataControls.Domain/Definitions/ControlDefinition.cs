using System.Text.Json;

namespace DataControls.Domain.Definitions;

/// <summary>
/// הגדרת בקרה - מבנה נתונים טהור (AST) שאינו תלוי ב-SQL.
/// Filter מתאר את "תנאי החריגה": כל רשומה של RootEntity שמקיימת אותו היא חריגה.
/// אותו מבנה משמש את העורך הידני, את ה-AI ואת מנוע ההרצה.
/// </summary>
public sealed class ControlDefinition
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string RootEntity { get; set; } = string.Empty;
    public GroupNode Filter { get; set; } = new();
}

public enum NodeType { Group, Condition, Related }

public abstract class FilterNode
{
    /// <summary>מזהה יציב לצומת - משמש לסימון "איזה תנאי נכשל" בתוצאות.</summary>
    public string? Id { get; set; }
    public abstract NodeType Type { get; }
}

public enum LogicalOperator { And, Or }

/// <summary>קבוצת תנאים המחוברים ב"וגם" / "או". ניתן לקנן קבוצות.</summary>
public sealed class GroupNode : FilterNode
{
    public override NodeType Type => NodeType.Group;
    public LogicalOperator Logic { get; set; } = LogicalOperator.And;

    /// <summary>שלילת הקבוצה כולה ("לא מתקיים ש...").</summary>
    public bool Negate { get; set; }

    public List<FilterNode> Children { get; set; } = new();
}

/// <summary>תנאי בודד על שדה: {שדה} {אופרטור} {ערך}.</summary>
public sealed class ConditionNode : FilterNode
{
    public override NodeType Type => NodeType.Condition;

    /// <summary>נתיב עסקי לשדה, יחסית לישות הנוכחית: "age", "department.isActive".</summary>
    public string Field { get; set; } = string.Empty;

    public ComparisonOperator Operator { get; set; }
    public ValueSpec? Value { get; set; }

    /// <summary>גבול עליון - לאופרטור "בין" בלבד.</summary>
    public ValueSpec? ValueTo { get; set; }
}

public enum Quantifier
{
    /// <summary>קיימת לפחות רשומה קשורה אחת שעומדת בתנאי.</summary>
    Any,

    /// <summary>לא קיימת אף רשומה קשורה שעומדת בתנאי.</summary>
    None
}

/// <summary>
/// תנאי על רשומות קשורות (קשר אחד-לרבים), למשל "מחלקה שאין בה אף עובד פעיל".
/// התנאים הפנימיים מתייחסים לישות היעד של הקשר.
/// </summary>
public sealed class RelatedNode : FilterNode
{
    public override NodeType Type => NodeType.Related;
    public string Relation { get; set; } = string.Empty;
    public Quantifier Quantifier { get; set; } = Quantifier.Any;
    public GroupNode? Filter { get; set; }
}

public enum ValueKind
{
    /// <summary>ערך קבוע שהמשתמש הזין.</summary>
    Constant,

    /// <summary>רשימת ערכים (לאופרטורים "אחד מ" / "אינו אחד מ").</summary>
    List,

    /// <summary>השוואה לשדה אחר באותה רשומה (תאריך סיום &lt; תאריך התחלה).</summary>
    Field,

    /// <summary>התאריך של היום (+/- ימים) - מחושב בזמן ההרצה.</summary>
    Today
}

public sealed class ValueSpec
{
    public ValueKind Kind { get; set; } = ValueKind.Constant;
    public JsonElement? Value { get; set; }
    public List<JsonElement>? Values { get; set; }
    public string? Field { get; set; }
    public int? OffsetDays { get; set; }

    public static ValueSpec Constant(object? value) => new()
    {
        Kind = ValueKind.Constant,
        Value = JsonSerializer.SerializeToElement(value)
    };

    public static ValueSpec OfField(string field) => new() { Kind = ValueKind.Field, Field = field };
    public static ValueSpec TodayPlus(int days = 0) => new() { Kind = ValueKind.Today, OffsetDays = days };

    public static ValueSpec ListOf(params object[] values) => new()
    {
        Kind = ValueKind.List,
        Values = values.Select(v => JsonSerializer.SerializeToElement(v)).ToList()
    };
}
