using System.Net;
using System.Text;
using System.Text.Json;
using Murmur.Abstractions;
using Murmur.Speech;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>The HTTP side of sync, against a fake transport: the request shapes docs/sync.md fixes.</summary>
public sealed class SidgroveSyncClientTests
{
    [Fact]
    public async Task Sync_posts_the_contract_shape_with_the_bearer_token_and_reads_the_reply()
    {
        var server = new FakeServer(_ => (HttpStatusCode.OK,
            """{"cursor":1234,"more":true,"changes":[{"kind":"history","key":"3ca2387d","data":{"text":"hi"},"deleted":false,"updatedAt":"2026-10-02T13:17:45.712Z","device":"SIDGROVE_9950X"}]}"""));
        using var client = new SidgroveSyncClient(() => "sg_acapella_abc", handler: server);
        using var data = JsonDocument.Parse("""{"line":"Gev -> Jev"}""");

        var reply = await client.SyncAsync(new SyncRequest
        {
            Cursor = 7,
            Device = "LAPTOP",
            Changes =
            [
                new SyncChange { Kind = "dictionary", Key = "Correction|gev|Jev", Data = data.RootElement.Clone(), UpdatedAt = new DateTimeOffset(2026, 10, 2, 13, 17, 45, 712, TimeSpan.Zero) },
                new SyncChange { Kind = "dictionary", Key = "Term|Old", Deleted = true, UpdatedAt = new DateTimeOffset(2026, 10, 2, 13, 17, 45, 712, TimeSpan.Zero) },
            ],
        }, CancellationToken.None);

        var request = server.Seen.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe("https://intelligence.sidgrove.com/api/acapella/sync");
        request.Authorization.ShouldBe("Bearer sg_acapella_abc");
        request.ContentType.ShouldBe("application/json; charset=utf-8");
        // Compared as JSON: the serialiser escapes ">" as >, which any parser reads back as ">".
        System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(request.Body), System.Text.Json.Nodes.JsonNode.Parse(
            """{"cursor":7,"device":"LAPTOP","changes":[""" +
            """{"kind":"dictionary","key":"Correction|gev|Jev","data":{"line":"Gev -> Jev"},"deleted":false,"updatedAt":"2026-10-02T13:17:45.712Z"},""" +
            """{"kind":"dictionary","key":"Term|Old","data":null,"deleted":true,"updatedAt":"2026-10-02T13:17:45.712Z"}]}""")).ShouldBeTrue(request.Body);

