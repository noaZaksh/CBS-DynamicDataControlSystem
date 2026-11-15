using DataControls.Application.Ai;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>
/// הגדרת בקרות בשפה חופשית. מחזיר *טיוטה* בלבד - שמירה והרצה נעשות דרך /api/controls
/// לאחר שהמשתמש מאשר, כך שה-AI לעולם לא מריץ דבר בעצמו.
/// </summary>
[ApiController]
[Route("api/ai")]
[Produces("application/json")]
public sealed class AiController : ControllerBase
{
    private readonly AiControlService _ai;

    public AiController(AiControlService ai) => _ai = ai;

    /// <summary>פענוח תיאור חופשי להצעת בקרה.</summary>
    [HttpPost("interpret")]
    public Task<AiDraftDto> Interpret([FromBody] InterpretRequest request, CancellationToken ct) =>
        _ai.InterpretAsync(request.Text, ct);

    /// <summary>(משימת רשות) הצעות יזומות לבקרות איכות נתונים על בסיס מבנה הנתונים.</summary>
    [HttpGet("suggestions")]
    public Task<IReadOnlyList<AiDraftDto>> Suggestions([FromQuery] int max = 10, CancellationToken ct = default) =>
        _ai.SuggestAsync(max, ct);

    /// <summary>המידע שנשלח למודל ה-AI על מבנה הנתונים (לשקיפות).</summary>
    [HttpGet("schema-context")]
    public Task<SchemaContext> SchemaContext(CancellationToken ct) => _ai.GetSchemaContextAsync(ct);
}
