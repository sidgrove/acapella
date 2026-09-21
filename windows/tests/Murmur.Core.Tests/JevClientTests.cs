using System.Net;
using System.Text;
using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Speech;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>The decision model client, against a fake gateway.</summary>
public sealed class JevClientTests
{
    [Fact]
    public async Task Sends_the_typesafe_shape_with_a_bearer_key_and_reads_the_answers()
    {
        var server = new FakeJev("{\"model\":\"typesafe-ai/jev\",\"answers\":{\"send\":{\"type\":\"noul\",\"noul\":0.97},\"style\":{\"type\":\"choice\",\"choice\":\"chat\",\"confidence\":0.81,\"probabilities\":{\"chat\":0.81,\"email\":0.19}}},\"usage\":{\"input_tokens\":20,\"output_tokens\":2}}");
        using var jev = new JevClient(() => "gw-key", handler: server);

        var answers = await jev.DecideAsync("Okay, finish up, send it.",
        [
            new DecisionQuestion("send", "noul", "Is this a send command?", new Dictionary<string, string?> { ["true"] = "yes", ["false"] = "no" }),
            new DecisionQuestion("style", "choice", "What kind of text?", new Dictionary<string, string?> { ["chat"] = null, ["email"] = null }),
        ], CancellationToken.None);

        server.LastRequestUri!.ToString().ShouldBe("https://ai-gateway.vercel.sh/typesafe/v1/systemone");
        server.LastAuthorization.ShouldBe("Bearer gw-key");
        server.LastBody.ShouldContain("\"model\":\"typesafe-ai/jev\"");
        server.LastBody.ShouldContain("\"state\":\"Okay, finish up, send it.\"");
        server.LastBody.ShouldContain("\"send\":{\"type\":\"noul\"");
        server.LastBody.ShouldContain("\"criteria\":{\"true\":\"yes\",\"false\":\"no\"}");
        server.LastBody.ShouldContain("\"style\":{\"type\":\"choice\"");

        answers.ShouldNotBeNull().Count.ShouldBe(2);
        answers[0].ShouldBe(new Decision("send", 0.97, null, null));
        answers[1].ShouldBe(new Decision("style", null, "chat", 0.81));
    }

    [Fact]
    public async Task Another_base_url_and_model_are_honoured()
    {
        var server = new FakeJev("{\"answers\":{}}");
        using var jev = new JevClient(() => "ts-key", "https://api.typesafe.ai/", "jev-latest", server);

        await jev.DecideAsync("x", [new DecisionQuestion("q", "noul", "?")], CancellationToken.None);

        server.LastRequestUri!.ToString().ShouldBe("https://api.typesafe.ai/v1/systemone");
        server.LastBody.ShouldContain("\"model\":\"jev-latest\"");
    }

    [Fact]
    public async Task No_key_means_no_call()
    {
        if (Environment.GetEnvironmentVariable(JevClient.ApiKeyEnvironmentVariable) is { Length: > 0 }) return;
        var server = new FakeJev("{}");
        using var jev = new JevClient(() => null, handler: server);

        (await jev.DecideAsync("x", [new DecisionQuestion("q", "noul", "?")], CancellationToken.None)).ShouldBeNull();
        server.Requests.ShouldBe(0);
        jev.LastError.ShouldBe("no API key");
    }

    [Fact]
    public async Task An_error_keeps_the_gateways_message()
    {
        var server = new FakeJev("{\"message\":\"questions.q.type: expected one of 'noul', 'choice', 'score'\",\"error_type\":\"invalid_request\"}", HttpStatusCode.UnprocessableEntity);
        using var jev = new JevClient(() => "k", handler: server);

        (await jev.DecideAsync("x", [new DecisionQuestion("q", "bad", "?")], CancellationToken.None)).ShouldBeNull();
        jev.LastError.ShouldNotBeNull().ShouldContain("expected one of");
    }

    private sealed class FakeJev(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

/// <summary>The engine asks the decision model in the shadow and never waits for it.</summary>
public sealed class ShadowDecisionTests
{
    [Fact]
    public async Task A_send_command_is_asked_about_and_the_text_is_typed_without_waiting()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(1);
        var injector = new RecordingTextInjector();
        var model = new FakeDecisionModel();
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("Here is my message, send."), injector, () => [])
        {
            SendWord = "send",
            Decisions = model,
        };

        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);

        injector.Injected.ShouldBe(["Here is my message"]);
        (await Wait.UntilAsync(() => model.Calls.Count == 1)).ShouldBeTrue();
        model.Calls[0].Keys.ShouldBe(["send", "style"]);
        model.Release.TrySetResult();
    }

    [Fact]
    public async Task Period_and_short_dictations_get_their_questions()
    {
        var hotkey = new FakeHotkeySource();
        var capture = FakeAudioCapture.Tone(1);
        var model = new FakeDecisionModel();
        await using var engine = new DictationEngine(capture, hotkey, new FakeTranscriber("Period reference."), new RecordingTextInjector(), () => [])
        {
            Decisions = model,
        };

        hotkey.Press();
        await Wait.UntilAsync(() => capture.Delivered);
        hotkey.Release();
        await Wait.UntilAsync(() => engine.State == DictationState.Idle);

        (await Wait.UntilAsync(() => model.Calls.Count == 1)).ShouldBeTrue();
        model.Calls[0].Keys.ShouldBe(["period", "mishearing", "style"]);
        model.Release.TrySetResult();
    }
}
