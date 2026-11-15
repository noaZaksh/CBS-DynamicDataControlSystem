using System.Diagnostics;
using DataControls.Application.Common;
using DataControls.Application.Validation;
using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Application.Execution;

public sealed class EngineOptions
{
    public const string Section = "Engine";

    /// <summary>מקסימום רשומות חריגות שמוחזרות למסך (המספר הכולל נספר תמיד).</summary>
    public int MaxResultRows { get; set; } = 5000;

    /// <summary>האם להחזיר את ה-SQL שנוצר (לשקיפות / דיבאג). מומלץ לכבות בסביבת ייצור.</summary>
    public bool ExposeGeneratedSql { get; set; } = true;
}

public sealed record ResultColumnDto(string Key, string Label, string DataType, string Role);

public sealed class ResultRowDto
{
    public required string Key { get; init; }
    public string? Title { get; init; }
    public required Dictionary<string, object?> Values { get; init; }
    public required List<string> Reasons { get; init; }
    public required List<string> MatchedNodeIds { get; init; }
}

public sealed class RunResultDto
{
    public int? ControlId { get; init; }
    public string? ControlName { get; init; }
    public long? RunId { get; set; }
    public required string EntityName { get; init; }
    public required string Summary { get; init; }
    public DateTime StartedAt { get; init; }
    public int DurationMs { get; init; }
    public long TotalRecords { get; init; }
    public long ExceptionCount { get; init; }
    public bool Truncated { get; init; }
    public required List<ResultColumnDto> Columns { get; init; }
    public required List<ResultRowDto> Rows { get; init; }
}

/// <summary>
/// מתזמר הרצה: ולידציה → תרגום → הרצה → בניית הסברים.
/// לא יודע דבר על SQL - עובד מול IQueryCompiler / IQueryExecutor.
/// </summary>
public sealed class ControlRunner
{
    private readonly IQueryCompiler _compiler;
    private readonly IQueryExecutor _executor;
    private readonly ControlDefinitionValidator _validator;
    private readonly ReasonBuilder _reasons;
    private readonly Describing.DefinitionDescriber _describer;
    private readonly EngineOptions _options;
    private readonly ILogger<ControlRunner> _logger;

    public ControlRunner(IQueryCompiler compiler, IQueryExecutor executor, ControlDefinitionValidator validator,
        ReasonBuilder reasons, Describing.DefinitionDescriber describer, IOptions<EngineOptions> options, ILogger<ControlRunner> logger)
    {
        _compiler = compiler;
        _executor = executor;
        _validator = validator;
        _reasons = reasons;
        _describer = describer;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>מאמת ומתרגם (ללא הרצה). זורק DefinitionValidationException אם ההגדרה לא תקינה.</summary>
    public CompiledQuery Prepare(ControlDefinition definition, DataModel model)
    {
        DefinitionNormalizer.Normalize(definition);
        var validation = _validator.Validate(definition, model);
        if (!validation.IsValid)
            throw new DefinitionValidationException(validation.Errors);
        return _compiler.Compile(definition, model, new QueryOptions(_options.MaxResultRows));
    }

    public async Task<RunResultDto> RunAsync(ControlDefinition definition, DataModel model, int? controlId, string? controlName, CancellationToken ct)
    {
        var query = Prepare(definition, model);
        var startedAt = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        var result = await _executor.ExecuteAsync(query, ct);
        sw.Stop();

        _logger.LogInformation("Control {ControlId} ran in {Elapsed} ms: {Matches}/{Total} exceptions",
            controlId, sw.ElapsedMilliseconds, result.MatchCount, result.TotalRecords);

        var root = model.GetEntity(definition.RootEntity);
        var visibleColumns = query.Columns
            .Where(c => c.Role is ColumnRole.Display or ColumnRole.Evidence or ColumnRole.Title)
            .ToList();
        var titleAlias = query.Columns.FirstOrDefault(c => c.Role == ColumnRole.Title)?.Alias;

        var rows = result.Rows.Select(row =>
        {
            var (reasons, matched) = _reasons.Explain(definition, model, query, row);
            return new ResultRowDto
            {
                Key = Convert.ToString(row[query.KeyAlias], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                Title = titleAlias is not null && row.TryGetValue(titleAlias, out var t) ? t?.ToString() : null,
                Values = visibleColumns.ToDictionary(c => c.Alias, c => ToJsonValue(row.GetValueOrDefault(c.Alias), c.DataType)),
                Reasons = reasons,
                MatchedNodeIds = matched
            };
        }).ToList();

        return new RunResultDto
        {
            ControlId = controlId,
            ControlName = controlName,
            EntityName = root.DisplayNamePlural,
            Summary = _describer.Describe(definition, model),
            StartedAt = startedAt,
            DurationMs = (int)sw.ElapsedMilliseconds,
            TotalRecords = result.TotalRecords,
            ExceptionCount = result.MatchCount,
            Truncated = result.MatchCount > rows.Count,
            Columns = visibleColumns.Select(c => new ResultColumnDto(c.Alias, c.Label, c.DataType.ToString().ToLowerInvariant(), c.Role.ToString().ToLowerInvariant())).ToList(),
            Rows = rows
        };
    }

    /// <summary>ערכים בפורמט ידידותי ל-JSON: תאריכים כ-yyyy-MM-dd, מספרים כמספרים.</summary>
    private static object? ToJsonValue(object? value, FieldDataType type) => value switch
    {
        null or DBNull => null,
        DateTime dt => dt.ToString("yyyy-MM-dd"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        bool b => b,
        _ when type == FieldDataType.Number => Convert.ToDecimal(value),
        _ => value
    };
}
