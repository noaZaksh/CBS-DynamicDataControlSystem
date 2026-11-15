namespace DataControls.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>חיבור לניהול הגדרות (סכמת ctl) - משתמש dc_app.</summary>
    public string ControlStoreConnection { get; set; } = string.Empty;

    /// <summary>חיבור להרצת בקרות - משתמש dc_reader עם הרשאות SELECT בלבד על הנתונים העסקיים.</summary>
    public string DataReaderConnection { get; set; } = string.Empty;

    /// <summary>זמן מקסימלי להרצת בקרה (שניות).</summary>
    public int QueryTimeoutSeconds { get; set; } = 30;

    /// <summary>זמן שמירת מילון הנתונים במטמון (שניות).</summary>
    public int MetadataCacheSeconds { get; set; } = 300;

    /// <summary>סכמות שנסרקות בגילוי סכמה אוטומטי.</summary>
    public string[] DiscoverySchemas { get; set; } = { "hr" };
}
