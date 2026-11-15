using System.Data;
using DataControls.Application.Abstractions;
using DataControls.Application.Metadata;
using DataControls.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure.Metadata;

/// <summary>
/// סורק את קטלוג ה-DB ומשווה למילון הנתונים: אילו טבלאות/עמודות/מפתחות זרים עדיין לא ממופים.
/// מאפשר להרחיב את המערכת לטבלאות חדשות בלי לגעת בקוד - רק להוסיף תוויות עסקיות.
/// (זהו גם המידע שבעתיד יוזן ל-AI כדי שיציע תוויות ובקרות באופן יזום.)
/// </summary>
public sealed class SqlSchemaDiscoveryService : ISchemaDiscoveryService
{
    private readonly DatabaseOptions _options;
    private readonly IMetadataProvider _metadata;

    public SqlSchemaDiscoveryService(IOptions<DatabaseOptions> options, IMetadataProvider metadata)
    {
        _options = options.Value;
        _metadata = metadata;
    }

    public async Task<SchemaDiscoveryResult> DiscoverAsync(CancellationToken ct)
    {
        var model = await _metadata.GetModelAsync(ct);

        var mappedObjects = model.Entities
            .Select(e => $"{e.SourceSchema}.{e.SourceObject}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mappedColumns = model.Entities
            .SelectMany(e => e.Fields.Values.Select(f => $"{e.SourceSchema}.{e.SourceObject}.{f.ColumnName}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        const string sql = """
            SELECT s.name AS SchemaName, o.name AS ObjectName, c.name AS ColumnName, t.name AS TypeName, c.is_nullable
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            JOIN sys.columns c ON c.object_id = o.object_id
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE o.type IN ('U', 'V') AND s.name IN (SELECT value FROM STRING_SPLIT(@schemas, ','))
            ORDER BY s.name, o.name, c.column_id;

            SELECT ps.name, po.name, pc.name, rs.name, ro.name, rc.name
            FROM sys.foreign_key_columns fkc
            JOIN sys.objects po ON po.object_id = fkc.parent_object_id
            JOIN sys.schemas ps ON ps.schema_id = po.schema_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.objects ro ON ro.object_id = fkc.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE ps.name IN (SELECT value FROM STRING_SPLIT(@schemas, ','));
            """;

        await using var conn = new SqlConnection(_options.ControlStoreConnection);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@schemas", SqlDbType.NVarChar, 400) { Value = string.Join(',', _options.DiscoverySchemas) });
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var columns = new List<DiscoveredColumn>();
        var unmappedObjects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await r.ReadAsync(ct))
        {
            var schema = r.GetString(0);
            var obj = r.GetString(1);
            var column = r.GetString(2);
            var objectKey = $"{schema}.{obj}";
            var isMapped = mappedColumns.Contains($"{objectKey}.{column}");

            if (!mappedObjects.Contains(objectKey)) unmappedObjects.Add(objectKey);
            if (isMapped) continue;

            var sqlType = r.GetString(3);
            columns.Add(new DiscoveredColumn(schema, obj, column, sqlType, r.GetBoolean(4),
                char.ToLowerInvariant(column[0]) + column[1..], MapType(sqlType), false));
        }

        // קשר נחשב ממופה אם קיים קשר במילון עם אותן עמודות (בכל אחד מהכיוונים)
        var mappedRelations = model.Entities.SelectMany(e => e.Relations.Values.Select(rel =>
        {
            var to = model.GetEntity(rel.ToEntityKey);
            return $"{e.SourceObject}.{rel.FromColumn}->{to.SourceObject}.{rel.ToColumn}";
        })).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fks = new List<DiscoveredForeignKey>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var fk = new DiscoveredForeignKey(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), false);
            var forward = $"{fk.FromObject}.{fk.FromColumn}->{fk.ToObject}.{fk.ToColumn}";
            var backward = $"{fk.ToObject}.{fk.ToColumn}->{fk.FromObject}.{fk.FromColumn}";
            if (!mappedRelations.Contains(forward) && !mappedRelations.Contains(backward))
                fks.Add(fk);
        }

        return new SchemaDiscoveryResult(columns, fks, unmappedObjects.ToList());
    }

    private static string MapType(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => "number",
        "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => "date",
        "bit" => "boolean",
        _ => "string"
    };
}
