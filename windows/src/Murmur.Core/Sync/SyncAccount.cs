using System.Text;
using System.Text.Json;
using Murmur.Abstractions;

namespace Murmur.Core.Sync;

/// <summary>
/// The device token and the account it belongs to, kept encrypted in <c>sync-token.bin</c>
/// and never in <c>settings.json</c> (docs/sync.md).
/// </summary>
/// <remarks>
/// The encryption itself is <see cref="ISecretStore"/> (DPAPI, this Windows user only), which
/// lives in the platform layer; reading, writing and deleting the file happen here.
/// </remarks>
public sealed class SyncAccount
{
    private readonly string _path;
    private readonly ISecretStore? _secrets;
    private SyncAccountData? _data;

    /// <summary>Reads the account at <paramref name="path"/>, if there is one this user can decrypt.</summary>
    public SyncAccount(string path, ISecretStore? secrets)
    {
        _path = path;
        _secrets = secrets;
        _data = Load();
    }

    /// <summary>The default location.</summary>
    public static string DefaultPath => Path.Combine(AppPaths.Root, "sync-token.bin");

    /// <summary>Whether a token can be kept safely here at all; false off Windows.</summary>
    public bool CanSignIn => _secrets is not null;

    /// <summary>The device token, or null when signed out.</summary>
    public string? Token => _data?.Token;

    /// <summary>The signed-in account, or null.</summary>
    public string? Email => _data?.Email;

    /// <summary>Whether there is a token.</summary>
    public bool IsSignedIn => _data is not null;

    /// <summary>Raised on sign-in and sign-out. Any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Keeps a new token.</summary>
    public void Save(string token, string email)
    {
        _data = new SyncAccountData(token, email);
        if (_secrets is not null)
        {
            try
            {
                var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_data, SyncJsonContext.Default.SyncAccountData));
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllBytes(temp, _secrets.Protect(plain));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                Log.Warn($"sync: sign-in could not be saved, so it lasts until Acapella closes: {e.Message}");
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets the token, here and on disk.</summary>
    public void Clear()
    {
        _data = null;
        try
        {
            File.Delete(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"sync: token file could not be deleted: {e.Message}");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private SyncAccountData? Load()
    {
        if (_secrets is null) return null;
        try
        {
            if (!File.Exists(_path)) return null;
            var plain = _secrets.Unprotect(File.ReadAllBytes(_path));
            if (plain is null)
            {
                Log.Warn("sync: the saved sign-in belongs to another Windows user or PC; sign in again");
                return null;
            }
            return JsonSerializer.Deserialize(plain, SyncJsonContext.Default.SyncAccountData);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"sync: saved sign-in could not be read: {e.Message}");
            return null;
        }
    }
}
