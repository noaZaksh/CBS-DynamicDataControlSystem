using System.Text.Json;
using DataControls.Application.Abstractions;
using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Persistence;
using DataControls.Infrastructure.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure.Metadata;

/// <summary>
/// טוען את מילון הנתונים (ctl.Entities / Fields / Relations) ושומר במטמון.
/// רשומות עם מזהים טכניים לא בטוחים או הפניות שבורות נדחות ומתועדות בלוג.
/// </summary>
public sealed class SqlMetadataProvider : IMetadataProvider
{
    private const string CacheKey = "data-model";

    private readonly DatabaseOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SqlMetadataProvider> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public SqlMetadataProvider(IOptions<DatabaseOptions> options, IMemoryCache cache, ILogger<SqlMetadataProvider> logger)
    {
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<DataModel> GetModelAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out DataModel? cached) && cached is not null)
            return cached;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached) && cached is not null)
                return cached;

            var model = await LoadAsync(ct);
            _cache.Set(CacheKey, model, TimeSpan.FromSeconds(_options.MetadataCacheSeconds));
            return model;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void Invalidate() => _cache.Remove(CacheKey);

    private async Task<DataModel> LoadAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT EntityId, EntityKey, DisplayName, DisplayNamePlural, Description, SourceSchema, SourceObject, PrimaryKeyColumn, TitleFieldKey, Synonyms
            FROM ctl.Entities WHERE IsActive = 1;

            SELECT EntityId, FieldKey, ColumnName, DisplayName, Description, DataType, IsBusinessRequired, ShowInResults, AllowedValues, Synonyms, SortOrder
            FROM ctl.Fields WHERE IsActive = 1;

            SELECT r.FromEntityId, r.RelationKey, te.EntityKey AS ToEntityKey, r.FromColumn, r.ToColumn, r.Cardinality, r.DisplayName, r.Description
            FROM ctl.Relations r JOIN ctl.Entities te ON te.EntityId = r.ToEntityId
            WHERE r.IsActive = 1 AND te.IsActive = 1;
            """;

        await using var conn = new SqlConnection(_options.ControlStoreConnection);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var rawEntities = new Dictionary<int, (string Key, Func<Dictionary<string, FieldMeta>, Dictionary<string, RelationMeta>, EntityMeta> Factory)>();
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            var key = r.GetString(1);
            var schema = r.GetString(5);
            var obj = r.GetString(6);
            var pk = r.GetString(7);
            if (!SqlIdentifier.IsSafe(schema) || !SqlIdentifier.IsSafe(obj) || !SqlIdentifier.IsSafe(pk))
            {
                _logger.LogError("Entity {Entity} has unsafe technical identifiers and is ignored", key);
                continue;
            }

            var displayName = r.GetString(2);
            var plural = r.GetString(3);
            var description = NullableString(r, 4);
            var title = NullableString(r, 8);
            var synonyms = SplitSynonyms(NullableString(r, 9));

            rawEntities[id] = (key, (fields, relations) => new EntityMeta
            {
                Key = key,
                DisplayName = displayName,
                DisplayNamePlural = plural,
                Description = description,
                SourceSchema = schema,
                SourceObject = obj,
                PrimaryKeyColumn = pk,
                TitleFieldKey = title,
                Synonyms = synonyms,
                Fields = fields,
                Relations = relations
            });
        }

        var fieldsByEntity = rawEntities.Keys.ToDictionary(k => k, _ => new Dictionary<string, FieldMeta>(StringComparer.OrdinalIgnoreCase));
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var entityId = r.GetInt32(0);
            if (!fieldsByEntity.TryGetValue(entityId, out var fields)) continue;

            var key = r.GetString(1);
            var column = r.GetString(2);
            if (!SqlIdentifier.IsSafe(column) || !Enum.TryParse<FieldDataType>(r.GetString(5), true, out var type))
            {
                _logger.LogError("Field {Field} has an unsafe column name or unknown type and is ignored", key);
                continue;
            }

            fields[key] = new FieldMeta
            {
                Key = key,
                ColumnName = column,
                DisplayName = r.GetString(3),
                Description = NullableString(r, 4),
                DataType = type,
                IsBusinessRequired = r.GetBoolean(6),
                ShowInResults = r.GetBoolean(7),
                AllowedValues = ParseAllowedValues(NullableString(r, 8)),
                Synonyms = SplitSynonyms(NullableString(r, 9)),
                SortOrder = r.GetInt32(10)
            };
        }

        var relationsByEntity = rawEntities.Keys.ToDictionary(k => k, _ => new Dictionary<string, RelationMeta>(StringComparer.OrdinalIgnoreCase));
        var entityKeys = rawEntities.Values.Select(v => v.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var fromId = r.GetInt32(0);
            if (!relationsByEntity.TryGetValue(fromId, out var relations)) continue;

            var key = r.GetString(1);
            var toKey = r.GetString(2);
            var fromColumn = r.GetString(3);
            var toColumn = r.GetString(4);
            if (!entityKeys.Contains(toKey) || !SqlIdentifier.IsSafe(fromColumn) || !SqlIdentifier.IsSafe(toColumn) ||
                !Enum.TryParse<RelationCardinality>(r.GetString(5), true, out var cardinality))
            {
                _logger.LogError("Relation {Relation} is invalid and is ignored", key);
                continue;
            }

            relations[key] = new RelationMeta
            {
                Key = key,
                FromEntityKey = rawEntities[fromId].Key,
                ToEntityKey = toKey,
                FromColumn = fromColumn,
                ToColumn = toColumn,
                Cardinality = cardinality,
                DisplayName = r.GetString(6),
                Description = NullableString(r, 7)
            };
        }

        var entities = rawEntities.Select(kv => kv.Value.Factory(fieldsByEntity[kv.Key], relationsByEntity[kv.Key])).ToList();
        _logger.LogInformation("Data dictionary loaded: {Entities} entities, {Fields} fields",
            entities.Count, entities.Sum(e => e.Fields.Count));
        return new DataModel(entities);
    }

    private static string? NullableString(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static IReadOnlyList<string> SplitSynonyms(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string>? ParseAllowedValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<string>>(json); }
        catch (JsonException) { return null; }
    }
}
