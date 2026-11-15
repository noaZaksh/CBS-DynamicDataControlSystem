using System.Data;
using DataControls.Application.Abstractions;
using DataControls.Domain.Controls;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure.Persistence;

public sealed class SqlControlRunRepository : IControlRunRepository
{
    private readonly DatabaseOptions _options;

    public SqlControlRunRepository(IOptions<DatabaseOptions> options) => _options = options.Value;

    public async Task<long> AddAsync(ControlRunSummary run, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO ctl.ControlRuns (ControlId, ControlVersion, StartedAt, DurationMs, Status, TotalRecords, ExceptionCount, ErrorMessage)
            OUTPUT INSERTED.RunId
            VALUES (@controlId, @version, @startedAt, @durationMs, @status, @total, @exceptions, @error);
            """;
        await using var conn = new SqlConnection(_options.ControlStoreConnection);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@controlId", SqlDbType.Int) { Value = run.ControlId });
        cmd.Parameters.Add(new SqlParameter("@version", SqlDbType.Int) { Value = run.ControlVersion });
        cmd.Parameters.Add(new SqlParameter("@startedAt", SqlDbType.DateTime2) { Value = run.StartedAt });
        cmd.Parameters.Add(new SqlParameter("@durationMs", SqlDbType.Int) { Value = run.DurationMs });
        cmd.Parameters.Add(new SqlParameter("@status", SqlDbType.VarChar, 10) { Value = run.Status.ToString() });
        cmd.Parameters.Add(new SqlParameter("@total", SqlDbType.Int) { Value = (object?)run.TotalRecords ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@exceptions", SqlDbType.Int) { Value = (object?)run.ExceptionCount ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@error", SqlDbType.NVarChar, 1000)
        {
            Value = run.ErrorMessage is null ? DBNull.Value : run.ErrorMessage[..Math.Min(run.ErrorMessage.Length, 1000)]
        });
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<IReadOnlyList<ControlRunSummary>> ListAsync(int controlId, int take, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (@take) RunId, ControlVersion, StartedAt, DurationMs, Status, TotalRecords, ExceptionCount, ErrorMessage
            FROM ctl.ControlRuns WHERE ControlId = @controlId ORDER BY StartedAt DESC;
            """;
        await using var conn = new SqlConnection(_options.ControlStoreConnection);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@take", SqlDbType.Int) { Value = take });
        cmd.Parameters.Add(new SqlParameter("@controlId", SqlDbType.Int) { Value = controlId });

        var list = new List<ControlRunSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRun(reader, controlId));
        return list;
    }

    internal static ControlRunSummary MapRun(SqlDataReader r, int controlId)
    {
        int? NullableInt(string name) => r.IsDBNull(r.GetOrdinal(name)) ? null : r.GetInt32(r.GetOrdinal(name));

        return new ControlRunSummary
        {
            RunId = r.GetInt64(r.GetOrdinal("RunId")),
            ControlId = controlId,
            ControlVersion = r.GetInt32(r.GetOrdinal("ControlVersion")),
            StartedAt = DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal("StartedAt")), DateTimeKind.Utc),
            DurationMs = r.GetInt32(r.GetOrdinal("DurationMs")),
            Status = Enum.Parse<RunStatus>(r.GetString(r.GetOrdinal("Status")), ignoreCase: true),
            TotalRecords = NullableInt("TotalRecords"),
            ExceptionCount = NullableInt("ExceptionCount"),
            ErrorMessage = r.IsDBNull(r.GetOrdinal("ErrorMessage")) ? null : r.GetString(r.GetOrdinal("ErrorMessage"))
        };
    }
}
