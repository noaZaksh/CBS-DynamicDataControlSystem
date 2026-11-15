using System.Globalization;
using System.Text;
using DataControls.Application.Execution;
using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;

namespace DataControls.Infrastructure.Sql;

/// <summary>
/// מתרגם הגדרת בקרה (AST) לשאילתת T-SQL פרמטרית.
///
/// עקרונות:
///  • מזהים (טבלאות/עמודות) מגיעים רק ממילון הנתונים ומצוטטים; ערכים - רק כפרמטרים.
///  • נתיב שדה דרך קשר ManyToOne → LEFT JOIN (פעם אחת לכל נתיב, ללא שכפול רשומות).
///  • תנאי על קשר OneToMany → EXISTS / NOT EXISTS (ללא שכפול רשומות וללא GROUP BY).
///  • לכל צומת ברמה הראשית נוצרת עמודת דגל (CASE WHEN) כדי להסביר "למה הרשומה חריגה".
///  • נוצרת שאילתת SELECT בלבד. אין שום נתיב קוד שמייצר DML/DDL.
/// </summary>
public sealed class SqlServerQueryCompiler : IQueryCompiler
{
    public CompiledQuery Compile(ControlDefinition definition, DataModel model, QueryOptions options) =>
        new Builder(model, options).Build(definition);

    private sealed class Scope
    {
        public required string Alias { get; init; }
        public required EntityMeta Entity { get; init; }
        public bool IsRoot { get; init; }
        public Dictionary<string, string> JoinAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> JoinClauses { get; } = new();
    }

    private sealed class Builder
    {
        private readonly DataModel _model;
        private readonly QueryOptions _options;
        private readonly List<QueryParameter> _parameters = new();
        private readonly List<(string NodeId, string Expression)> _flags = new();
        private readonly List<ResolvedField> _evidence = new();
        private int _aliasCounter;

        public Builder(DataModel model, QueryOptions options)
        {
            _model = model;
            _options = options;
        }

        public CompiledQuery Build(ControlDefinition definition)
        {
            var rootEntity = _model.GetEntity(definition.RootEntity);
            var root = NewScope(rootEntity, isRoot: true);

            var where = CompileNode(definition.Filter, root);

            var columns = new List<OutputColumn>();
            var select = new List<string>();

            void AddColumn(string expression, OutputColumn column)
            {
                select.Add($"{expression} AS {SqlIdentifier.Quote(column.Alias)}");
                columns.Add(column);
            }

            AddColumn($"{root.Alias}.{SqlIdentifier.Quote(rootEntity.PrimaryKeyColumn)}",
                new OutputColumn("__key", "מזהה", FieldDataType.Number, ColumnRole.Key));

            var coveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (rootEntity.TitleField is { } title)
            {
                AddColumn($"{root.Alias}.{SqlIdentifier.Quote(title.ColumnName)}",
                    new OutputColumn("__title", title.DisplayName, title.DataType, ColumnRole.Title, title.Key));
                coveredPaths.Add(title.Key);
            }

            foreach (var field in rootEntity.Fields.Values.Where(f => f.ShowInResults).OrderBy(f => f.SortOrder))
            {
                if (!coveredPaths.Add(field.Key)) continue;
                AddColumn($"{root.Alias}.{SqlIdentifier.Quote(field.ColumnName)}",
                    new OutputColumn($"d_{field.Key}", field.DisplayName, field.DataType, ColumnRole.Display, field.Key));
            }

            var evidenceIndex = 0;
            foreach (var resolved in _evidence)
            {
                if (!coveredPaths.Add(resolved.Path)) continue;
                AddColumn(ColumnRef(root, resolved),
                    new OutputColumn($"e_{evidenceIndex++}", resolved.DisplayLabel, resolved.Field.DataType, ColumnRole.Evidence, resolved.Path));
            }

            foreach (var (nodeId, expression) in _flags)
            {
                AddColumn($"CASE WHEN {expression} THEN 1 ELSE 0 END",
                    new OutputColumn($"__f_{nodeId}", nodeId, FieldDataType.Number, ColumnRole.NodeFlag, NodeId: nodeId));
            }

            var from = $"{Source(rootEntity)} AS {root.Alias}";
            var joins = root.JoinClauses.Count == 0 ? string.Empty : "\n" + string.Join("\n", root.JoinClauses);
            var maxRows = Math.Max(1, _options.MaxRows).ToString(CultureInfo.InvariantCulture);

            var sql = new StringBuilder()
                .AppendLine("-- 1) total records checked")
                .AppendLine($"SELECT COUNT_BIG(*) AS [TotalRecords] FROM {from};")
                .AppendLine("-- 2) number of exceptions")
                .AppendLine($"SELECT COUNT_BIG(*) AS [MatchCount] FROM {from}{joins}")
                .AppendLine($"WHERE {where};")
                .AppendLine("-- 3) exception rows")
                .AppendLine($"SELECT TOP ({maxRows})")
                .AppendLine("    " + string.Join(",\n    ", select))
                .AppendLine($"FROM {from}{joins}")
                .AppendLine($"WHERE {where}")
                .Append($"ORDER BY {root.Alias}.{SqlIdentifier.Quote(rootEntity.PrimaryKeyColumn)};")
                .ToString();

            return new CompiledQuery
            {
                Sql = sql,
                Parameters = _parameters,
                Columns = columns,
                MaxRows = _options.MaxRows
            };
        }

