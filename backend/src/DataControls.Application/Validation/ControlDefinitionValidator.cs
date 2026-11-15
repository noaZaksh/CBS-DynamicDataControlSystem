using System.Text.RegularExpressions;
using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Validation;

public sealed record ValidationError(string? NodeId, string Message);

public sealed class ValidationResult
{
    public List<ValidationError> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// בודק שהגדרת בקרה תקינה מול מילון הנתונים הנוכחי.
/// זו "החומה" שלפני מנוע ה-SQL: שום הגדרה (ידנית או מה-AI) לא מגיעה לשלב התרגום בלי לעבור כאן.
/// </summary>
public sealed partial class ControlDefinitionValidator
{
    public const int MaxNodes = 60;
    public const int MaxDepth = 6;
    public const int MaxListItems = 200;
    public const int MaxStringLength = 400;

    [GeneratedRegex("^[A-Za-z0-9_]{1,30}$")]
    private static partial Regex NodeIdRegex();

    public ValidationResult Validate(ControlDefinition? definition, DataModel model)
    {
        var result = new ValidationResult();

        if (definition is null)
        {
            result.Errors.Add(new(null, "לא התקבלה הגדרת בקרה"));
            return result;
        }

        if (definition.SchemaVersion != ControlDefinition.CurrentSchemaVersion)
            result.Errors.Add(new(null, $"גרסת הגדרה לא נתמכת ({definition.SchemaVersion})"));

        if (string.IsNullOrWhiteSpace(definition.RootEntity) || !model.TryGetEntity(definition.RootEntity, out var root))
        {
            result.Errors.Add(new(null, "יש לבחור על אילו נתונים תתבצע הבקרה"));
            return result;
        }

        if (definition.Filter is null)
        {
            result.Errors.Add(new(null, "יש להגדיר לפחות תנאי אחד"));
            return result;
        }

        var ctx = new Context(model, result);
        ValidateNode(definition.Filter, root, depth: 1, ctx);

        if (ctx.ConditionCount == 0)
            result.Errors.Add(new(definition.Filter.Id, "יש להגדיר לפחות תנאי אחד"));
        if (ctx.NodeCount > MaxNodes)
            result.Errors.Add(new(null, $"הבקרה מורכבת מדי (מעל {MaxNodes} תנאים)"));

        return result;
    }

    private sealed class Context(DataModel model, ValidationResult result)
    {
        public DataModel Model { get; } = model;
        public ValidationResult Result { get; } = result;
        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);
        public int NodeCount { get; set; }
        public int ConditionCount { get; set; }
        public void Error(FilterNode node, string message) => Result.Errors.Add(new(node.Id, message));
    }

    private static void ValidateNode(FilterNode node, EntityMeta entity, int depth, Context ctx)
    {
        ctx.NodeCount++;

        if (node.Id is null || !NodeIdRegex().IsMatch(node.Id))
            ctx.Error(node, "מזהה צומת לא תקין");
        else if (!ctx.Ids.Add(node.Id))
            ctx.Error(node, $"מזהה צומת כפול '{node.Id}'");

        if (depth > MaxDepth)
        {
            ctx.Error(node, $"קינון עמוק מדי (מעל {MaxDepth} רמות)");
            return;
        }

        switch (node)
        {
            case GroupNode group:
                if (group.Children is null || group.Children.Count == 0)
                {
                    ctx.Error(group, "קבוצת תנאים ריקה");
                    return;
                }
                foreach (var child in group.Children)
                    ValidateNode(child, entity, depth + 1, ctx);
                break;

            case ConditionNode condition:
                ctx.ConditionCount++;
                ValidateCondition(condition, entity, ctx);
                break;

            case RelatedNode related:
                ctx.ConditionCount++;
                if (!entity.Relations.TryGetValue(related.Relation ?? string.Empty, out var relation))
                {
                    ctx.Error(related, $"הקשר '{related.Relation}' אינו מוגדר עבור {entity.DisplayName}");
                    return;
                }
                if (!ctx.Model.TryGetEntity(relation.ToEntityKey, out var target))
                {
                    ctx.Error(related, $"הישות '{relation.ToEntityKey}' אינה מוגדרת");
                    return;
                }
                if (related.Filter is not null)
                {
                    if (related.Filter.Children.Count == 0)
                        ctx.Error(related, "יש להגדיר תנאי על הרשומות הקשורות, או להסיר את קבוצת התנאים");
                    else
                        ValidateNode(related.Filter, target, depth + 1, ctx);
                }
                break;

            default:
                ctx.Error(node, "סוג צומת לא נתמך");
                break;
        }
    }

