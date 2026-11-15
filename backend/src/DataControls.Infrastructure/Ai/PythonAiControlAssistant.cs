using System.Net.Http.Json;
using System.Text.Json;
using DataControls.Application.Ai;
using DataControls.Application.Common;
using Microsoft.Extensions.Logging;

namespace DataControls.Infrastructure.Ai;

public sealed class AiServiceOptions
{
    public const string Section = "AiService";
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// מתאם HTTP לשירות ה-AI (Python / FastAPI).
/// השירות אחראי על בחירת המודל, בניית ה-Prompt ופענוח התשובה; .NET לא תלוי באף ספק AI.
/// </summary>
public sealed class PythonAiControlAssistant : IAiControlAssistant
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<PythonAiControlAssistant> _logger;

    public PythonAiControlAssistant(HttpClient http, ILogger<PythonAiControlAssistant> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AiInterpretation> InterpretAsync(string text, SchemaContext schema, CancellationToken ct)
    {
        var response = await PostAsync("v1/interpret", new { text, schema }, ct);
        return await ReadAsync<AiInterpretation>(response, ct);
    }

    public async Task<IReadOnlyList<AiInterpretation>> SuggestControlsAsync(SchemaContext schema, int max, CancellationToken ct)
    {
        var response = await PostAsync("v1/suggest-controls", new { schema, max }, ct);
        var body = await ReadAsync<SuggestionsEnvelope>(response, ct);
        return body.Suggestions;
    }

    private sealed class SuggestionsEnvelope
    {
        public List<AiInterpretation> Suggestions { get; set; } = new();
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object payload, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(path, payload, Json, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("AI service returned {Status}: {Detail}", (int)response.StatusCode, detail);
                throw new AiServiceException("שירות ה-AI החזיר שגיאה. נסו שוב או הגדירו את הבקרה ידנית.");
            }
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AI service is unreachable");
            throw new AiServiceException("שירות ה-AI אינו זמין כרגע. ניתן להגדיר את הבקרה ידנית.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "AI service timed out");
            throw new AiServiceException("שירות ה-AI לא הגיב בזמן. נסו שוב.", ex);
        }
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                   ?? throw new AiServiceException("שירות ה-AI החזיר תשובה ריקה");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "AI service response does not match the contract");
            throw new AiServiceException("תשובת שירות ה-AI אינה בפורמט המוסכם", ex);
        }
    }
}
