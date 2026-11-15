using System.Data;
using System.Text.Json;
using DataControls.Application.Abstractions;
using DataControls.Domain.Controls;
using DataControls.Domain.Definitions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure.Persistence;

public sealed class SqlControlRepository : IControlRepository
{
    private const string SelectColumns = """
        SELECT c.ControlId, c.Name, c.Description, c.Category, c.Severity, c.Source, c.DefinitionJson,
               c.Version, c.CreatedAt, c.UpdatedAt,
               lr.RunId, lr.ControlVersion, lr.StartedAt, lr.DurationMs, lr.Status, lr.TotalRecords, lr.ExceptionCount, lr.ErrorMessage
        FROM ctl.Controls AS c
        OUTER APPLY (SELECT TOP (1) r.* FROM ctl.ControlRuns AS r WHERE r.ControlId = c.ControlId ORDER BY r.StartedAt DESC) AS lr
        """;

    private readonly DatabaseOptions _options;
    private readonly ILogger<SqlControlRepository> _logger;

    public SqlControlRepository(IOptions<DatabaseOptions> options, ILogger<SqlControlRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Control>> ListAsync(string? search, string? category, CancellationToken ct)
    {
        const string sql = SelectColumns + """

            WHERE c.IsActive = 1
              AND (@search IS NULL OR c.Name LIKE @search OR c.Description LIKE @search)
              AND (@category IS NULL OR c.Category = @category)
            ORDER BY c.Category, c.Name;
            """;

        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@search", SqlDbType.NVarChar, 200)
        {
            Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : $"%{EscapeLike(search.Trim())}%"
        });
        cmd.Parameters.Add(new SqlParameter("@category", SqlDbType.NVarChar, 50)
        {
            Value = string.IsNullOrWhiteSpace(category) ? DBNull.Value : category.Trim()
        });

        var list = new List<Control>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var control = Map(reader);
            if (control is not null) list.Add(control);
        }
        return list;
    }

    public async Task<Control?> GetAsync(int id, CancellationToken ct)
    {
        const string sql = SelectColumns + "\nWHERE c.ControlId = @id AND c.IsActive = 1;";
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<int> CreateAsync(Control control, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO ctl.Controls (Name, Description, Category, Severity, RootEntityKey, DefinitionJson, Source)
            OUTPUT INSERTED.ControlId
            VALUES (@name, @description, @category, @severity, @root, @json, @source);
            """;
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        AddCommonParameters(cmd, control);
        cmd.Parameters.Add(new SqlParameter("@source", SqlDbType.VarChar, 10) { Value = control.Source.ToString() });
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> UpdateAsync(Control control, CancellationToken ct)
    {
        const string sql = """
            UPDATE ctl.Controls
               SET Name = @name, Description = @description, Category = @category, Severity = @severity,
                   RootEntityKey = @root, DefinitionJson = @json,
                   Version = Version + 1, UpdatedAt = SYSUTCDATETIME()
             WHERE ControlId = @id AND IsActive = 1;
            """;
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        AddCommonParameters(cmd, control);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = control.Id });
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>מחיקה רכה - שומרת את היסטוריית ההרצות לצורכי ביקורת.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        const string sql = "UPDATE ctl.Controls SET IsActive = 0, UpdatedAt = SYSUTCDATETIME() WHERE ControlId = @id AND IsActive = 1;";
        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_options.ControlStoreConnection);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static void AddCommonParameters(SqlCommand cmd, Control c)
    {
        cmd.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 150) { Value = c.Name });
        cmd.Parameters.Add(new SqlParameter("@description", SqlDbType.NVarChar, 1000) { Value = (object?)c.Description ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@category", SqlDbType.NVarChar, 50) { Value = (object?)c.Category ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@severity", SqlDbType.VarChar, 10) { Value = c.Severity.ToString() });
        cmd.Parameters.Add(new SqlParameter("@root", SqlDbType.VarChar, 50) { Value = c.Definition.RootEntity });
        cmd.Parameters.Add(new SqlParameter("@json", SqlDbType.NVarChar, -1) { Value = DefinitionSerializer.Serialize(c.Definition) });
    }

    private Control? Map(SqlDataReader r)
    {
        var id = r.GetInt32(r.GetOrdinal("ControlId"));
        ControlDefinition definition;
        try
        {
            definition = DefinitionSerializer.Deserialize(r.GetString(r.GetOrdinal("DefinitionJson")));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Control {ControlId} has a corrupt definition and is skipped", id);
            return null;
        }

        var control = new Control
        {
            Id = id,
            Name = r.GetString(r.GetOrdinal("Name")),
            Description = r.IsDBNull(r.GetOrdinal("Description")) ? null : r.GetString(r.GetOrdinal("Description")),
            Category = r.IsDBNull(r.GetOrdinal("Category")) ? null : r.GetString(r.GetOrdinal("Category")),
            Severity = Enum.Parse<Severity>(r.GetString(r.GetOrdinal("Severity")), ignoreCase: true),
            Source = Enum.Parse<ControlSource>(r.GetString(r.GetOrdinal("Source")), ignoreCase: true),
            Definition = definition,
            Version = r.GetInt32(r.GetOrdinal("Version")),
            CreatedAt = DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal("CreatedAt")), DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal("UpdatedAt")), DateTimeKind.Utc)
        };

        if (!r.IsDBNull(r.GetOrdinal("RunId")))
            control.LastRun = SqlControlRunRepository.MapRun(r, id);

        return control;
    }

    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
