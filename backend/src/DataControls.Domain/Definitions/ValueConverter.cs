using System.Globalization;
using System.Text.Json;
using DataControls.Domain.Metadata;

namespace DataControls.Domain.Definitions;

/// <summary>
/// המרה של ערך JSON (מהמשתמש או מה-AI) לערך CLR מטיפוס התואם לשדה.
/// משמש גם את הוולידציה וגם את בניית הפרמטרים לשאילתה - כך שאין פער בין השניים.
/// </summary>
public static class ValueConverter
{
    private static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss.fffZ", "dd/MM/yyyy", "d/M/yyyy" };

    public static bool TryConvert(JsonElement? element, FieldDataType type, out object? value, out string? error)
    {
        value = null;
        error = null;

        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            error = "חסר ערך";
            return false;
        }

        var e = element.Value;
        switch (type)
        {
            case FieldDataType.Number:
                if (e.ValueKind == JsonValueKind.Number && e.TryGetDecimal(out var d))
                {
                    value = d;
                    return true;
                }
                if (e.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(e.GetString()!.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var ds))
                {
                    value = ds;
                    return true;
                }
                error = "יש להזין מספר";
                return false;

            case FieldDataType.Date:
                if (e.ValueKind == JsonValueKind.String &&
                    DateTime.TryParseExact(e.GetString()!.Trim(), DateFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                {
                    value = dt.Date;
                    return true;
                }
                error = "יש להזין תאריך תקין (yyyy-MM-dd)";
                return false;

            case FieldDataType.Boolean:
                if (e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    value = e.GetBoolean();
                    return true;
                }
                if (e.ValueKind == JsonValueKind.String)
                {
                    var s = e.GetString()!.Trim().ToLowerInvariant();
                    if (s is "true" or "כן" or "1") { value = true; return true; }
                    if (s is "false" or "לא" or "0") { value = false; return true; }
                }
                error = "יש לבחור כן / לא";
                return false;

            case FieldDataType.String:
                if (e.ValueKind == JsonValueKind.String) { value = e.GetString(); return true; }
                if (e.ValueKind == JsonValueKind.Number) { value = e.GetRawText(); return true; }
                error = "יש להזין טקסט";
                return false;

            default:
                error = "סוג נתון לא נתמך";
                return false;
        }
    }
}
