using System.Data;
using DataControls.Application.Common;
using DataControls.Application.Execution;
using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure.Sql;

/// <summary>
/// מריץ שאילתה מתורגמת. שכבות הגנה:
///  1. חיבור עם משתמש DB בעל הרשאת SELECT בלבד (dc_reader).
///  2. בדיקה שהטקסט שנוצר הוא SELECT בלבד.
///  3. ריצה בתוך טרנזקציה שתמיד מבוטלת (Rollback).
///  4. Timeout ופרמטרים מוקלדים.
/// </summary>
public sealed class SqlQueryExecutor : IQueryExecutor
{
    private static readonly string[] ForbiddenKeywords =
        { "INSERT ", "UPDATE ", "DELETE ", "MERGE ", "DROP ", "ALTER ", "CREATE ", "EXEC ", "EXECUTE ", "TRUNCATE ", "GRANT ", "INTO " };

    private readonly DatabaseOptions _options;
    private readonly ILogger<SqlQueryExecutor> _logger;

    public SqlQueryExecutor(IOptions<DatabaseOptions> options, ILogger<SqlQueryExecutor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<QueryResult> ExecuteAsync(CompiledQuery query, CancellationToken ct)
    {
        EnsureReadOnly(query.Sql);

        try
        {
            await using var connection = new SqlConnection(_options.DataReaderConnection);
            await connection.OpenAsync(ct);
            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            try
            {
                await using var command = new SqlCommand(query.Sql, connection, tx)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = _options.QueryTimeoutSeconds
                };
                foreach (var p in query.Parameters)
                    command.Parameters.Add(ToSqlParameter(p));

                await using var reader = await command.ExecuteReaderAsync(ct);

                await reader.ReadAsync(ct);
                var total = reader.GetInt64(0);

                await reader.NextResultAsync(ct);
                await reader.ReadAsync(ct);
                var matches = reader.GetInt64(0);

                await reader.NextResultAsync(ct);
                var rows = new List<IReadOnlyDictionary<string, object?>>();
                while (await reader.ReadAsync(ct))
                {
                    var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                    for (var i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }

                return new QueryResult { TotalRecords = total, MatchCount = matches, Rows = rows };
            }
            finally
            {
                // בקרה לעולם לא משנה נתונים - גם אם משהו "השתחל", הוא מבוטל.
                await tx.RollbackAsync(CancellationToken.None);
            }
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            _logger.LogWarning(ex, "Control query timed out");
            throw new QueryExecutionException(
                $"הרצת הבקרה חרגה מהזמן המותר ({_options.QueryTimeoutSeconds} שניות). מומלץ לצמצם את התנאים או לפנות לצוות הטכני.", ex, isTimeout: true);
        }
        catch (SqlException ex) when (ex.Number is 207 or 208 or 4104)
        {
            // עמודה / טבלה לא קיימת - מילון הנתונים לא מסונכרן עם ה-DB
            _logger.LogError(ex, "Control query references a missing object. Data dictionary may be out of sync");
            throw new QueryExecutionException("אחד השדות או הטבלאות שבבקרה אינו קיים עוד בבסיס הנתונים. יש לעדכן את מילון הנתונים.", ex);
        }
        catch (SqlException ex) when (ex.Number is 229 or 230)
        {
            _logger.LogError(ex, "Permission denied for control query");
            throw new QueryExecutionException("אין הרשאת קריאה לנתונים הנדרשים לבקרה זו.", ex);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Control query failed (SQL error {Number})", ex.Number);
            throw new QueryExecutionException("אירעה שגיאה בהרצת הבקרה מול בסיס הנתונים.", ex);
        }
    }

    internal static void EnsureReadOnly(string sql)
    {
        var statements = sql.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("--", StringComparison.Ordinal))
            .ToList();

        if (statements.Count == 0 || !statements[0].StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only SELECT statements may be executed by the control engine");

        var upper = " " + string.Join(' ', statements).ToUpperInvariant();
        foreach (var keyword in ForbiddenKeywords)
        {
            if (upper.Contains(" " + keyword, StringComparison.Ordinal))
                throw new InvalidOperationException($"Generated SQL contains a forbidden keyword: {keyword.Trim()}");
        }
    }

    private static SqlParameter ToSqlParameter(QueryParameter p)
    {
        var parameter = p.DataType switch
        {
            FieldDataType.String => new SqlParameter(p.Name, SqlDbType.NVarChar, 4000),
            FieldDataType.Number => new SqlParameter(p.Name, SqlDbType.Decimal) { Precision = 38, Scale = ScaleOf(p.Value) },
            FieldDataType.Date => new SqlParameter(p.Name, SqlDbType.Date),
            FieldDataType.Boolean => new SqlParameter(p.Name, SqlDbType.Bit),
            _ => throw new NotSupportedException($"Unsupported parameter type {p.DataType}")
        };
        parameter.Value = p.Value ?? DBNull.Value;
        return parameter;
    }

    private static byte ScaleOf(object? value) =>
        value is decimal d ? (byte)Math.Min((decimal.GetBits(d)[3] >> 16) & 0xFF, 10) : (byte)0;
}
