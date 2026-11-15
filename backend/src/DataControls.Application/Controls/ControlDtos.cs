using DataControls.Application.Validation;
using DataControls.Domain.Controls;
using DataControls.Domain.Definitions;

namespace DataControls.Application.Controls;

public sealed class ControlDto
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public Severity Severity { get; init; }
    public ControlSource Source { get; init; }
    public required string RootEntity { get; init; }
    public required string RootEntityName { get; init; }
    public required ControlDefinition Definition { get; init; }

    /// <summary>תיאור ההגדרה בעברית, נוצר אוטומטית.</summary>
    public required string Summary { get; init; }

    public int Version { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public ControlRunSummary? LastRun { get; init; }
}

public sealed class SaveControlRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public Severity Severity { get; set; } = Severity.Medium;
    public ControlSource Source { get; set; } = ControlSource.Manual;
    public ControlDefinition? Definition { get; set; }
}

public sealed class DefinitionRequest
{
    public ControlDefinition? Definition { get; set; }
}

public sealed class ValidateDefinitionResponse
{
    public bool IsValid { get; init; }
    public required IReadOnlyList<ValidationError> Errors { get; init; }
    public string? Summary { get; init; }

    /// <summary>ה-SQL שייווצר (לשקיפות בלבד - לעולם לא מתקבל SQL מהלקוח).</summary>
    public string? GeneratedSql { get; init; }
    public IReadOnlyList<object>? Parameters { get; init; }

    /// <summary>ההגדרה לאחר נרמול (עם מזהי צמתים).</summary>
    public ControlDefinition? NormalizedDefinition { get; init; }
}
