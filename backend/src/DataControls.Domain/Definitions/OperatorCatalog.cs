using DataControls.Domain.Metadata;

namespace DataControls.Domain.Definitions;

public enum ComparisonOperator
{
    Eq,
    Neq,
    Gt,
    Gte,
    Lt,
    Lte,
    Between,
    In,
    NotIn,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    IsEmpty,
    IsNotEmpty
}

/// <summary>כמה ערכים האופרטור צריך.</summary>
public enum OperandArity { None, Single, Range, List }

public sealed record OperatorInfo(
    ComparisonOperator Operator,
    string Label,
    OperandArity Arity,
    IReadOnlyList<FieldDataType> AppliesTo,
    bool AllowsFieldComparison,
    string? DateLabel = null)
{
    /// <summary>תווית מותאמת לסוג הנתון ("מוקדם מ" לתאריכים במקום "קטן מ").</summary>
    public string LabelFor(FieldDataType type) =>
        type == FieldDataType.Date && DateLabel is not null ? DateLabel : Label;
}

/// <summary>
/// קטלוג האופרטורים - מקור אמת יחיד לאופרטורים, התוויות בעברית והתאמתם לסוגי נתונים.
/// נחשף גם ל-Frontend (GET /api/metadata/operators) כך שהממשק נבנה ממנו באופן גנרי.
/// </summary>
public static class OperatorCatalog
{
    private static readonly FieldDataType[] All = Enum.GetValues<FieldDataType>();
    private static readonly FieldDataType[] Ordered = { FieldDataType.Number, FieldDataType.Date, FieldDataType.String };
    private static readonly FieldDataType[] Text = { FieldDataType.String };
    private static readonly FieldDataType[] Listable = { FieldDataType.String, FieldDataType.Number };

    public static readonly IReadOnlyList<OperatorInfo> Operators = new[]
    {
        new OperatorInfo(ComparisonOperator.Eq,          "שווה ל",          OperandArity.Single, All,      true),
        new OperatorInfo(ComparisonOperator.Neq,         "שונה מ",          OperandArity.Single, All,      true),
        new OperatorInfo(ComparisonOperator.Gt,          "גדול מ",          OperandArity.Single, Ordered,  true, "מאוחר מ"),
        new OperatorInfo(ComparisonOperator.Gte,         "גדול או שווה ל",  OperandArity.Single, Ordered,  true, "מאוחר או שווה ל"),
        new OperatorInfo(ComparisonOperator.Lt,          "קטן מ",           OperandArity.Single, Ordered,  true, "מוקדם מ"),
        new OperatorInfo(ComparisonOperator.Lte,         "קטן או שווה ל",   OperandArity.Single, Ordered,  true, "מוקדם או שווה ל"),
        new OperatorInfo(ComparisonOperator.Between,     "בין",             OperandArity.Range,  new[] { FieldDataType.Number, FieldDataType.Date }, false),
        new OperatorInfo(ComparisonOperator.In,          "אחד מ",           OperandArity.List,   Listable, false),
        new OperatorInfo(ComparisonOperator.NotIn,       "אינו אחד מ",      OperandArity.List,   Listable, false),
        new OperatorInfo(ComparisonOperator.Contains,    "מכיל",            OperandArity.Single, Text,     false),
        new OperatorInfo(ComparisonOperator.NotContains, "אינו מכיל",       OperandArity.Single, Text,     false),
        new OperatorInfo(ComparisonOperator.StartsWith,  "מתחיל ב",         OperandArity.Single, Text,     false),
        new OperatorInfo(ComparisonOperator.EndsWith,    "מסתיים ב",        OperandArity.Single, Text,     false),
        new OperatorInfo(ComparisonOperator.IsEmpty,     "ריק / חסר",       OperandArity.None,   All,      false),
        new OperatorInfo(ComparisonOperator.IsNotEmpty,  "מלא (לא ריק)",    OperandArity.None,   All,      false),
    };

    private static readonly Dictionary<ComparisonOperator, OperatorInfo> ByOperator =
        Operators.ToDictionary(o => o.Operator);

    public static OperatorInfo Get(ComparisonOperator op) => ByOperator[op];

    public static bool IsApplicable(ComparisonOperator op, FieldDataType type) =>
        ByOperator[op].AppliesTo.Contains(type);
}
