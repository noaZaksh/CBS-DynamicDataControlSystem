using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Execution;

/// <summary>פרמטר מוקלד לשאילתה. ערכי משתמש תמיד עוברים כפרמטרים ולעולם לא משורשרים ל-SQL.</summary>
public sealed record QueryParameter(string Name, FieldDataType DataType, object? Value);

public enum ColumnRole
{
    /// <summary>המפתח הראשי של הרשומה.</summary>
    Key,
    /// <summary>שדה הכותרת ("שם מלא").</summary>
    Title,
    /// <summary>שדה שמוגדר במילון להצגה קבועה בתוצאות.</summary>
    Display,
    /// <summary>שדה שמשתתף בתנאי - מוצג כ"ראיה" לסיבת החריגה.</summary>
    Evidence,
    /// <summary>דגל פנימי: האם צומת X התקיים ברשומה (לא מוצג).</summary>
    NodeFlag
}

public sealed record OutputColumn(string Alias, string Label, FieldDataType DataType, ColumnRole Role, string? FieldPath = null, string? NodeId = null);

/// <summary>
/// תוצר התרגום: שאילתת SELECT בלבד + פרמטרים + מיפוי עמודות.
/// השאילתה מחזירה 3 Result Sets: סך רשומות שנבדקו, מספר חריגות, והרשומות החריגות (עד MaxRows).
/// </summary>
public sealed class CompiledQuery
{
    public required string Sql { get; init; }
    public required IReadOnlyList<QueryParameter> Parameters { get; init; }
    public required IReadOnlyList<OutputColumn> Columns { get; init; }
    public required int MaxRows { get; init; }

    public string KeyAlias => Columns.First(c => c.Role == ColumnRole.Key).Alias;

    public string? AliasForField(string path) =>
        Columns.FirstOrDefault(c => c.FieldPath is not null &&
                                    string.Equals(c.FieldPath, path, StringComparison.OrdinalIgnoreCase) &&
                                    c.Role is ColumnRole.Evidence or ColumnRole.Display or ColumnRole.Title)?.Alias;

    public string? AliasForNode(string nodeId) =>
        Columns.FirstOrDefault(c => c.Role == ColumnRole.NodeFlag && c.NodeId == nodeId)?.Alias;
}

public sealed record QueryOptions(int MaxRows);

public sealed class QueryResult
{
    public long TotalRecords { get; init; }
    public long MatchCount { get; init; }
    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; }
}

/// <summary>מתרגם הגדרת בקרה (AST) לשאילתה. המימוש תלוי-מנוע (SQL Server) ונמצא ב-Infrastructure.</summary>
public interface IQueryCompiler
{
    CompiledQuery Compile(ControlDefinition definition, DataModel model, QueryOptions options);
}

/// <summary>מריץ שאילתה מתורגמת במצב קריאה-בלבד.</summary>
public interface IQueryExecutor
{
    Task<QueryResult> ExecuteAsync(CompiledQuery query, CancellationToken ct);
}
