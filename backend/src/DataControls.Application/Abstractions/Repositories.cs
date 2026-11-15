using DataControls.Domain.Controls;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Abstractions;

/// <summary>מספק את מילון הנתונים העסקי (עם מטמון).</summary>
public interface IMetadataProvider
{
    Task<DataModel> GetModelAsync(CancellationToken ct);
    void Invalidate();
}

public interface IControlRepository
{
    Task<IReadOnlyList<Control>> ListAsync(string? search, string? category, CancellationToken ct);
    Task<Control?> GetAsync(int id, CancellationToken ct);
    Task<int> CreateAsync(Control control, CancellationToken ct);

    /// <summary>מעדכן ומעלה גרסה. מחזיר false אם הבקרה לא נמצאה.</summary>
    Task<bool> UpdateAsync(Control control, CancellationToken ct);

    Task<bool> DeleteAsync(int id, CancellationToken ct);
}

public interface IControlRunRepository
{
    Task<long> AddAsync(ControlRunSummary run, CancellationToken ct);
    Task<IReadOnlyList<ControlRunSummary>> ListAsync(int controlId, int take, CancellationToken ct);
}