        // ------------------------------------------------------------------ nodes

        private string CompileNode(FilterNode node, Scope scope)
        {
            var expression = node switch
            {
                GroupNode g => CompileGroup(g, scope),
                ConditionNode c => CompileCondition(c, scope),
                RelatedNode r => CompileRelated(r, scope),
                _ => throw new NotSupportedException($"Unsupported node type {node.GetType().Name}")
            };

            if (scope.IsRoot && node.Id is not null)
                _flags.Add((node.Id, expression));

            return expression;
        }

        private string CompileGroup(GroupNode group, Scope scope)
        {
            if (group.Children.Count == 0)
                return group.Negate ? "(1 = 0)" : "(1 = 1)";

            var joiner = group.Logic == LogicalOperator.And ? " AND " : " OR ";
            var body = "(" + string.Join(joiner, group.Children.Select(c => CompileNode(c, scope))) + ")";
            return group.Negate ? $"(NOT {body})" : body;
        }

        private string CompileRelated(RelatedNode node, Scope scope)
        {
            var relation = scope.Entity.Relations[node.Relation];
            var target = _model.GetEntity(relation.ToEntityKey);
            var sub = NewScope(target, isRoot: false);

            var inner = node.Filter is { Children.Count: > 0 } ? CompileNode(node.Filter, sub) : null;
            var joins = sub.JoinClauses.Count == 0 ? string.Empty : " " + string.Join(" ", sub.JoinClauses);

            var correlation = $"{sub.Alias}.{SqlIdentifier.Quote(relation.ToColumn)} = {scope.Alias}.{SqlIdentifier.Quote(relation.FromColumn)}";
            var exists = $"EXISTS (SELECT 1 FROM {Source(target)} AS {sub.Alias}{joins} WHERE {correlation}{(inner is null ? string.Empty : " AND " + inner)})";

            return node.Quantifier == Quantifier.None ? $"(NOT {exists})" : exists;
        }

        private string CompileCondition(ConditionNode c, Scope scope)
        {
            var field = FieldPathResolver.Resolve(_model, scope.Entity, c.Field);
            var type = field.Field.DataType;
            var col = ColumnRef(scope, field);
            if (scope.IsRoot) _evidence.Add(field);

            string Val(ValueSpec? spec) => ValueExpression(spec, type, scope);

            switch (c.Operator)
            {
                case ComparisonOperator.Eq: return $"{col} = {Val(c.Value)}";
                // "שונה מ" כולל גם ערך חסר - כך משתמש עסקי מצפה ("סטטוס שונה מפעיל" כולל סטטוס ריק)
                case ComparisonOperator.Neq: return $"({col} <> {Val(c.Value)} OR {col} IS NULL)";
                case ComparisonOperator.Gt: return $"{col} > {Val(c.Value)}";
                case ComparisonOperator.Gte: return $"{col} >= {Val(c.Value)}";
                case ComparisonOperator.Lt: return $"{col} < {Val(c.Value)}";
                case ComparisonOperator.Lte: return $"{col} <= {Val(c.Value)}";
                case ComparisonOperator.Between: return $"({col} BETWEEN {Val(c.Value)} AND {Val(c.ValueTo)})";

                case ComparisonOperator.In:
                    return $"{col} IN ({ListExpression(c.Value!, type)})";
                case ComparisonOperator.NotIn:
                    return $"({col} NOT IN ({ListExpression(c.Value!, type)}) OR {col} IS NULL)";

                case ComparisonOperator.Contains:
                    return $"{col} LIKE {LikeParam(c.Value, "%", "%")} ESCAPE '\\'";
                case ComparisonOperator.NotContains:
                    return $"({col} NOT LIKE {LikeParam(c.Value, "%", "%")} ESCAPE '\\' OR {col} IS NULL)";
                case ComparisonOperator.StartsWith:
                    return $"{col} LIKE {LikeParam(c.Value, "", "%")} ESCAPE '\\'";
                case ComparisonOperator.EndsWith:
                    return $"{col} LIKE {LikeParam(c.Value, "%", "")} ESCAPE '\\'";

                case ComparisonOperator.IsEmpty:
                    return type == FieldDataType.String
                        ? $"({col} IS NULL OR LTRIM(RTRIM({col})) = N'')"
                        : $"{col} IS NULL";
                case ComparisonOperator.IsNotEmpty:
                    return type == FieldDataType.String
                        ? $"({col} IS NOT NULL AND LTRIM(RTRIM({col})) <> N'')"
                        : $"{col} IS NOT NULL";

                default:
                    throw new NotSupportedException($"Operator {c.Operator} is not supported");
            }
        }

