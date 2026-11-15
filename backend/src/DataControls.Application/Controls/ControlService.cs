using DataControls.Application.Abstractions;
using DataControls.Application.Common;
using DataControls.Application.Describing;
using DataControls.Application.Execution;
using DataControls.Application.Validation;
using DataControls.Domain.Controls;
using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Application.Controls;

/// <summary>Use-cases של בקרות: ניהול, ולידציה, הרצה ותיעוד הרצות.</summary>
public sealed class ControlService
{
    private readonly IControlRepository _controls;
    private readonly IControlRunRepository _runs;
    private readonly IMetadataProvider _metadata;
    private readonly ControlDefinitionValidator _validator;
    private readonly DefinitionDescriber _describer;
    private readonly ControlRunner _runner;
    private readonly EngineOptions _options;
    private readonly ILogger<ControlService> _logger;

    public ControlService(IControlRepository controls, IControlRunRepository runs, IMetadataProvider metadata,
        ControlDefinitionValidator validator, DefinitionDescriber describer, ControlRunner runner,
        IOptions<EngineOptions> options, ILogger<ControlService> logger)
    {
        _controls = controls;
        _runs = runs;
        _metadata = metadata;
        _validator = validator;
        _describer = describer;
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ControlDto>> ListAsync(string? search, string? category, CancellationToken ct)
    {
        var model = await _metadata.GetModelAsync(ct);
        var controls = await _controls.ListAsync(search, category, ct);
        return controls.Select(c => ToDto(c, model)).ToList();
    }

    public async Task<ControlDto> GetAsync(int id, CancellationToken ct)
    {
        var model = await _metadata.GetModelAsync(ct);
        return ToDto(await LoadAsync(id, ct), model);
    }

    public async Task<ControlDto> CreateAsync(SaveControlRequest request, CancellationToken ct)
    {
        var model = await _metadata.GetModelAsync(ct);
        var control = BuildValidated(request, model);
        control.Id = await _controls.CreateAsync(control, ct);
        _logger.LogInformation("Control {ControlId} created ({Source})", control.Id, control.Source);
        return await GetAsync(control.Id, ct);
    }

    public async Task<ControlDto> UpdateAsync(int id, SaveControlRequest request, CancellationToken ct)
    {
        var model = await _metadata.GetModelAsync(ct);
        var existing = await LoadAsync(id, ct);
        var control = BuildValidated(request, model);
        control.Id = id;
        control.Source = existing.Source;   // מקור הבקרה נשמר גם אחרי עריכה
        if (!await _controls.UpdateAsync(control, ct))
            throw new NotFoundException($"בקרה {id} לא נמצאה");
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        if (!await _controls.DeleteAsync(id, ct))
            throw new NotFoundException($"בקרה {id} לא נמצאה");
    }

    /// <summary>שימוש חוזר: יוצר עותק של בקרה קיימת כנקודת מוצא לבקרה חדשה.</summary>
    public async Task<ControlDto> DuplicateAsync(int id, CancellationToken ct)
    {
        var source = await LoadAsync(id, ct);
        var copy = new SaveControlRequest
        {
            Name = $"{source.Name} (עותק)",
            Description = source.Description,
            Category = source.Category,
            Severity = source.Severity,
            Source = ControlSource.Manual,
            Definition = DefinitionSerializer.Deserialize(DefinitionSerializer.Serialize(source.Definition))
        };
        return await CreateAsync(copy, ct);
    }

    public async Task<ValidateDefinitionResponse> ValidateAsync(ControlDefinition? definition, CancellationToken ct)
    {
        var model = await _metadata.GetModelAsync(ct);
        if (definition is null)
            return new ValidateDefinitionResponse { IsValid = false, Errors = new[] { new ValidationError(null, "לא התקבלה הגדרת בקרה") } };

        DefinitionNormalizer.Normalize(definition);
        var validation = _validator.Validate(definition, model);
        if (!validation.IsValid)
            return new ValidateDefinitionResponse { IsValid = false, Errors = validation.Errors, NormalizedDefinition = definition };

        var compiled = _runner.Prepare(definition, model);
        return new ValidateDefinitionResponse
        {
            IsValid = true,
            Errors = Array.Empty<ValidationError>(),
            Summary = _describer.Describe(definition, model),
            GeneratedSql = _options.ExposeGeneratedSql ? compiled.Sql : null,
            Parameters = _options.ExposeGeneratedSql
                ? compiled.Parameters.Select(p => (object)new { p.Name, Type = p.DataType.ToString(), p.Value }).ToList()
                : null,
            NormalizedDefinition = definition
        };
    }

    /// <summary>מריץ בקרה שמורה ושומר את תוצאת ההרצה בהיסטוריה (גם במקרה כשל).</summary>
    public async Task<RunResultDto> RunAsync(int id, CancellationToken ct)
    {
        var control = await LoadAsync(id, ct);
        var model = await _metadata.GetModelAsync(ct);
        var startedAt = DateTime.UtcNow;

        try
        {
            var result = await _runner.RunAsync(control.Definition, model, control.Id, control.Name, ct);
            result.RunId = await _runs.AddAsync(new ControlRunSummary
            {
                ControlId = control.Id,
                ControlVersion = control.Version,
                StartedAt = result.StartedAt,
                DurationMs = result.DurationMs,
                Status = RunStatus.Success,
                TotalRecords = (int)Math.Min(result.TotalRecords, int.MaxValue),
                ExceptionCount = (int)Math.Min(result.ExceptionCount, int.MaxValue)
            }, CancellationToken.None);
            return result;
        }
        catch (UserFacingException ex) when (ex is DefinitionValidationException or QueryExecutionException)
        {
            await _runs.AddAsync(new ControlRunSummary
            {
                ControlId = control.Id,
                ControlVersion = control.Version,
                StartedAt = startedAt,
                DurationMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                Status = RunStatus.Failed,
                ErrorMessage = ex is DefinitionValidationException dve
                    ? string.Join("; ", dve.Errors.Select(e => e.Message))
                    : ex.Message
            }, CancellationToken.None);
            throw;
        }
    }

    /// <summary>הרצת "ניסיון" על הגדרה שעדיין לא נשמרה (מהעורך או מהצעת AI). לא נשמרת בהיסטוריה.</summary>
    public async Task<RunResultDto> RunPreviewAsync(ControlDefinition? definition, CancellationToken ct)
    {
        if (definition is null)
            throw new DefinitionValidationException(new[] { new ValidationError(null, "לא התקבלה הגדרת בקרה") });
        var model = await _metadata.GetModelAsync(ct);
        return await _runner.RunAsync(definition, model, null, null, ct);
    }

    public async Task<IReadOnlyList<ControlRunSummary>> GetRunsAsync(int id, int take, CancellationToken ct)
    {
        await LoadAsync(id, ct);
        return await _runs.ListAsync(id, Math.Clamp(take, 1, 200), ct);
    }

    private async Task<Control> LoadAsync(int id, CancellationToken ct) =>
        await _controls.GetAsync(id, ct) ?? throw new NotFoundException($"בקרה {id} לא נמצאה");

    private Control BuildValidated(SaveControlRequest request, DataModel model)
    {
        var errors = new List<ValidationError>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) errors.Add(new(null, "יש להזין שם לבקרה"));
        if (name.Length > 150) errors.Add(new(null, "שם הבקרה ארוך מדי (עד 150 תווים)"));
        if (request.Description?.Length > 1000) errors.Add(new(null, "התיאור ארוך מדי (עד 1000 תווים)"));
        if (request.Category?.Length > 50) errors.Add(new(null, "שם הקטגוריה ארוך מדי"));

        if (request.Definition is null)
        {
            errors.Add(new(null, "לא התקבלה הגדרת בקרה"));
        }
        else
        {
            DefinitionNormalizer.Normalize(request.Definition);
            errors.AddRange(_validator.Validate(request.Definition, model).Errors);
        }

        if (errors.Count > 0)
            throw new DefinitionValidationException(errors);

        return new Control
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
            Severity = request.Severity,
            Source = request.Source,
            Definition = request.Definition!
        };
    }

    private ControlDto ToDto(Control c, DataModel model)
    {
        var entityName = model.TryGetEntity(c.Definition.RootEntity, out var e) ? e.DisplayNamePlural : c.Definition.RootEntity;
        string summary;
        try { summary = _describer.Describe(c.Definition, model); }
        catch (Exception ex)
        {
            // הגדרה ישנה שאינה תואמת את מילון הנתונים העדכני - לא מפילים את הרשימה
            _logger.LogWarning(ex, "Could not describe control {ControlId}", c.Id);
            summary = "לא ניתן להציג תיאור - ייתכן שמילון הנתונים השתנה";
        }

        return new ControlDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            Category = c.Category,
            Severity = c.Severity,
            Source = c.Source,
            RootEntity = c.Definition.RootEntity,
            RootEntityName = entityName,
            Definition = c.Definition,
            Summary = summary,
            Version = c.Version,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            LastRun = c.LastRun
        };
    }
}
