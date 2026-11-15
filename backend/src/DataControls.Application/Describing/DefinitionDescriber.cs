using System.Globalization;
using System.Text.Json;
using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Describing;

/// <summary>
/// מתרגם הגדרת בקרה למשפט בעברית ("עובדים שבהם: גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000").
/// משמש לתצוגת סיכום בעורך, לאישור הצעת AI, ולהסבר "למה הרשומה חריגה".
/// </summary>
public sealed class DefinitionDescriber
{
    // פורמט מספרים ניטרלי: he-IL מוסיף תו כיווניות (LRM) לפני סימן מינוס
    private static readonly CultureInfo He = CultureInfo.InvariantCulture;

    public string Describe(ControlDefinition definition, DataModel model)
    {
        if (!model.TryGetEntity(definition.RootEntity, out var root))
            return string.Empty;
        return $"{root.DisplayNamePlural} שבהם: {DescribeNode(definition.Filter, root, model, topLevel: true)}";
    }

    public string DescribeNode(FilterNode node, EntityMeta entity, DataModel model, bool topLevel = false) => node switch
    {
        GroupNode g => DescribeGroup(g, entity, model, topLevel),
        ConditionNode c => DescribeCondition(c, entity, model),
        RelatedNode r => DescribeRelated(r, entity, model),
        _ => string.Empty
    };

    private string DescribeGroup(GroupNode g, EntityMeta entity, DataModel model, bool topLevel)
    {
        var joiner = g.Logic == LogicalOperator.And ? " וגם " : " או ";
        var parts = g.Children.Select(c => DescribeNode(c, entity, model)).Where(s => s.Length > 0).ToList();
        var body = string.Join(joiner, parts);
        if (g.Negate) return $"לא מתקיים ש({body})";
        return topLevel || parts.Count <= 1 ? body : $"({body})";
    }

    private string DescribeRelated(RelatedNode r, EntityMeta entity, DataModel model)
    {
        if (!entity.Relations.TryGetValue(r.Relation, out var rel) || !model.TryGetEntity(rel.ToEntityKey, out var target))
            return r.Relation;

        var hasFilter = r.Filter is { Children.Count: > 0 };
        var inner = hasFilter ? DescribeGroup(r.Filter!, target, model, topLevel: true) : null;

        return (r.Quantifier, hasFilter) switch
        {
            (Quantifier.Any, true) => $"קיימת לפחות רשומה אחת ב\"{rel.DisplayName}\" שבה {inner}",
            (Quantifier.None, true) => $"אין אף רשומה ב\"{rel.DisplayName}\" שבה {inner}",
            (Quantifier.Any, false) => $"קיימת לפחות רשומה אחת ב\"{rel.DisplayName}\"",
            _ => $"אין רשומות ב\"{rel.DisplayName}\""
        };
    }

    public string DescribeCondition(ConditionNode c, EntityMeta entity, DataModel model)
    {
        if (!FieldPathResolver.TryResolve(model, entity, c.Field, out var field, out _))
            return c.Field;

        var type = field!.Field.DataType;
        var op = OperatorCatalog.Get(c.Operator);
        var label = field.DisplayLabel;

        return op.Arity switch
        {
            OperandArity.None => $"{label} {op.LabelFor(type)}",
            OperandArity.Range => $"{label} בין {DescribeValue(c.Value, type, entity, model)} ל-{DescribeValue(c.ValueTo, type, entity, model)}",
            _ when type == FieldDataType.Boolean && c.Value?.Kind == ValueKind.Constant && c.Operator is ComparisonOperator.Eq or ComparisonOperator.Neq
                => DescribeBoolean(label, c),
            _ => $"{label} {Attach(op.LabelFor(type), DescribeValue(c.Value, type, entity, model))}"
        };
    }

