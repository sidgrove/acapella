using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Abstractions;

namespace Murmur.Speech;

/// <summary>
/// TypeSafe's Jev, a "System One" decision model, through its <c>POST /v1/systemone</c> API.
/// </summary>
/// <remarks>
/// <para>
/// Jev evaluates a piece of state against typed questions and returns a probability for
/// each yes/no and a distribution over each choice, in about a tenth of a second and for
/// a few hundredths of a penny. It cannot write text, so it sits beside Gemini rather than
/// replacing it: the questions the app currently settles with a regex (is this a send
/// command, does the sentence carry on across a cut, is "period" the noun) are the ones it
/// is for.
/// </para>
/// <para>
/// Reached through Vercel's AI Gateway by default, which speaks the same request and
/// response shapes at a different base URL and with the gateway's own key; TypeSafe's own
/// endpoint works by changing <see cref="DefaultBaseUrl"/> to <c>https://api.typesafe.ai</c>
/// and the model to <c>jev-latest</c>.
/// </para>
/// </remarks>
public sealed class JevClient : IDecisionModel, IDisposable
{
    /// <summary>Vercel AI Gateway's TypeSafe-compatible base.</summary>
    public const string DefaultBaseUrl = "https://ai-gateway.vercel.sh/typesafe";

    /// <summary>The model id at the default base.</summary>
    public const string DefaultModel = "typesafe-ai/jev";

    /// <summary>Environment variable consulted when no key is set in the app.</summary>
    public const string ApiKeyEnvironmentVariable = "AI_GATEWAY_API_KEY";

    /// <summary>Decisions are meant to be quick; anything slower is not worth waiting for.</summary>
    public static TimeSpan Deadline { get; } = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly string _model;
    private readonly Uri _endpoint;

    /// <summary>Creates a client that reads the key each call, so a key pasted into Settings works at once.</summary>
    public JevClient(Func<string?> apiKey, string? baseUrl = null, string? model = null, HttpMessageHandler? handler = null)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        var root = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
        _endpoint = new Uri(root + "/v1/systemone");
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = Deadline;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppPaths.ProductName, "1.0"));
    }

    /// <inheritdoc />
    public string Name => _model;

    /// <inheritdoc />
    public string? LastError { get; private set; }

    /// <summary>The key in use: the configured one, else the environment variable.</summary>
    public static string? ResolveKey(string? configured) =>
        !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) is { Length: > 0 } fromEnv ? fromEnv.Trim() : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Decision>?> DecideAsync(string state, IReadOnlyList<DecisionQuestion> questions, CancellationToken cancellationToken)
    {
        var key = ResolveKey(_apiKey());
        if (key is null)
        {
            LastError = "no API key";
            return null;
        }
        if (questions.Count == 0 || string.IsNullOrWhiteSpace(state)) return [];

        var wire = new Dictionary<string, Question>(questions.Count);
        foreach (var question in questions)
        {
            wire[question.Key] = new Question(question.Kind, question.Instructions, question.Criteria);
        }
        var body = JsonSerializer.Serialize(new Request(_model, state, wire), JevJsonContext.Default.Request);

        using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        try
        {
            using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"{(int)response.StatusCode} {response.ReasonPhrase}: {Excerpt(payload)}";
                return null;
            }

            var parsed = JsonSerializer.Deserialize(payload, JevJsonContext.Default.Response);
            if (parsed?.Answers is null)
            {
                LastError = "no answers in the reply";
                return null;
            }

            var decisions = new List<Decision>(questions.Count);
            foreach (var question in questions)
            {
                if (!parsed.Answers.TryGetValue(question.Key, out var answer)) continue;
                decisions.Add(new Decision(question.Key, answer.Noul, answer.Choice, answer.Confidence));
            }
            LastError = null;
            return decisions;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            LastError = e is TaskCanceledException && !cancellationToken.IsCancellationRequested ? $"no reply within {Deadline.TotalSeconds:0} s" : e.Message;
            return null;
        }
    }

    private static string Excerpt(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("message", out var msg)) return msg.GetString() ?? string.Empty;
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var inner)) return inner.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            // Not JSON; fall through.
        }
        return payload.Length > 200 ? payload[..200] : payload;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    // ---- Wire shapes. Source-generated so they survive single-file publishing. ----

    internal sealed record Request(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("questions")] Dictionary<string, Question> Questions);

    internal sealed record Question(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("instructions")] string Instructions,
        [property: JsonPropertyName("criteria")] IReadOnlyDictionary<string, string?>? Criteria);

    internal sealed record Response([property: JsonPropertyName("answers")] Dictionary<string, Answer>? Answers);

    internal sealed record Answer(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("noul")] double? Noul,
        [property: JsonPropertyName("choice")] string? Choice,
        [property: JsonPropertyName("confidence")] double? Confidence);
}

/// <summary>Source-generated JSON for the Jev wire shapes.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JevClient.Request))]
[JsonSerializable(typeof(JevClient.Response))]
internal sealed partial class JevJsonContext : JsonSerializerContext;
