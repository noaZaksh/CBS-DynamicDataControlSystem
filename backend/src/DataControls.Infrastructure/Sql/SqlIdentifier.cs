using System.Text.RegularExpressions;

namespace DataControls.Infrastructure.Sql;

/// <summary>
/// ציטוט מזהי SQL (טבלה/עמודה/סכמה). מזהים מגיעים *רק* ממילון הנתונים,
/// ובכל זאת נבדקים מול Whitelist קפדני ומצוטטים בסוגריים מרובעים (הגנה בעומק).
/// </summary>
public static partial class SqlIdentifier
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex SafeIdentifier();

    public static bool IsSafe(string? name) => name is not null && SafeIdentifier().IsMatch(name);

    public static string Quote(string name)
    {
        if (!IsSafe(name))
            throw new InvalidOperationException($"Unsafe SQL identifier rejected: '{name}'");
        return $"[{name}]";
    }

    public static string QuoteObject(string schema, string obj) => $"{Quote(schema)}.{Quote(obj)}";
}
