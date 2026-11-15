namespace DataControls.Domain.Metadata;

/// <summary>סוג נתון עסקי של שדה. קובע אילו אופרטורים מותרים ואיך ממירים ערכים.</summary>
public enum FieldDataType
{
    String,
    Number,
    Date,
    Boolean
}

public enum RelationCardinality
{
    /// <summary>לכל רשומה במקור יש לכל היותר רשומה אחת ביעד (עובד → מחלקה). מאפשר "נתיב שדה" (department.isActive).</summary>
    ManyToOne,

    /// <summary>לרשומה במקור יכולות להיות הרבה רשומות ביעד (מחלקה → עובדים). נבדק באמצעות תנאי "קיים / לא קיים".</summary>
    OneToMany
}

/// <summary>שדה עסקי במילון הנתונים.</summary>
public sealed class FieldMeta
{
    public required string Key { get; init; }
    public required string ColumnName { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required FieldDataType DataType { get; init; }
    public bool IsBusinessRequired { get; init; }
    public bool ShowInResults { get; init; }
    public IReadOnlyList<string>? AllowedValues { get; init; }
    public IReadOnlyList<string> Synonyms { get; init; } = Array.Empty<string>();
    public int SortOrder { get; init; }
}

/// <summary>קשר עסקי בין שתי ישויות. ההצטרפות היא תמיד  To.ToColumn = From.FromColumn.</summary>
public sealed class RelationMeta
{
    public required string Key { get; init; }
    public required string FromEntityKey { get; init; }
    public required string ToEntityKey { get; init; }
    public required string FromColumn { get; init; }
    public required string ToColumn { get; init; }
    public required RelationCardinality Cardinality { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
}

/// <summary>ישות עסקית (עובד, מחלקה...) הממופה לטבלה או View.</summary>
public sealed class EntityMeta
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string DisplayNamePlural { get; init; }
    public string? Description { get; init; }
    public required string SourceSchema { get; init; }
    public required string SourceObject { get; init; }
    public required string PrimaryKeyColumn { get; init; }
    public string? TitleFieldKey { get; init; }
    public IReadOnlyList<string> Synonyms { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, FieldMeta> Fields { get; init; } =
        new Dictionary<string, FieldMeta>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, RelationMeta> Relations { get; init; } =
        new Dictionary<string, RelationMeta>(StringComparer.OrdinalIgnoreCase);

    public FieldMeta? TitleField =>
        TitleFieldKey is not null && Fields.TryGetValue(TitleFieldKey, out var f) ? f : null;
}

/// <summary>
/// תמונת מצב של כל מילון הנתונים. נטען מה-DB (ctl.Entities/Fields/Relations) ונשמר במטמון.
/// זהו המקור היחיד שממנו המנוע לומד על טבלאות ועמודות - אין שמות טכניים בקוד.
/// </summary>
public sealed class DataModel
{
    private readonly Dictionary<string, EntityMeta> _entities;

    public DataModel(IEnumerable<EntityMeta> entities)
    {
        _entities = entities.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<EntityMeta> Entities => _entities.Values;

    public bool TryGetEntity(string key, out EntityMeta entity) =>
        _entities.TryGetValue(key, out entity!);

    public EntityMeta GetEntity(string key) =>
        _entities.TryGetValue(key, out var e)
            ? e
            : throw new KeyNotFoundException($"Entity '{key}' is not defined in the data model.");
}