        reply.Cursor.ShouldBe(1234);
        reply.More.ShouldBeTrue();
        var change = reply.Changes.Single();
        change.Kind.ShouldBe("history");
        change.Device.ShouldBe("SIDGROVE_9950X");
        change.Data!.Value.GetProperty("text").GetString().ShouldBe("hi");
        change.UpdatedAt.ShouldBe(new DateTimeOffset(2026, 10, 2, 13, 17, 45, 712, TimeSpan.Zero));
    }

    [Fact]
    public async Task Another_server_from_settings_is_used_for_every_call()
    {
        var server = new FakeServer(_ => (HttpStatusCode.OK, """{"cursor":0,"more":false,"changes":[]}"""));
        using var client = new SidgroveSyncClient(() => "t", () => "https://staging.sidgrove.test/", server);

        await client.SyncAsync(new SyncRequest { Device = "PC" }, CancellationToken.None);

        server.Seen.Single().Uri.ShouldBe("https://staging.sidgrove.test/api/acapella/sync");
    }

    [Fact]
    public async Task A_401_means_signed_out()
    {
        var server = new FakeServer(_ => (HttpStatusCode.Unauthorized, """{"error":"invalid token"}"""));
        using var client = new SidgroveSyncClient(() => "expired", handler: server);

        await Should.ThrowAsync<SyncUnauthorizedException>(() => client.SyncAsync(new SyncRequest { Device = "PC" }, CancellationToken.None));
        await Should.ThrowAsync<SyncUnauthorizedException>(() => client.RecordingUrlAsync("k", RecordingTransfer.Upload, CancellationToken.None));
    }

    [Fact]
    public async Task No_token_means_no_call()
    {
        var server = new FakeServer(_ => (HttpStatusCode.OK, "{}"));
        using var client = new SidgroveSyncClient(() => null, handler: server);

        await Should.ThrowAsync<SyncUnauthorizedException>(() => client.SyncAsync(new SyncRequest { Device = "PC" }, CancellationToken.None));
        server.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_server_error_is_a_network_failure_to_back_off_on()
    {
        var server = new FakeServer(_ => (HttpStatusCode.InternalServerError, "boom"));
        using var client = new SidgroveSyncClient(() => "t", handler: server);

        var e = await Should.ThrowAsync<HttpRequestException>(() => client.SyncAsync(new SyncRequest { Device = "PC" }, CancellationToken.None));
        e.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Recordings_ask_for_a_url_then_put_and_get_without_the_token()
    {
        var server = new FakeServer(request => request.Uri switch
        {
            "https://intelligence.sidgrove.com/api/acapella/recordings" => (HttpStatusCode.OK, """{"url":"https://blob.test/rec/abc?sig=1"}"""),
            "https://blob.test/rec/abc?sig=1" when request.Method == HttpMethod.Get => (HttpStatusCode.OK, "RIFF"),
            _ => (HttpStatusCode.OK, string.Empty),
        });
        using var client = new SidgroveSyncClient(() => "t", handler: server);

        var upload = await client.RecordingUrlAsync("abc", RecordingTransfer.Upload, CancellationToken.None);
        await client.UploadAsync(upload!, [82, 73, 70, 70], CancellationToken.None);
        var download = await client.RecordingUrlAsync("abc", RecordingTransfer.Download, CancellationToken.None);
        var bytes = await client.DownloadAsync(download!, CancellationToken.None);

        server.Seen[0].Body.ShouldBe("""{"key":"abc","action":"upload"}""");
        server.Seen[0].Authorization.ShouldBe("Bearer t");
        server.Seen[1].Method.ShouldBe(HttpMethod.Put);
        server.Seen[1].ContentType.ShouldBe("audio/wav");
        server.Seen[1].Authorization.ShouldBeNull();
        server.Seen[1].Body.ShouldBe("RIFF");
        server.Seen[2].Body.ShouldBe("""{"key":"abc","action":"download"}""");
        server.Seen[3].Authorization.ShouldBeNull();
        Encoding.ASCII.GetString(bytes!).ShouldBe("RIFF");
    }

    [Fact]
    public async Task A_recording_the_server_does_not_have_is_null()
    {
        var server = new FakeServer(_ => (HttpStatusCode.NotFound, """{"error":"no file"}"""));
        using var client = new SidgroveSyncClient(() => "t", handler: server);

        (await client.RecordingUrlAsync("abc", RecordingTransfer.Download, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Sign_out_deletes_the_token()
    {
        var server = new FakeServer(_ => (HttpStatusCode.NoContent, string.Empty));
        using var client = new SidgroveSyncClient(() => "t", handler: server);

        await client.RevokeAsync(CancellationToken.None);

        server.Seen.Single().Method.ShouldBe(HttpMethod.Delete);
        server.Seen.Single().Uri.ShouldBe("https://intelligence.sidgrove.com/api/acapella/token");
        server.Seen.Single().Authorization.ShouldBe("Bearer t");
    }

    [Fact]
    public async Task Keys_are_asked_for_with_the_bearer_token_and_read_from_the_reply()
    {
        var server = new FakeServer(_ => (HttpStatusCode.OK,
            """{"keys":{"gemini":"gemini-key","elevenLabs":" eleven-key ","anthropic":null,"aiGateway":"gateway-key","openAi":"something-newer"},"fetchedAt":"2026-10-10T09:00:00Z"}"""));
        using var client = new SidgroveSyncClient(() => "sg_acapella_abc", () => "https://staging.sidgrove.test/", server);

        var keys = await client.KeysAsync(CancellationToken.None);

        var request = server.Seen.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.ShouldBe("https://staging.sidgrove.test/api/acapella/keys");
        request.Authorization.ShouldBe("Bearer sg_acapella_abc");
        request.Body.ShouldBeEmpty();
        keys.ShouldBe(new ManagedKeySet("gemini-key", "eleven-key", null, "gateway-key"));
    }

    [Fact]
    public async Task Keys_the_server_does_not_hold_are_null()
    {
        var server = new FakeServer(_ => (HttpStatusCode.OK, """{"keys":{"gemini":null,"elevenLabs":"","aiGateway":null}}"""));
        using var client = new SidgroveSyncClient(() => "t", handler: server);

        var keys = await client.KeysAsync(CancellationToken.None);

        server.Seen.Single().Uri.ShouldBe("https://intelligence.sidgrove.com/api/acapella/keys");
        keys.ShouldBe(new ManagedKeySet(null, null, null, null));
    }

    [Fact]
    public async Task A_401_for_the_keys_means_signed_out()
    {
        var server = new FakeServer(_ => (HttpStatusCode.Unauthorized, """{"error":"invalid token"}"""));
        using var client = new SidgroveSyncClient(() => "revoked", handler: server);

        await Should.ThrowAsync<SyncUnauthorizedException>(() => client.KeysAsync(CancellationToken.None));
    }

    [Fact]
    public async Task No_token_means_the_keys_are_not_asked_for()
    {
        var server = new FakeServer(_ => (HttpStatusCode.OK, """{"keys":{}}"""));
        using var client = new SidgroveSyncClient(() => null, handler: server);

        await Should.ThrowAsync<SyncUnauthorizedException>(() => client.KeysAsync(CancellationToken.None));
        server.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_older_server_without_the_keys_route_has_none()
    {
        var server = new FakeServer(_ => (HttpStatusCode.NotFound, "<html>Not found</html>"));
        using var client = new SidgroveSyncClient(() => "t", handler: server);

        (await client.KeysAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_failed_keys_call_never_quotes_the_reply()
    {
        var broken = new FakeServer(_ => (HttpStatusCode.InternalServerError, """{"keys":{"gemini":"gemini-key"}}"""));
        using var client = new SidgroveSyncClient(() => "t", handler: broken);
        var failed = await Should.ThrowAsync<HttpRequestException>(() => client.KeysAsync(CancellationToken.None));
        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        failed.ToString().ShouldNotContain("gemini-key");

        var garbled = new FakeServer(_ => (HttpStatusCode.OK, """{"keys":{"gemini":"gemini-key","""));
        using var second = new SidgroveSyncClient(() => "t", handler: garbled);
        (await Should.ThrowAsync<HttpRequestException>(() => second.KeysAsync(CancellationToken.None))).ToString().ShouldNotContain("gemini-key");

        var empty = new FakeServer(_ => (HttpStatusCode.OK, "{}"));
        using var third = new SidgroveSyncClient(() => "t", handler: empty);
        await Should.ThrowAsync<HttpRequestException>(() => third.KeysAsync(CancellationToken.None));
    }

    internal sealed record Seen(HttpMethod Method, string Uri, string? Authorization, string? ContentType, string Body);

    internal sealed class FakeServer(Func<Seen, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        public List<Seen> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var seen = new Seen(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.ToString(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            lock (Seen) Seen.Add(seen);
            var (status, body) = answer(seen);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

/// <summary>The loopback sign-in: PKCE, the state check and the token exchange.</summary>
public sealed class SidgroveSignInTests
{
    [Fact]
    public void The_pkce_challenge_matches_rfc_7636()
    {
        SidgroveSignIn.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk").ShouldBe("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }

    [Fact]
    public void A_verifier_is_43_base64url_characters()
    {
        var verifier = SidgroveSignIn.NewVerifier();
        verifier.Length.ShouldBe(43);
        verifier.ShouldAllBe(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
        SidgroveSignIn.NewVerifier().ShouldNotBe(verifier);
    }

    [Fact]
    public async Task The_browser_round_trip_swaps_the_code_for_a_token()
    {
        var token = new SidgroveSyncClientTests.FakeServer(_ => (HttpStatusCode.OK, """{"token":"sg_acapella_0123456789012345678901234567890123456789abc","email":"dave@sidgrove.com"}"""));
        Uri? opened = null;
        string? page = null;
        var signIn = new SidgroveSignIn(null, "SIDGROVE_9950X", url =>
        {
            opened = url;
            _ = Task.Run(async () => page = await Browse(Callback(url, code: "the-code")));
            return true;
        }, token);

        var result = await signIn.RunAsync(CancellationToken.None);

        result.Succeeded.ShouldBeTrue(result.Error);
        result.Token.ShouldBe("sg_acapella_0123456789012345678901234567890123456789abc");
        result.Email.ShouldBe("dave@sidgrove.com");

        opened!.GetLeftPart(UriPartial.Path).ShouldBe("https://intelligence.sidgrove.com/api/acapella/connect");
        var query = Query(opened);
        query["redirect_uri"].ShouldMatch(@"^http://127\.0\.0\.1:\d+/callback/$");
        query["device"].ShouldBe("SIDGROVE_9950X");

        var exchange = token.Seen.Single();
        exchange.Uri.ShouldBe("https://intelligence.sidgrove.com/api/acapella/token");
        using var body = JsonDocument.Parse(exchange.Body);
        body.RootElement.GetProperty("code").GetString().ShouldBe("the-code");
        body.RootElement.GetProperty("redirect_uri").GetString().ShouldBe(query["redirect_uri"]);
        // The verifier sent with the code is the one the challenge was made from.
        SidgroveSignIn.Challenge(body.RootElement.GetProperty("code_verifier").GetString()!).ShouldBe(query["code_challenge"]);

        (await Wait(() => page is not null)).ShouldBeTrue();
        page!.ShouldContain("You can close this tab");
    }

    [Fact]
    public async Task A_reply_with_the_wrong_state_is_refused()
    {
        var token = new SidgroveSyncClientTests.FakeServer(_ => (HttpStatusCode.OK, """{"token":"x","email":"y"}"""));
        var signIn = new SidgroveSignIn(null, "PC", url =>
        {
            _ = Task.Run(() => Browse(Callback(url, code: "c", state: "forged")));
            return true;
        }, token);

        var result = await signIn.RunAsync(CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldNotBeNull().ShouldContain("didn't match");
        token.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refusal_comes_back_as_an_error()
    {
        var signIn = new SidgroveSignIn(null, "PC", url =>
        {
            var state = Query(url)["state"];
            var redirect = Query(url)["redirect_uri"];
            _ = Task.Run(() => Browse(new Uri($"{redirect}?error=not_internal&state={Uri.EscapeDataString(state)}")));
            return true;
        });

        var result = await signIn.RunAsync(CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldNotBeNull().ShouldContain("not_internal");
    }

    [Fact]
    public async Task Gives_up_after_the_timeout()
    {
        var signIn = new SidgroveSignIn(null, "PC", _ => true) { Timeout = TimeSpan.FromMilliseconds(200) };

        var result = await signIn.RunAsync(CancellationToken.None);

        result.Error.ShouldNotBeNull().ShouldContain("timed out");
    }

    [Fact]
    public async Task A_browser_that_will_not_open_is_reported()
    {
        var result = await new SidgroveSignIn(null, "PC", _ => false).RunAsync(CancellationToken.None);
        result.Error.ShouldNotBeNull().ShouldContain("browser");
    }

    private static Uri Callback(Uri connect, string code, string? state = null)
    {
        var query = Query(connect);
        return new Uri($"{query["redirect_uri"]}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state ?? query["state"])}");
    }

    /// <summary>What the browser does with the redirect: a plain GET to the loopback listener.</summary>
    private static async Task<string> Browse(Uri url)
    {
        using var browser = new HttpClient();
        return await browser.GetStringAsync(url);
    }

    private static Dictionary<string, string> Query(Uri url) =>
        url.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

    private static async Task<bool> Wait(Func<bool> condition) => await Murmur.Testing.Wait.UntilAsync(condition);
}
