using System.Text.Json;
using System.Text.Json.Serialization;

namespace Murmur.Abstractions;

/// <summary>
/// One row of the sync protocol (docs/sync.md): a dictionary entry, suggestion, history
/// record, the settings or a recording, as pushed to or pulled from Sidgrove Intelligence.
/// </summary>
/// <remarks>
/// Lives here rather than in Core because the HTTP client in Murmur.Speech implements the
/// server side of it, and Speech references only this project.
/// </remarks>
public sealed record SyncChange
{
    /// <summary><c>dictionary</c>, <c>suggestion</c>, <c>history</c>, <c>settings</c> or <c>recording</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>The item's key within its kind, 1 to 300 characters.</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>The item as a JSON object, or null when deleted.</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    /// <summary>A tombstone.</summary>
    [JsonPropertyName("deleted")]
    public bool Deleted { get; init; }

    /// <summary>When the change was made; the later one wins on the server.</summary>
    [JsonPropertyName("updatedAt")]
    [JsonConverter(typeof(UtcInstantConverter))]
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The PC that made the change. Set by the server on pulled rows; ignored on a push.</summary>
    [JsonPropertyName("device")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Device { get; init; }
}

/// <summary>
/// Writes an instant as UTC with a <c>Z</c> and milliseconds (<c>2026-10-02T13:17:45.712Z</c>),
/// the form docs/sync.md shows; reads any ISO 8601 form.
/// </summary>
public sealed class UtcInstantConverter : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>What one sync call sends.</summary>
public sealed record SyncRequest
{
    /// <summary>The cursor from the last reply, 0 the first time.</summary>
    [JsonPropertyName("cursor")]
    public long Cursor { get; init; }

    /// <summary>This PC's name.</summary>
    [JsonPropertyName("device")]
    public string Device { get; init; } = string.Empty;

    /// <summary>Up to 500 changes to push.</summary>
    [JsonPropertyName("changes")]
    public IReadOnlyList<SyncChange> Changes { get; init; } = [];
}

/// <summary>What one sync call returns.</summary>
public sealed record SyncReply
{
    /// <summary>The cursor to send next time.</summary>
    [JsonPropertyName("cursor")]
    public long Cursor { get; init; }

    /// <summary>Whether more rows are waiting; call again with <see cref="Cursor"/> straight away.</summary>
    [JsonPropertyName("more")]
    public bool More { get; init; }

    /// <summary>Rows changed since the cursor sent, in order, the caller's own writes included.</summary>
    [JsonPropertyName("changes")]
    public IReadOnlyList<SyncChange> Changes { get; init; } = [];
}

/// <summary>Whether a recording URL is for sending a file or fetching one.</summary>
public enum RecordingTransfer
{
    /// <summary>A URL to <c>PUT</c> the WAV to.</summary>
    Upload,

    /// <summary>A URL to <c>GET</c> the WAV from.</summary>
    Download,
}

/// <summary>
/// The sync server: Sidgrove Intelligence in the app, an in-memory stand-in in tests.
/// </summary>
/// <remarks>
/// Every method throws <see cref="SyncUnauthorizedException"/> when the server says the
/// device token is no good, and lets network failures (<see cref="HttpRequestException"/>,
/// timeouts) through for the caller to back off on.
/// </remarks>
public interface ISyncServer
{
    /// <summary>Pushes changes and pulls what changed since the cursor.</summary>
    Task<SyncReply> SyncAsync(SyncRequest request, CancellationToken cancellationToken);

    /// <summary>A short-lived URL for one recording, or null when there is nothing to download.</summary>
    Task<Uri?> RecordingUrlAsync(string key, RecordingTransfer transfer, CancellationToken cancellationToken);

    /// <summary>Sends WAV bytes to an upload URL.</summary>
    Task UploadAsync(Uri url, byte[] wav, CancellationToken cancellationToken);

    /// <summary>Fetches WAV bytes from a download URL, or null when the file is not there.</summary>
    Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken);

    /// <summary>Revokes this device's token, for signing out.</summary>
    Task RevokeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The AI provider keys Sidgrove Intelligence hands a signed-in PC (docs/sync.md, "Keys").
/// A null is a key the server does not hold.
/// </summary>
/// <param name="Gemini">The Gemini key, for the clean-up and Gemini's cloud hearing.</param>
/// <param name="ElevenLabs">The ElevenLabs key, for cloud hearing.</param>
/// <param name="Anthropic">The Anthropic key, for a Claude clean-up model.</param>
/// <param name="AiGateway">The Vercel AI Gateway key, for Jev.</param>
public sealed record ManagedKeySet(string? Gemini, string? ElevenLabs, string? Anthropic, string? AiGateway)
{
    /// <summary>How many of the four are present.</summary>
    [JsonIgnore]
    public int Count => new[] { Gemini, ElevenLabs, Anthropic, AiGateway }.Count(k => !string.IsNullOrWhiteSpace(k));

    /// <summary>Never the keys themselves: this record must be safe to end up in a log line.</summary>
    public override string ToString() => $"{nameof(ManagedKeySet)} ({Count} of 4)";
}

/// <summary>
/// Where the managed keys come from: Sidgrove Intelligence in the app, a stand-in in tests.
/// Separate from <see cref="ISyncServer"/> so a fake of one need not answer for the other.
/// </summary>
public interface IManagedKeyServer
{
    /// <summary>
    /// The keys the server holds for this account, or null when the server is an older one
    /// without the route. Throws <see cref="SyncUnauthorizedException"/> on a refused token
    /// and lets network failures through.
    /// </summary>
    Task<ManagedKeySet?> KeysAsync(CancellationToken cancellationToken);
}

/// <summary>The server refused the device token: the user has to sign in again.</summary>
public sealed class SyncUnauthorizedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SyncUnauthorizedException() : base("The sync server refused the sign-in.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public SyncUnauthorizedException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and cause.</summary>
    public SyncUnauthorizedException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>The end of a sign-in: a device token and the account it belongs to, or why there is none.</summary>
/// <param name="Token">The device token, null on failure.</param>
/// <param name="Email">The signed-in account, null on failure.</param>
/// <param name="Error">Why it failed, in words for the Settings card, null on success.</param>
public sealed record SignInResult(string? Token, string? Email, string? Error)
{
    /// <summary>Whether a token came back.</summary>
    public bool Succeeded => Token is not null;
}

/// <summary>
/// Encrypts small secrets so only this Windows user can read them back: DPAPI on Windows.
/// </summary>
/// <remarks>Logic-free on purpose; the file handling lives in Core.</remarks>
public interface ISecretStore
{
    /// <summary>Encrypts <paramref name="plain"/>.</summary>
    byte[] Protect(byte[] plain);

    /// <summary>Decrypts what <see cref="Protect"/> made, or null if it cannot be read (another user, another PC).</summary>
    byte[]? Unprotect(byte[] cipher);
}
