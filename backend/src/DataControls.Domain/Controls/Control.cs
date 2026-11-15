using DataControls.Domain.Definitions;

namespace DataControls.Domain.Controls;

public enum Severity { Low, Medium, High }

public enum ControlSource { Manual, AI }

/// <summary>בקרה שמורה: מטא-דאטה עסקי + הגדרה מובנית.</summary>
public sealed class Control
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public Severity Severity { get; set; } = Severity.Medium;
    public ControlSource Source { get; set; } = ControlSource.Manual;
    public ControlDefinition Definition { get; set; } = new();
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ControlRunSummary? LastRun { get; set; }
}

public enum RunStatus { Success, Failed }

public sealed class ControlRunSummary
{
    public long RunId { get; set; }
    public int ControlId { get; set; }
    public int ControlVersion { get; set; }
    public DateTime StartedAt { get; set; }
    public int DurationMs { get; set; }
    public RunStatus Status { get; set; }
    public int? TotalRecords { get; set; }
    public int? ExceptionCount { get; set; }
    public string? ErrorMessage { get; set; }
}
