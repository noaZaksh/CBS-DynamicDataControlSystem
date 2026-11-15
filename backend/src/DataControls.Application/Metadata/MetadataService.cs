using DataControls.Application.Abstractions;
using DataControls.Domain.Definitions;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Metadata;

// ---- DTOs: מה שה-Frontend רואה. במכוון אין כאן שמות טבלאות/עמודות טכניים. ----

public sealed record FieldDto(string Key, string DisplayName, string? Description, string DataType,
    bool IsBusinessRequired, IReadOnlyList<string>? AllowedValues);

public sealed record RelationDto(string Key, string DisplayName, string? Description, string TargetEntity, string Cardinality);

public sealed record EntityDto(string Key, string DisplayName, string DisplayNamePlural, string? Description,
    string? TitleFieldKey, IReadOnlyList<FieldDto> Fields, IReadOnlyList<RelationDto> Relations);

public sealed record OperatorDto(string Operator, string Label, string? DateLabel, string Arity,
    IReadOnlyList<string> AppliesTo, bool AllowsFieldComparison);

public sealed record DataModelDto(IReadOnlyList<EntityDto> Entities, IReadOnlyList<OperatorDto> Operators);

// ---- גילוי סכמה: הצעות להרחבת מילון הנתונים מתוך מבנה ה-DB ----

public sealed record DiscoveredColumn(string Schema, string Object, string Column, string SqlType, bool IsNullable,
    string SuggestedFieldKey, string SuggestedDataType, bool IsMapped);

public sealed record DiscoveredForeignKey(string FromSchema, string FromObject, string FromColumn,
    string ToSchema, string ToObject, string ToColumn, bool IsMapped);

public sealed record SchemaDiscoveryResult(IReadOnlyList<DiscoveredColumn> UnmappedColumns,
    IReadOnlyList<DiscoveredForeignKey> UnmappedForeignKeys, IReadOnlyList<string> UnmappedObjects);

public interface ISchemaDiscoveryService
{
    Task<SchemaDiscoveryResult> DiscoverAsync(CancellationToken ct);
}

public sealed class MetadataService
{
    private readonly IMetadataProvider _provider;

    public MetadataService(IMetadataProvider provider) => _provider = provider;

    public async Task<DataModelDto> GetModelAsync(CancellationToken ct)
    {
        var model = await _provider.GetModelAsync(ct);
        return new DataModelDto(ToEntityDtos(model), GetOperators());
    }

    public static IReadOnlyList<EntityDto> ToEntityDtos(DataModel model) =>
        model.Entities
            .OrderBy(e => e.DisplayName)
            .Select(e => new EntityDto(
                e.Key, e.DisplayName, e.DisplayNamePlural, e.Description, e.TitleFieldKey,
                e.Fields.Values.OrderBy(f => f.SortOrder).ThenBy(f => f.DisplayName)
                    .Select(f => new FieldDto(f.Key, f.DisplayName, f.Description, Lower(f.DataType), f.IsBusinessRequired, f.AllowedValues))
                    .ToList(),
                e.Relations.Values.OrderBy(r => r.DisplayName)
                    .Select(r => new RelationDto(r.Key, r.DisplayName, r.Description, r.ToEntityKey, r.Cardinality.ToString()))
                    .ToList()))
            .ToList();

    public static IReadOnlyList<OperatorDto> GetOperators() =>
        OperatorCatalog.Operators.Select(o => new OperatorDto(
            Camel(o.Operator.ToString()), o.Label, o.DateLabel, Camel(o.Arity.ToString()),
            o.AppliesTo.Select(Lower).ToList(), o.AllowsFieldComparison)).ToList();

    public void Invalidate() => _provider.Invalidate();

    private static string Lower(FieldDataType t) => t.ToString().ToLowerInvariant();
    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