    private static void ValidateCondition(ConditionNode c, EntityMeta entity, Context ctx)
    {
        if (!FieldPathResolver.TryResolve(ctx.Model, entity, c.Field, out var field, out var pathError))
        {
            ctx.Error(c, pathError!);
            return;
        }

        if (!Enum.IsDefined(c.Operator))
        {
            ctx.Error(c, "אופרטור לא נתמך");
            return;
        }

        var type = field!.Field.DataType;
        var op = OperatorCatalog.Get(c.Operator);
        var label = field.DisplayLabel;

        if (!op.AppliesTo.Contains(type))
        {
            ctx.Error(c, $"לא ניתן להשתמש ב\"{op.LabelFor(type)}\" עבור השדה \"{label}\"");
            return;
        }

        switch (op.Arity)
        {
            case OperandArity.None:
                break;

            case OperandArity.Single:
                ValidateSingleValue(c, c.Value, entity, type, op, label, ctx);
                break;

            case OperandArity.Range:
                ValidateSingleValue(c, c.Value, entity, type, op, label, ctx, allowField: false);
                ValidateSingleValue(c, c.ValueTo, entity, type, op, label, ctx, allowField: false);
                break;

            case OperandArity.List:
                if (c.Value is null || c.Value.Kind != ValueKind.List || c.Value.Values is null || c.Value.Values.Count == 0)
                {
                    ctx.Error(c, $"יש להזין לפחות ערך אחד עבור \"{label}\"");
                    return;
                }
                if (c.Value.Values.Count > MaxListItems)
                {
                    ctx.Error(c, $"ניתן להזין עד {MaxListItems} ערכים");
                    return;
                }
                foreach (var v in c.Value.Values)
                {
                    if (!ValueConverter.TryConvert(v, type, out var converted, out var err))
                        ctx.Error(c, $"\"{label}\": {err}");
                    else
                        CheckStringLength(c, converted, label, ctx);
                }
                break;
        }
    }

    private static void ValidateSingleValue(
        ConditionNode c, ValueSpec? value, EntityMeta entity, FieldDataType type,
        OperatorInfo op, string label, Context ctx, bool allowField = true)
    {
        if (value is null)
        {
            ctx.Error(c, $"יש להזין ערך להשוואה עבור \"{label}\"");
            return;
        }

        switch (value.Kind)
        {
            case ValueKind.Constant:
                if (!ValueConverter.TryConvert(value.Value, type, out var converted, out var err))
                    ctx.Error(c, $"\"{label}\": {err}");
                else
                    CheckStringLength(c, converted, label, ctx);
                break;

            case ValueKind.Field:
                if (!allowField || !op.AllowsFieldComparison)
                {
                    ctx.Error(c, $"לא ניתן להשוות את \"{label}\" לשדה אחר עם האופרטור \"{op.LabelFor(type)}\"");
                    return;
                }
                if (!FieldPathResolver.TryResolve(ctx.Model, entity, value.Field, out var other, out var pathError))
                {
                    ctx.Error(c, pathError!);
                    return;
                }
                if (other!.Field.DataType != type)
                    ctx.Error(c, $"לא ניתן להשוות \"{label}\" ל\"{other.DisplayLabel}\" - סוגי נתונים שונים");
                break;

            case ValueKind.Today:
                if (type != FieldDataType.Date)
                    ctx.Error(c, $"\"היום\" מתאים רק לשדות תאריך");
                else if (value.OffsetDays is < -36500 or > 36500)
                    ctx.Error(c, "היסט ימים לא סביר");
                break;

            default:
                ctx.Error(c, $"סוג ערך לא מתאים לאופרטור \"{op.LabelFor(type)}\"");
                break;
        }
    }

    private static void CheckStringLength(ConditionNode c, object? value, string label, Context ctx)
    {
        if (value is string s && s.Length > MaxStringLength)
            ctx.Error(c, $"הערך עבור \"{label}\" ארוך מדי");
    }
}
