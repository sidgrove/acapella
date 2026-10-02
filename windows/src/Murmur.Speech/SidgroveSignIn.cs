using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Murmur.Abstractions;

namespace Murmur.Speech;

/// <summary>
/// Signs this PC in to Sidgrove Intelligence the way RFC 8252 says a desktop app should:
/// the system browser, a loopback redirect and PKCE (docs/sync.md, "Signing in").
/// </summary>
/// <remarks>
/// <para>
/// The app listens on <c>http://127.0.0.1:{free port}/callback/</c>, opens the browser at the
/// connect page with a random <c>state</c> and the PKCE challenge, and waits up to
/// <see cref="Timeout"/> for the redirect. The code it brings back is swapped for a device
/// token together with the verifier, so a code caught by anything else on the PC is useless.
/// </para>
/// <para>
/// No password ever passes through Acapella; the browser holds the Sidgrove session.
/// </para>
/// </remarks>
public sealed class SidgroveSignIn
{
    private readonly string _root;
    private readonly string _device;
    private readonly Func<Uri, bool> _openBrowser;
    private readonly HttpMessageHandler? _handler;

    /// <summary>Prepares a sign-in.</summary>
    /// <param name="baseUrl">The server; null or blank for <see cref="SidgroveSyncClient.DefaultBaseUrl"/>.</param>
    /// <param name="device">This PC's name, shown on the server's list of devices.</param>
    /// <param name="openBrowser">Opens a URL in the user's browser; false if it could not.</param>
    /// <param name="handler">A stand-in transport for the token call, for tests.</param>
    public SidgroveSignIn(string? baseUrl, string device, Func<Uri, bool> openBrowser, HttpMessageHandler? handler = null)
    {
        _root = SidgroveSyncClient.Root(baseUrl);
        _device = device;
        _openBrowser = openBrowser;
        _handler = handler;
    }

    /// <summary>How long to wait for the browser to come back.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>A PKCE verifier: 32 random bytes as base64url, 43 characters.</summary>
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge for <paramref name="verifier"/>: base64url(SHA-256(verifier)), no padding.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Base64url without padding (RFC 4648 §5).</summary>
    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Runs the whole sign-in. Never throws for an ordinary failure; the reason comes back in
    /// <see cref="SignInResult.Error"/>, worded for the Settings card.
    /// </summary>
    public async Task<SignInResult> RunAsync(CancellationToken cancellationToken)
    {
        var port = FreePort();
        var redirect = $"http://127.0.0.1:{port}/callback/";
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var verifier = NewVerifier();

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException e)
        {
            return new SignInResult(null, null, $"Acapella couldn't listen for the browser: {e.Message}");
        }

        var connect = new Uri(
            $"{_root}/api/acapella/connect?redirect_uri={Uri.EscapeDataString(redirect)}&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(Challenge(verifier))}&device={Uri.EscapeDataString(_device)}");
        if (!_openBrowser(connect)) return new SignInResult(null, null, "Acapella couldn't open your browser.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new SignInResult(null, null, "Sign-in timed out. Try again when you're ready.");
            }

            // A browser asks for a favicon too; only the callback counts.
            if (!string.Equals(context.Request.Url?.AbsolutePath, "/callback/", StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                continue;
            }

            var query = Query(context.Request.Url!.Query);
            query.TryGetValue("state", out var returned);
            SignInResult result;
            if (!string.Equals(returned, state, StringComparison.Ordinal))
            {
                result = new SignInResult(null, null, "The sign-in reply didn't match this request, so it was ignored. Try again.");
            }
            else if (query.TryGetValue("error", out var error))
            {
                result = new SignInResult(null, null, $"Sidgrove Intelligence said no: {error}.");
            }
            else if (!query.TryGetValue("code", out var code) || code.Length == 0)
            {
                result = new SignInResult(null, null, "The sign-in reply had no code. Try again.");
            }
            else
            {
                result = await ExchangeAsync(code, verifier, redirect, cancellationToken).ConfigureAwait(false);
            }

            await ReplyAsync(context.Response, result).ConfigureAwait(false);
            return result;
        }
    }

    private async Task<SignInResult> ExchangeAsync(string code, string verifier, string redirect, CancellationToken cancellationToken)
    {
        using var http = _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppPaths.ProductName, "1.0"));

        var body = JsonSerializer.Serialize(new SidgroveSyncClient.TokenRequest(code, verifier, redirect), SyncWireContext.Default.TokenRequest);
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(new Uri(_root + "/api/acapella/token"), content, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new SignInResult(null, null, $"Sidgrove Intelligence refused the sign-in ({(int)response.StatusCode}).");
            var reply = JsonSerializer.Deserialize(payload, SyncWireContext.Default.TokenReply);
            return string.IsNullOrWhiteSpace(reply?.Token)
                ? new SignInResult(null, null, "Sidgrove Intelligence sent no token.")
                : new SignInResult(reply.Token, reply.Email ?? string.Empty, null);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new SignInResult(null, null, $"Couldn't reach Sidgrove Intelligence: {e.Message}");
        }
    }

    /// <summary>A small page in the browser tab saying how it went, so the tab is not left blank.</summary>
    private static async Task ReplyAsync(HttpListenerResponse response, SignInResult result)
    {
        var (title, line) = result.Succeeded
            ? ("Signed in to Acapella", "Acapella is now syncing with Sidgrove Intelligence. You can close this tab.")
            : ("Acapella didn't sign in", WebUtility.HtmlEncode(result.Error ?? "Something went wrong.") + " You can close this tab.");
        var html =
            "<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><title>" + title + "</title>" +
            "<style>body{font-family:'DM Sans',system-ui,sans-serif;background:#f7f7fb;color:#1b1d2a;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}" +
            "main{background:#fff;border:1px solid #e3e5ee;border-radius:16px;padding:32px 40px;max-width:420px}h1{font-size:20px;margin:0 0 8px}p{margin:0;color:#5b6070;line-height:1.5}</style>" +
            "</head><body><main><h1>" + title + "</h1><p>" + line + "</p></main></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        try
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            response.Close();
        }
        catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException)
        {
            // The tab was closed first; the sign-in itself is unaffected.
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static Dictionary<string, string> Query(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            values.TryAdd(name, value);
        }
        return values;
    }
}
