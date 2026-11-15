using System.Text.Json;
using DataControls.Application.Validation;
using DataControls.Domain.Controls;
using DataControls.Domain.Definitions;

namespace DataControls.Application.Ai;

// =====================================================================
//  החוזה מול שכבת ה-AI.
//  .NET שולח "הקשר סכמה" עסקי (ללא שמות טכניים) + טקסט חופשי,
//  ומקבל בחזרה הצעת בקרה באותו פורמט JSON שבו משתמש העורך הידני.
//  כל מודל (Mock / OpenAI / Claude / מודל מקומי) חייב להחזיר את המבנה הזה.
// =====================================================================

public sealed record SchemaFieldContext(string Key, string Name, string? Description, string DataType,
    bool IsBusinessRequired, IReadOnlyList<string>? AllowedValues, IReadOnlyList<string> Synonyms);

public sealed record SchemaRelationContext(string Key, string Name, string TargetEntity, string Cardinality);

public sealed record SchemaEntityContext(string Key, string Name, string PluralName, string? Description,
    IReadOnlyList<string> Synonyms, IReadOnlyList<SchemaFieldContext> Fields, IReadOnlyList<SchemaRelationContext> Relations);

public sealed record SchemaOperatorContext(string Key, string Label, string Arity, IReadOnlyList<string> AppliesTo, bool AllowsFieldComparison);

/// <summary>כל מה שמודל AI צריך לדעת על הנתונים ועל שפת הבקרות - במונחים עסקיים בלבד.</summary>
public sealed record SchemaContext(
    string Locale,
    IReadOnlyList<SchemaEntityContext> Entities,
    IReadOnlyList<SchemaOperatorContext> Operators,
    IReadOnlyList<string> ValueKinds,
    int DefinitionSchemaVersion);

/// <summary>תשובה גולמית משירות ה-AI (לפני ולידציה). הגדרה מגיעה כ-JSON ונבדקת כמו קלט משתמש.</summary>
public sealed class AiInterpretation
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public JsonElement? Definition { get; set; }
    public double Confidence { get; set; }
    public string? Explanation { get; set; }
    public List<string> Assumptions { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> UnmatchedTerms { get; set; } = new();
    public string? Provider { get; set; }
    public string? Model { get; set; }
}

/// <summary>מתאם לשירות AI. המימוש הנוכחי פונה לשירות Python; ניתן להחליף ללא שינוי בלוגיקת הבקרות.</summary>
public interface IAiControlAssistant
{
    Task<AiInterpretation> InterpretAsync(string text, SchemaContext schema, CancellationToken ct);
    Task<IReadOnlyList<AiInterpretation>> SuggestControlsAsync(SchemaContext schema, int max, CancellationToken ct);
}

/// <summary>טיוטת בקרה מוכנה לאישור המשתמש.</summary>
public sealed class AiDraftDto
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public Severity Severity { get; init; }
    public ControlDefinition? Definition { get; init; }
    public string? Summary { get; init; }
    public bool IsValid { get; init; }
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();
    public double Confidence { get; init; }
    public string? Explanation { get; init; }
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnmatchedTerms { get; init; } = Array.Empty<string>();
    public string? Provider { get; init; }
    public string? Model { get; init; }
}

public sealed class InterpretRequest
{
    public string Text { get; set; } = string.Empty;
}
