using System.Net;
using System.Text;
using System.Text.Json;
using Murmur.Abstractions;
using Murmur.Speech;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>The Claude clean-up asks in Gemini's words and never types a reply that did not finish.</summary>
public sealed class ClaudeCleanerTests
{
    private const string Tidy = """{"content":[{"type":"text","text":"Tidy text"}],"stop_reason":"end_turn"}""";

    [Fact]
    public async Task The_request_is_shaped_for_haiku_with_thinking_off()
    {
        var handler = new Handler(Tidy);
        using var cleaner = new ClaudeCleaner(() => "test-key", handler: handler, customInstructions: () => "No exclamation marks", vocabulary: () => ["Xero"]);

        (await cleaner.CleanAsync("um tidy text", CancellationToken.None)).ShouldBe("Tidy text");

        handler.Uri.ShouldBe("https://api.anthropic.com/v1/messages");
        handler.Headers["x-api-key"].ShouldBe("test-key");
        handler.Headers["anthropic-version"].ShouldBe("2023-06-01");

        var body = JsonDocument.Parse(handler.Body.ShouldNotBeNull()).RootElement;
        body.GetProperty("model").GetString().ShouldBe("claude-haiku-5-5");
        body.GetProperty("max_tokens").GetInt32().ShouldBe(ClaudeCleaner.MaxTokens);
        body.GetProperty("thinking").GetProperty("type").GetString().ShouldBe("disabled");
        body.GetProperty("output_config").GetProperty("effort").GetString().ShouldBe("low");
        body.TryGetProperty("temperature", out _).ShouldBeFalse("any value but the default is a 400 on this model");

        var system = body.GetProperty("system")[0];
        system.GetProperty("text").GetString().ShouldBe(GeminiCleaner.Prompt("No exclamation marks", ["Xero"]));
        system.GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");

        var messages = body.GetProperty("messages");
        messages.GetArrayLength().ShouldBe(1);
        messages[0].GetProperty("role").GetString().ShouldBe("user");
        messages[0].GetProperty("content").GetString().ShouldBe("um tidy text");
    }

    [Fact]
    public async Task The_screen_and_both_readings_are_framed_as_they_are_for_gemini()
    {
        var handler = new Handler(Tidy);
        using var cleaner = new ClaudeCleaner(() => "test-key", handler: handler);
        var screen = new ScreenContext(new FocusedWindow("OUTLOOK", "Inbox"), "Siobhan here");

        await cleaner.CleanTwoReadingsAsync("thanks shivon", "thanks, siobhan", screen, CancellationToken.None);

        JsonDocument.Parse(handler.Body.ShouldNotBeNull()).RootElement.GetProperty("messages")[0].GetProperty("content").GetString()
            .ShouldBe(GeminiCleaner.Input("thanks shivon", null, mayStopMidSentence: false, "thanks, siobhan", screen));
    }

    [Fact]
    public async Task The_answer_is_the_text_blocks_wherever_they_come()
    {
        var handler = new Handler("""{"content":[{"type":"thinking","thinking":"","signature":"abc"},{"type":"text","text":"Tidy text"}],"stop_reason":"end_turn"}""");
        using var cleaner = new ClaudeCleaner(() => "test-key", handler: handler);

        (await cleaner.CleanAsync("tidy text", CancellationToken.None)).ShouldBe("Tidy text");
    }

    [Theory]
    [InlineData("max_tokens")]
    [InlineData("refusal")]
    public async Task A_reply_that_did_not_finish_cleanly_is_never_typed(string stopReason)
    {
        var body = JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = "Tidy te" } }, stop_reason = stopReason });
        using var cleaner = new ClaudeCleaner(() => "test-key", handler: new Handler(body));

        (await cleaner.CleanAsync("tidy text", CancellationToken.None)).ShouldBeNull();
        cleaner.LastError.ShouldNotBeNull().ShouldContain(stopReason);
    }

    [Fact]
    public async Task An_api_error_is_reported_in_its_own_words()
    {
        var handler = new Handler("""{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", HttpStatusCode.Unauthorized);
        using var cleaner = new ClaudeCleaner(() => "wrong", handler: handler);

        (await cleaner.CleanAsync("tidy text", CancellationToken.None)).ShouldBeNull();
        cleaner.LastError.ShouldNotBeNull().ShouldContain("401");
        cleaner.LastError.ShouldContain("invalid x-api-key");
    }

    [Fact]
    public async Task No_key_sends_nothing()
    {
        var before = Environment.GetEnvironmentVariable(ClaudeCleaner.ApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(ClaudeCleaner.ApiKeyEnvironmentVariable, null);
        try
        {
            var handler = new Handler(Tidy);
            using var cleaner = new ClaudeCleaner(() => null, handler: handler);

            (await cleaner.CleanAsync("tidy text", CancellationToken.None)).ShouldBeNull();
            cleaner.LastError.ShouldBe("no API key");
            handler.Body.ShouldBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClaudeCleaner.ApiKeyEnvironmentVariable, before);
        }
    }

    [Theory]
    [InlineData("claude-haiku-5-5", typeof(ClaudeCleaner))]
    [InlineData(" Claude-Sonnet-5-5 ", typeof(ClaudeCleaner))]
    [InlineData("gemini-2.5-flash", typeof(GeminiCleaner))]
    [InlineData(null, typeof(GeminiCleaner))]
    public void The_model_id_picks_the_client(string? model, Type expected)
    {
        var cleaner = TranscriptCleaners.Create(model, () => "gemini", () => "anthropic");
        using var owned = cleaner as IDisposable;

        cleaner.ShouldBeOfType(expected);
    }

    private sealed class Handler(string reply, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Uri { get; private set; }
        public Dictionary<string, string> Headers { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri?.ToString();
            foreach (var header in request.Headers) Headers[header.Key] = string.Join(",", header.Value);
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }
}