        // ------------------------------------------------------------------ values

        private string ValueExpression(ValueSpec? spec, FieldDataType type, Scope scope)
        {
            if (spec is null) throw new InvalidOperationException("Missing value");

            switch (spec.Kind)
            {
                case ValueKind.Constant:
                    return AddParameter(type, Convert(spec.Value, type));

                case ValueKind.Field:
                    var other = FieldPathResolver.Resolve(_model, scope.Entity, spec.Field!);
                    if (scope.IsRoot) _evidence.Add(other);
                    return ColumnRef(scope, other);

                case ValueKind.Today:
                    // OffsetDays הוא int מוקלד ומאומת - בטוח לשילוב כליטרל
                    var days = (spec.OffsetDays ?? 0).ToString(CultureInfo.InvariantCulture);
                    return $"DATEADD(DAY, {days}, CAST(GETDATE() AS DATE))";

                default:
                    throw new InvalidOperationException($"Value kind {spec.Kind} is not valid here");
            }
        }

        private string ListExpression(ValueSpec spec, FieldDataType type) =>
            string.Join(", ", spec.Values!.Select(v => AddParameter(type, Convert(v, type))));

        private string LikeParam(ValueSpec? spec, string prefix, string suffix)
        {
            var text = Convert(spec?.Value, FieldDataType.String) as string ?? string.Empty;
            return AddParameter(FieldDataType.String, prefix + EscapeLike(text) + suffix);
        }

        internal static string EscapeLike(string value) => value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_")
            .Replace("[", "\\[");

        private static object? Convert(System.Text.Json.JsonElement? value, FieldDataType type) =>
            ValueConverter.TryConvert(value, type, out var converted, out var error)
                ? converted
                : throw new InvalidOperationException(error);

        private string AddParameter(FieldDataType type, object? value)
        {
            var name = $"@p{_parameters.Count}";
            _parameters.Add(new QueryParameter(name, type, value));
            return name;
        }

        // ------------------------------------------------------------------ joins

        private Scope NewScope(EntityMeta entity, bool isRoot) =>
            new() { Alias = NextAlias(), Entity = entity, IsRoot = isRoot };

        private string NextAlias() => $"t{_aliasCounter++}";

        private static string Source(EntityMeta entity) => SqlIdentifier.QuoteObject(entity.SourceSchema, entity.SourceObject);

        /// <summary>מחזיר הפניה לעמודה, ומוסיף LEFT JOIN לכל קשר בנתיב (פעם אחת בלבד לכל נתיב).</summary>
        private string ColumnRef(Scope scope, ResolvedField field)
        {
            var alias = scope.Alias;
            var path = string.Empty;

            foreach (var relation in field.Relations)
            {
                path = path.Length == 0 ? relation.Key : $"{path}.{relation.Key}";
                if (!scope.JoinAliases.TryGetValue(path, out var joinAlias))
                {
                    joinAlias = NextAlias();
                    var target = _model.GetEntity(relation.ToEntityKey);
                    scope.JoinClauses.Add(
                        $"LEFT JOIN {Source(target)} AS {joinAlias} ON {joinAlias}.{SqlIdentifier.Quote(relation.ToColumn)} = {alias}.{SqlIdentifier.Quote(relation.FromColumn)}");
                    scope.JoinAliases[path] = joinAlias;
                }
                alias = joinAlias;
            }

            return $"{alias}.{SqlIdentifier.Quote(field.Field.ColumnName)}";
        }
    }
}
