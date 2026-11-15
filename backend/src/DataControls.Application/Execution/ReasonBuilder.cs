using DataControls.Application.Describing;
using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Execution;

/// <summary>
/// בונה "סיבת חריגה" לכל רשומה: אילו תנאים בפועל התקיימו, יחד עם הערכים בפועל.
/// מתבסס על עמודות-דגל (CASE WHEN ... THEN 1) שהמתרגם מוסיף לכל צומת ברמת הישות הראשית.
/// </summary>
public sealed class ReasonBuilder
{
    private readonly DefinitionDescriber _describer;

    public ReasonBuilder(DefinitionDescriber describer) => _describer = describer;

    public (List<string> Reasons, List<string> MatchedNodeIds) Explain(
        ControlDefinition definition, DataModel model, CompiledQuery query, IReadOnlyDictionary<string, object?> row)
    {
        var reasons = new List<string>();
        var matched = new List<string>();
        var root = model.GetEntity(definition.RootEntity);
        Visit(definition.Filter, root, model, query, row, reasons, matched);
        return (reasons, matched);
    }

    private void Visit(FilterNode node, EntityMeta entity, DataModel model, CompiledQuery query,
        IReadOnlyDictionary<string, object?> row, List<string> reasons, List<string> matched)
    {
        if (!IsTrue(node, query, row))
            return;

        matched.Add(node.Id!);

        switch (node)
        {
            case GroupNode { Negate: true } g:
                reasons.Add($"לא מתקיים: {_describer.DescribeNode(new GroupNode { Logic = g.Logic, Children = g.Children }, entity, model)}");
                break;

            case GroupNode g:
                foreach (var child in g.Children)
                    Visit(child, entity, model, query, row, reasons, matched);
                break;

            case ConditionNode c:
                var actual = Value(query.AliasForField(c.Field), row);
                var compare = c.Value?.Kind == ValueKind.Field ? Value(query.AliasForField(c.Value.Field!), row) : null;
                reasons.Add(_describer.ExplainCondition(c, entity, model, actual, compare));
                break;

            case RelatedNode r:
                reasons.Add(_describer.DescribeNode(r, entity, model));
                break;
        }
    }

    private static bool IsTrue(FilterNode node, CompiledQuery query, IReadOnlyDictionary<string, object?> row)
    {
        var alias = query.AliasForNode(node.Id!);
        if (alias is null || !row.TryGetValue(alias, out var flag) || flag is null)
            return false;
        return Convert.ToInt32(flag) == 1;
    }

    private static object? Value(string? alias, IReadOnlyDictionary<string, object?> row) =>
        alias is not null && row.TryGetValue(alias, out var v) ? v : null;
}
