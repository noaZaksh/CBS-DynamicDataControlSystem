namespace DataControls.Domain.Metadata;

/// <summary>שדה לאחר פענוח נתיב עסקי כמו "department.manager.isActive".</summary>
public sealed class ResolvedField
{
    public required string Path { get; init; }
    public required EntityMeta OwnerEntity { get; init; }
    public required FieldMeta Field { get; init; }

    /// <summary>שרשרת הקשרים (ManyToOne בלבד) מהישות הנוכחית ועד לישות שבה נמצא השדה.</summary>
    public required IReadOnlyList<RelationMeta> Relations { get; init; }

    /// <summary>מפתח הצטרפות - נתיב הקשרים בלבד ("department.manager"), ריק לשדה של הישות עצמה.</summary>
    public string RelationPath => string.Join('.', Relations.Select(r => r.Key));

    /// <summary>תווית עסקית מלאה, למשל "מחלקה פעילה (המחלקה של העובד)".</summary>
    public string DisplayLabel => Relations.Count == 0
        ? Field.DisplayName
        : $"{Field.DisplayName} ({string.Join(" › ", Relations.Select(r => r.DisplayName))})";
}

public static class FieldPathResolver
{
    public const int MaxRelationHops = 3;

    /// <summary>
    /// מפענח נתיב שדה יחסית לישות. רק קשרי ManyToOne מותרים בנתיב (כדי שה-JOIN לא ישכפל רשומות);
    /// קשרי OneToMany נבדקים באמצעות צומת "related" (EXISTS).
    /// </summary>
    public static bool TryResolve(DataModel model, EntityMeta start, string? path, out ResolvedField? resolved, out string? error)
    {
        resolved = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "לא נבחר שדה";
            return false;
        }

        var segments = path.Split('.', StringSplitOptions.TrimEntries);
        if (segments.Length - 1 > MaxRelationHops)
        {
            error = $"הנתיב '{path}' ארוך מדי (עד {MaxRelationHops} קשרים)";
            return false;
        }

        var current = start;
        var relations = new List<RelationMeta>();

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!current.Relations.TryGetValue(segments[i], out var relation))
            {
                error = $"הקשר '{segments[i]}' אינו מוגדר עבור {current.DisplayName}";
                return false;
            }

            if (relation.Cardinality != RelationCardinality.ManyToOne)
            {
                error = $"לא ניתן להשתמש בקשר '{relation.DisplayName}' בנתיב שדה, כי לכל {current.DisplayName} יכולות להיות כמה רשומות. יש להשתמש בתנאי \"קיים / לא קיים\".";
                return false;
            }

            if (!model.TryGetEntity(relation.ToEntityKey, out var next))
            {
                error = $"הישות '{relation.ToEntityKey}' אינה מוגדרת";
                return false;
            }

            relations.Add(relation);
            current = next;
        }

        if (!current.Fields.TryGetValue(segments[^1], out var field))
        {
            error = $"השדה '{segments[^1]}' אינו מוגדר עבור {current.DisplayName}";
            return false;
        }

        resolved = new ResolvedField
        {
            Path = string.Join('.', segments),
            OwnerEntity = current,
            Field = field,
            Relations = relations
        };
        return true;
    }

    public static ResolvedField Resolve(DataModel model, EntityMeta start, string path) =>
        TryResolve(model, start, path, out var r, out var error)
            ? r!
            : throw new InvalidOperationException(error);
}
