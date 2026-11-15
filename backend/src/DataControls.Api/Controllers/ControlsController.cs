using DataControls.Application.Controls;
using DataControls.Application.Execution;
using DataControls.Domain.Controls;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>ניהול והרצת בקרות.</summary>
[ApiController]
[Route("api/controls")]
[Produces("application/json")]
public sealed class ControlsController : ControllerBase
{
    private readonly ControlService _service;

    public ControlsController(ControlService service) => _service = service;

    /// <summary>רשימת הבקרות (כולל תוצאת ההרצה האחרונה).</summary>
    [HttpGet]
    public Task<IReadOnlyList<ControlDto>> List([FromQuery] string? search, [FromQuery] string? category, CancellationToken ct) =>
        _service.ListAsync(search, category, ct);

    [HttpGet("{id:int}")]
    public Task<ControlDto> Get(int id, CancellationToken ct) => _service.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType(typeof(ControlDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ControlDto>> Create([FromBody] SaveControlRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public Task<ControlDto> Update(int id, [FromBody] SaveControlRequest request, CancellationToken ct) =>
        _service.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>שכפול בקרה קיימת לצורך שימוש חוזר.</summary>
    [HttpPost("{id:int}/duplicate")]
    public Task<ControlDto> Duplicate(int id, CancellationToken ct) => _service.DuplicateAsync(id, ct);

    /// <summary>בדיקת תקינות הגדרה + תיאור בעברית + ה-SQL שייווצר (ללא הרצה).</summary>
    [HttpPost("validate")]
    public Task<ValidateDefinitionResponse> Validate([FromBody] DefinitionRequest request, CancellationToken ct) =>
        _service.ValidateAsync(request.Definition, ct);

    /// <summary>הרצת בקרה שמורה. ההרצה נרשמת בהיסטוריה.</summary>
    [HttpPost("{id:int}/run")]
    public Task<RunResultDto> Run(int id, CancellationToken ct) => _service.RunAsync(id, ct);

    /// <summary>הרצת ניסיון להגדרה שעדיין לא נשמרה.</summary>
    [HttpPost("run-preview")]
    public Task<RunResultDto> RunPreview([FromBody] DefinitionRequest request, CancellationToken ct) =>
        _service.RunPreviewAsync(request.Definition, ct);

    [HttpGet("{id:int}/runs")]
    public Task<IReadOnlyList<ControlRunSummary>> Runs(int id, [FromQuery] int take = 20, CancellationToken ct = default) =>
        _service.GetRunsAsync(id, take, ct);
}
