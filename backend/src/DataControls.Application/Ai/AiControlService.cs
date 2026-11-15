using System.Text.Json;
using DataControls.Application.Abstractions;
using DataControls.Application.Common;
using DataControls.Application.Describing;
using DataControls.Application.Metadata;
using DataControls.Application.Validation;
using DataControls.Domain.Controls;
using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;
using Microsoft.Extensions.Logging;

namespace DataControls.Application.Ai;

/// <summary>
/// מתווך בין שכבת ה-AI לבין מנוע הבקרות:
/// 1. בונה הקשר סכמה עסקי מתוך מילון הנתונים.
/// 2. שולח לספק ה-AI (דרך IAiControlAssistant).
/// 3. מתרגם את התשובה להגדרת בקרה ומאמת אותה בדיוק כמו קלט ידני.
/// התוצאה היא טיוטה בלבד - שמירה והרצה מתבצעות רק לאחר אישור המשתמש.
/// </summary>
public sealed class AiControlService
{
    public const int MaxTextLength = 1000;

    private readonly IAiControlAssistant _assistant;
    private readonly IMetadataProvider _metadata;
    private readonly ControlDefinitionValidator _validator;
    private readonly DefinitionDescriber _describer;
    private readonly ILogger<AiControlService> _logger;

    public AiControlService(IAiControlAssistant assistant, IMetadataProvider metadata,
        ControlDefinitionValidator validator, DefinitionDescriber describer, ILogger<AiControlService> logger)
    {
        _assistant = assistant;
        _metadata = metadata;
        _validator = validator;
        _describer = describer;
        _logger = logger;
    }

    public async Task<SchemaContext> GetSchemaContextAsync(CancellationToken ct) =>
        BuildSchemaContext(await _metadata.GetModelAsync(ct));

    public async Task<AiDraftDto> InterpretAsync(string? text, CancellationToken ct)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            throw new DefinitionValidationException(new[] { new ValidationError(null, "יש לתאר את הבקרה במילים") });
        if (text.Length > MaxTextLength)
            throw new DefinitionValidationException(new[] { new ValidationError(null, $"התיאור ארוך מדי (עד {MaxTextLength} תווים)") });

        var model = await _metadata.GetModelAsync(ct);
        var interpretation = await _assistant.InterpretAsync(text, BuildSchemaContext(model), ct);
        return ToDraft(interpretation, model);
    }

    public async Task<IReadOnlyList<AiDraftDto>> SuggestAsync(int max, CancellationToken ct)
    {
        var model = await _metadata.GetModelAsync(ct);
        var suggestions = await _assistant.SuggestControlsAsync(BuildSchemaContext(model), Math.Clamp(max, 1, 50), ct);
        return suggestions.Select(s => ToDraft(s, model)).ToList();
    }

    /// <summary>תרגום תשובת AI לטיוטת בקרה. תשובה לא תקינה לא נזרקת - מוצגת למשתמש עם השגיאות.</summary>
    internal AiDraftDto ToDraft(AiInterpretation ai, DataModel model)
    {
        ControlDefinition? definition = null;
        var errors = new List<ValidationError>();

        if (ai.Definition is null || ai.Definition.Value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new(null, "ה-AI לא הצליח לבנות הגדרת בקרה מהתיאור. נסו לנסח מחדש או להגדיר ידנית."));
        }
        else
        {
            try
            {
                definition = DefinitionNormalizer.Normalize(DefinitionSerializer.Deserialize(ai.Definition.Value));
                errors.AddRange(_validator.Validate(definition, model).Errors);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "AI returned a definition that does not match the contract");
                errors.Add(new(null, "תשובת ה-AI אינה תואמת את מבנה הבקרה המוסכם"));
            }
        }

        string? summary = null;
        if (definition is not null && errors.Count == 0)
            summary = _describer.Describe(definition, model);

        return new AiDraftDto
        {
            Name = string.IsNullOrWhiteSpace(ai.Name) ? "בקרה חדשה (הצעת AI)" : ai.Name.Trim(),
            Description = ai.Description,
            Category = ai.Category,
            Severity = Enum.TryParse<Severity>(ai.Severity, ignoreCase: true, out var sev) ? sev : Severity.Medium,
            Definition = definition,
            Summary = summary,
            IsValid = errors.Count == 0,
            ValidationErrors = errors,
            Confidence = Math.Clamp(ai.Confidence, 0, 1),
            Explanation = ai.Explanation,
            Assumptions = ai.Assumptions,
            Warnings = ai.Warnings,
            UnmatchedTerms = ai.UnmatchedTerms,
            Provider = ai.Provider,
            Model = ai.Model
        };
    }

    public static SchemaContext BuildSchemaContext(DataModel model) => new(
        Locale: "he-IL",
        Entities: model.Entities.OrderBy(e => e.Key).Select(e => new SchemaEntityContext(
            e.Key, e.DisplayName, e.DisplayNamePlural, e.Description, e.Synonyms,
            e.Fields.Values.OrderBy(f => f.SortOrder).Select(f => new SchemaFieldContext(
                f.Key, f.DisplayName, f.Description, f.DataType.ToString().ToLowerInvariant(),
                f.IsBusinessRequired, f.AllowedValues, f.Synonyms)).ToList(),
            e.Relations.Values.Select(r => new SchemaRelationContext(
                r.Key, r.DisplayName, r.ToEntityKey, r.Cardinality.ToString())).ToList())).ToList(),
        Operators: MetadataService.GetOperators()
            .Select(o => new SchemaOperatorContext(o.Operator, o.Label, o.Arity, o.AppliesTo, o.AllowsFieldComparison)).ToList(),
        ValueKinds: Enum.GetNames<ValueKind>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]).ToList(),
        DefinitionSchemaVersion: ControlDefinition.CurrentSchemaVersion);
}
