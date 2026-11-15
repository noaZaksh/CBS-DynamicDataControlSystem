using DataControls.Application.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>מילון הנתונים העסקי - הבסיס לבניית הממשק באופן גנרי.</summary>
[ApiController]
[Route("api/metadata")]
[Produces("application/json")]
public sealed class MetadataController : ControllerBase
{
    private readonly MetadataService _metadata;
    private readonly ISchemaDiscoveryService _discovery;

    public MetadataController(MetadataService metadata, ISchemaDiscoveryService discovery)
    {
        _metadata = metadata;
        _discovery = discovery;
    }

    /// <summary>ישויות, שדות, קשרים ואופרטורים - במונחים עסקיים בלבד.</summary>
    [HttpGet("model")]
    public Task<DataModelDto> Model(CancellationToken ct) => _metadata.GetModelAsync(ct);

    /// <summary>טבלאות / עמודות / מפתחות זרים ב-DB שעדיין לא ממופים במילון הנתונים.</summary>
    [HttpGet("discover")]
    public Task<SchemaDiscoveryResult> Discover(CancellationToken ct) => _discovery.DiscoverAsync(ct);

    /// <summary>ריענון המטמון לאחר עדכון מילון הנתונים ב-DB.</summary>
    [HttpPost("refresh")]
    public IActionResult Refresh()
    {
        _metadata.Invalidate();
        return NoContent();
    }
}