    /// <summary>הסבר לרשומה ספציפית: מוסיף את הערך בפועל של השדה (ושל שדה ההשוואה, אם יש).</summary>
    public string ExplainCondition(ConditionNode c, EntityMeta entity, DataModel model, object? actual, object? compareActual)
    {
        if (!FieldPathResolver.TryResolve(model, entity, c.Field, out var field, out _))
            return c.Field;

        var type = field!.Field.DataType;
        var op = OperatorCatalog.Get(c.Operator);
        var label = field.DisplayLabel;
        var actualText = FormatValue(actual, type);

        if (op.Arity == OperandArity.None)
            return c.Operator == ComparisonOperator.IsEmpty ? $"{label}: חסר" : $"{label}: {actualText}";

        if (type == FieldDataType.Boolean && op.Arity == OperandArity.Single)
            return $"{label}: {actualText}";

        string expected;
        if (c.Value?.Kind == ValueKind.Field &&
            FieldPathResolver.TryResolve(model, entity, c.Value.Field, out var other, out _))
            expected = $"{other!.DisplayLabel} ({FormatValue(compareActual, type)})";
        else if (op.Arity == OperandArity.Range)
            expected = $"{DescribeValue(c.Value, type, entity, model)} ל-{DescribeValue(c.ValueTo, type, entity, model)}";
        else
            expected = DescribeValue(c.Value, type, entity, model);

        var opText = op.Arity == OperandArity.Range ? $"בין {expected}" : Attach(op.LabelFor(type), expected);
        return $"{label}: {actualText} ({opText})";
    }

    private static string DescribeBoolean(string label, ConditionNode c)
    {
        var value = ValueConverter.TryConvert(c.Value!.Value, FieldDataType.Boolean, out var v, out _) && v is true;
        if (c.Operator == ComparisonOperator.Neq) value = !value;
        return value ? $"{label} = כן" : $"{label} = לא";
    }

    private string DescribeValue(ValueSpec? value, FieldDataType type, EntityMeta entity, DataModel model)
    {
        if (value is null) return "?";
        switch (value.Kind)
        {
            case ValueKind.Field:
                return FieldPathResolver.TryResolve(model, entity, value.Field, out var f, out _) ? f!.DisplayLabel : value.Field ?? "?";
            case ValueKind.Today:
                var days = value.OffsetDays ?? 0;
                return days switch
                {
                    0 => "היום",
                    > 0 => $"{days} ימים מהיום",
                    _ => $"{-days} ימים לפני היום"
                };
            case ValueKind.List:
                var items = (value.Values ?? new List<JsonElement>())
                    .Select(v => ValueConverter.TryConvert(v, type, out var o, out _) ? Quote(FormatValue(o, type), type) : v.ToString());
                return $"({string.Join(", ", items)})";
            default:
                return ValueConverter.TryConvert(value.Value, type, out var obj, out _)
                    ? Quote(FormatValue(obj, type), type)
                    : value.Value?.ToString() ?? "?";
        }
    }

    private static string Quote(string text, FieldDataType type) => type == FieldDataType.String ? $"\"{text}\"" : text;

    /// <summary>מחבר מילת יחס בת אות אחת לערך: "קטן מ" + "18" → "קטן מ-18".</summary>
    private static string Attach(string opLabel, string value)
    {
        var lastSpace = opLabel.LastIndexOf(' ');
        var lastWord = lastSpace >= 0 ? opLabel[(lastSpace + 1)..] : opLabel;
        if (lastWord.Length != 1) return $"{opLabel} {value}";
        // בעברית אות יחס נצמדת למילה ("מוקדם מהיום"), ומקף מפריד לפני מספר או ציטוט ("קטן מ-18")
        return value.Length > 0 && char.IsLetter(value[0]) ? $"{opLabel}{value}" : $"{opLabel}-{value}";
    }

    public static string FormatValue(object? value, FieldDataType type)
    {
        if (value is null or DBNull) return "חסר";
        return value switch
        {
            bool b => b ? "כן" : "לא",
            DateTime dt => dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            decimal m => m.ToString(m == decimal.Truncate(m) ? "#,0" : "#,0.##", He),
            double db => db.ToString("#,0.##", He),
            int or long or short => Convert.ToInt64(value).ToString("#,0", He),
            string s when string.IsNullOrWhiteSpace(s) => "חסר",
            _ => value.ToString() ?? string.Empty
        };
    }
}
