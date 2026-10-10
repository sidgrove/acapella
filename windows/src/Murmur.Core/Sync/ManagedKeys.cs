using System.Text;
using System.Text.Json;
using Murmur.Abstractions;

namespace Murmur.Core.Sync;

/// <summary>
/// The AI provider keys Sidgrove Intelligence handed this PC at sign-in, kept encrypted in
/// <c>managed-keys.bin</c> and never in <c>settings.json</c> or the log (docs/sync.md, "Keys").
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="SyncAccount"/>: the encryption is <see cref="ISecretStore"/> (DPAPI,
/// this Windows user only) and the file handling is here. Without a secret store (off
/// Windows) the keys are held in memory only and the disk is never touched.
/// </para>
/// <para>
/// Read on every dictation, from any thread: the four keys are swapped as one immutable set,
/// so a reader sees either the old set or the new one and never waits on the disk.
/// </para>
/// </remarks>
public sealed class ManagedKeys
{
    private static readonly ManagedKeySet None = new(null, null, null, null);

    private readonly string _path;
    private readonly ISecretStore? _secrets;
    private volatile ManagedKeySet _keys;

    /// <summary>Reads the keys at <paramref name="path"/>, if there are any this user can decrypt.</summary>
    public ManagedKeys(string path, ISecretStore? secrets)
    {
        _path = path;
        _secrets = secrets;
        _keys = Load();
    }

    /// <summary>The default location.</summary>
    public static string DefaultPath => Path.Combine(AppPaths.Root, "managed-keys.bin");

    /// <summary>The Gemini key, or null.</summary>
    public string? Gemini => _keys.Gemini;

    /// <summary>The ElevenLabs key, or null.</summary>
    public string? ElevenLabs => _keys.ElevenLabs;

    /// <summary>The Anthropic key, or null.</summary>
    public string? Anthropic => _keys.Anthropic;

    /// <summary>The Vercel AI Gateway key (Jev), or null.</summary>
    public string? AiGateway => _keys.AiGateway;

    /// <summary>How many of the four are held. Safe to log; the keys are not.</summary>
    public int Count => _keys.Count;

    /// <summary>Raised when the keys are replaced or cleared. Any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The key a client should be given: the one typed in Settings if there is one, else the
    /// managed one, else null, which each client reads as "use my environment variable".
    /// </summary>
    public static string? Prefer(string? typed, string? managed) =>
        !string.IsNullOrWhiteSpace(typed) ? typed
        : !string.IsNullOrWhiteSpace(managed) ? managed
        : null;

    /// <summary>Keeps a new set of keys, replacing all four. A blank is kept as "none".</summary>
    public void Save(string? gemini, string? elevenLabs, string? anthropic, string? aiGateway)
    {
        var keys = new ManagedKeySet(Clean(gemini), Clean(elevenLabs), Clean(anthropic), Clean(aiGateway));
        if (keys == _keys) return;
        if (keys.Count == 0)
        {
            Clear();
            return;
        }

        _keys = keys;
        if (_secrets is not null)
        {
            try
            {
                var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(keys, SyncJsonContext.Default.ManagedKeySet));
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllBytes(temp, _secrets.Protect(plain));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                Log.Warn($"keys: the keys from Sidgrove Intelligence could not be saved, so they last until Acapella closes: {e.Message}");
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps a new set of keys.</summary>
    public void Save(ManagedKeySet keys) => Save(keys.Gemini, keys.ElevenLabs, keys.Anthropic, keys.AiGateway);

    /// <summary>Forgets the keys, here and on disk.</summary>
    public void Clear()
    {
        var had = _keys.Count > 0;
        _keys = None;
        if (_secrets is not null)
        {
            try
            {
                File.Delete(_path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"keys: the keys file could not be deleted: {e.Message}");
            }
        }
        if (had) Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string? Clean(string? key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim();

    private ManagedKeySet Load()
    {
        if (_secrets is null) return None;
        try
        {
            if (!File.Exists(_path)) return None;
            var plain = _secrets.Unprotect(File.ReadAllBytes(_path));
            if (plain is null)
            {
                Log.Warn("keys: the saved keys belong to another Windows user or PC; they come back at the next sign-in or refresh");
                return None;
            }
            return JsonSerializer.Deserialize(plain, SyncJsonContext.Default.ManagedKeySet) ?? None;
        }
        catch (JsonException)
        {
            // No message: a JSON error can quote the text around where it stopped.
            Log.Warn("keys: the saved keys could not be read");
            return None;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"keys: the saved keys could not be read: {e.Message}");
            return None;
        }
    }
}
