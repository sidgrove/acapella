using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Murmur.Abstractions;

namespace Murmur.Speech;

/// <summary>
/// The sync server in Sidgrove Intelligence, over HTTPS (docs/sync.md).
/// </summary>
/// <remarks>
/// The token and base URL are read on every call, so signing in or pointing Settings at
/// another server takes effect without a new client. A 401 from any call becomes
/// <see cref="SyncUnauthorizedException"/>; other failures surface as
/// <see cref="HttpRequestException"/> for the service to back off on.
/// </remarks>
public sealed class SidgroveSyncClient : ISyncServer, IDisposable
{
    /// <summary>Where Sidgrove Intelligence lives.</summary>
    public const string DefaultBaseUrl = "https://intelligence.sidgrove.com";

    private readonly HttpClient _http;
    private readonly Func<string?> _token;
    private readonly Func<string?> _baseUrl;

    /// <summary>Creates the client.</summary>
    /// <param name="token">The device token, read per call.</param>
    /// <param name="baseUrl">The server, read per call; null or blank for <see cref="DefaultBaseUrl"/>.</param>
    /// <param name="handler">A stand-in transport for tests.</param>
    public SidgroveSyncClient(Func<string?> token, Func<string?>? baseUrl = null, HttpMessageHandler? handler = null)
    {
        _token = token;
        _baseUrl = baseUrl ?? (() => null);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        // A page of history or a 50 MB recording on a slow line; the service backs off on a timeout.
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppPaths.ProductName, "1.0"));
    }

    /// <summary>The server root in use, without a trailing slash.</summary>
    public static string Root(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim().TrimEnd('/');

    /// <inheritdoc />
    public async Task<SyncReply> SyncAsync(SyncRequest request, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(request, SyncWireContext.Default.SyncRequest);
        using var response = await SendAsync(HttpMethod.Post, "/api/acapella/sync", body, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(payload, SyncWireContext.Default.SyncReply)
               ?? throw new HttpRequestException("The sync server sent an empty reply.");
    }

    /// <inheritdoc />
    public async Task<Uri?> RecordingUrlAsync(string key, RecordingTransfer transfer, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(
            new RecordingRequest(key, transfer == RecordingTransfer.Upload ? "upload" : "download"),
            SyncWireContext.Default.RecordingRequest);
        using var response = await SendAsync(HttpMethod.Post, "/api/acapella/recordings", body, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound && transfer == RecordingTransfer.Download) return null;
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var url = JsonSerializer.Deserialize(payload, SyncWireContext.Default.UrlReply)?.Url;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : throw new HttpRequestException("The sync server sent no recording URL.");
    }

    /// <inheritdoc />
    public async Task UploadAsync(Uri url, byte[] wav, CancellationToken cancellationToken)
    {
        // A pre-signed URL: no bearer token, which belongs to Sidgrove Intelligence alone.
        using var content = new ByteArrayContent(wav);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Recording upload failed: {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
    }

    /// <inheritdoc />
    public async Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Recording download failed: {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, "/api/acapella/token", null, cancellationToken).ConfigureAwait(false);
        // Already gone is as good as revoked.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound) return;
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json, CancellationToken cancellationToken)
    {
        var token = _token();
        if (string.IsNullOrWhiteSpace(token)) throw new SyncUnauthorizedException("Not signed in.");

        using var request = new HttpRequestMessage(method, new Uri(Root(_baseUrl()) + path));
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new SyncUnauthorizedException();
        if (response.IsSuccessStatusCode) return;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {(payload.Length > 200 ? payload[..200] : payload)}", null, response.StatusCode);
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    // ---- Wire shapes. Source-generated so they survive single-file publishing. ----

    internal sealed record RecordingRequest(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("action")] string Action);

    internal sealed record UrlReply([property: JsonPropertyName("url")] string? Url);

    internal sealed record TokenRequest(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("code_verifier")] string CodeVerifier,
        [property: JsonPropertyName("redirect_uri")] string RedirectUri);

    internal sealed record TokenReply(
        [property: JsonPropertyName("token")] string? Token,
        [property: JsonPropertyName("email")] string? Email);
}

/// <summary>Source-generated JSON for the sync wire shapes.</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SyncRequest))]
[JsonSerializable(typeof(SyncReply))]
[JsonSerializable(typeof(SidgroveSyncClient.RecordingRequest))]
[JsonSerializable(typeof(SidgroveSyncClient.UrlReply))]
[JsonSerializable(typeof(SidgroveSyncClient.TokenRequest))]
[JsonSerializable(typeof(SidgroveSyncClient.TokenReply))]
internal sealed partial class SyncWireContext : JsonSerializerContext;
