using System.Text.RegularExpressions;
using DataControls.Domain.Definitions;

namespace DataControls.Application.Validation;

/// <summary>
/// מנרמל הגדרה לפני ולידציה/שמירה: מקצה מזהי צמתים חסרים/כפולים ומנקה רווחים.
/// כך גם הגדרה שהגיעה מה-AI או מלקוח חיצוני תקבל מזהים יציבים.
/// </summary>
public static partial class DefinitionNormalizer
{
    [GeneratedRegex("^[A-Za-z0-9_]{1,30}$")]
    private static partial Regex NodeIdRegex();

    public static ControlDefinition Normalize(ControlDefinition definition)
    {
        definition.RootEntity = definition.RootEntity?.Trim() ?? string.Empty;
        definition.Filter ??= new GroupNode();

        var used = new HashSet<string>(StringComparer.Ordinal);
        var counters = new Dictionary<string, int>();
        Visit(definition.Filter, used, counters);
        return definition;
    }

    private static void Visit(FilterNode node, HashSet<string> used, Dictionary<string, int> counters)
    {
        if (node.Id is null || !NodeIdRegex().IsMatch(node.Id) || used.Contains(node.Id))
            node.Id = NextId(node, used, counters);
        used.Add(node.Id);

        switch (node)
        {
            case GroupNode g:
                g.Children ??= new List<FilterNode>();
                g.Children.RemoveAll(c => c is null);
                foreach (var child in g.Children) Visit(child, used, counters);
                break;
            case ConditionNode c:
                c.Field = c.Field?.Trim() ?? string.Empty;
                if (c.Value?.Field is not null) c.Value.Field = c.Value.Field.Trim();
                break;
            case RelatedNode r:
                r.Relation = r.Relation?.Trim() ?? string.Empty;
                if (r.Filter is not null) Visit(r.Filter, used, counters);
                break;
        }
    }

    private static string NextId(FilterNode node, HashSet<string> used, Dictionary<string, int> counters)
    {
        var prefix = node switch { GroupNode => "g", RelatedNode => "r", _ => "c" };
        counters.TryGetValue(prefix, out var n);
        string id;
        do { n++; id = prefix + n; } while (used.Contains(id));
        counters[prefix] = n;
        return id;
    }
}
