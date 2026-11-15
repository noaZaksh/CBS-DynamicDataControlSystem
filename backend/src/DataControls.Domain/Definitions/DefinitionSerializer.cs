using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataControls.Domain.Definitions;

/// <summary>
/// סריאליזציה של הגדרות בקרה ל-JSON ובחזרה.
/// צמתים פולימורפיים מזוהים לפי השדה "type" (group / condition / related) בכל מיקום באובייקט.
/// </summary>
public static class DefinitionSerializer
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = 64
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new FilterNodeConverter());
        return options;
    }

    public static string Serialize(ControlDefinition definition) =>
        JsonSerializer.Serialize(definition, Options);

    /// <exception cref="JsonException">כאשר ה-JSON אינו תקין מבנית.</exception>
    public static ControlDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<ControlDefinition>(json, Options)
        ?? throw new JsonException("Empty control definition");

    public static ControlDefinition Deserialize(JsonElement json) =>
        json.Deserialize<ControlDefinition>(Options)
        ?? throw new JsonException("Empty control definition");
}

/// <summary>ממיר פולימורפי לצמתי העץ, עצמאי בסדר השדות ב-JSON.</summary>
public sealed class FilterNodeConverter : JsonConverter<FilterNode>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(FilterNode);

    public override FilterNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Filter node must be a JSON object");

        string? type = null;
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, "type", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
            {
                type = prop.Value.GetString();
                break;
            }
        }

        return type?.ToLowerInvariant() switch
        {
            "group" => root.Deserialize<GroupNode>(options),
            "condition" => root.Deserialize<ConditionNode>(options),
            "related" => root.Deserialize<RelatedNode>(options),
            null => throw new JsonException("Filter node is missing the 'type' property"),
            _ => throw new JsonException($"Unknown filter node type '{type}'")
        };
    }

    public override void Write(Utf8JsonWriter writer, FilterNode value, JsonSerializerOptions options)
    {
        var element = JsonSerializer.SerializeToElement(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString("type", JsonNamingPolicy.CamelCase.ConvertName(value.Type.ToString()));
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.NameEquals("type")) continue;
            prop.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}
