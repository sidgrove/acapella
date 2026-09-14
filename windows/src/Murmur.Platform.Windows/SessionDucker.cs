using System.Runtime.InteropServices;
using System.Text.Json;
using Murmur.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Murmur.Platform.Windows;

/// <summary>Mutes other apps temporarily, with a durable recovery record for interrupted recordings.</summary>
/// <remarks>
/// <para>
/// The recovery record is written <i>before</i> anything is muted, so a process killed
/// mid-dictation can put things right on its next start. It matches on the session
/// identifier as well as the instance identifier: Windows keeps an app's mute state across
/// relaunches, and a relaunched app comes back with a new instance id but the same session
/// id, which is the only way to find it again.
/// </para>
/// <para>
/// A record that cannot be acted on — the endpoint it names is gone, or it is older than
/// <see cref="RecoveryMaxAge"/> — is dropped rather than kept forever. Keeping it disabled
/// ducking permanently, since a pending record blocks a new one, and nothing said so.
/// </para>
/// </remarks>
public sealed class SessionDucker : IAudioDucker
{
    /// <summary>A recovery record older than this is stale and is discarded.</summary>
    public static readonly TimeSpan RecoveryMaxAge = TimeSpan.FromDays(1);

    private const int ErrorNotFound = unchecked((int)0x80070490);

    private readonly object _lock = new();
    private readonly List<AudioSessionControl> _ducked = [];
    private readonly string _recoveryPath = Path.Combine(AppPaths.Root, "audio-restore.json");
    private MMDevice? _device;

    /// <summary>Restores sessions left muted by a previous interrupted app process.</summary>
    public SessionDucker()
    {
        lock (_lock) RecoverInterruptedMute();
    }

    /// <inheritdoc />
    public (float Peak, string? Source) Duck()
    {
        lock (_lock)
        {
            if (_device is not null) return (0, null);
            var peak = 0f;
            string? source = null;
            try
            {
                // Never overwrite an outstanding restoration record.
                RecoverInterruptedMute();
                if (File.Exists(_recoveryPath))
                {
                    PlatformDiagnostics.Warn("audio not ducked: a previous restoration is still pending");
                    return (0, null);
                }
                using var enumerator = new MMDeviceEnumerator();
                if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return (0, null);
                _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = _device.AudioSessionManager.Sessions;
                var record = new List<string>();
                for (var i = 0; i < sessions.Count; i++)
                {
                    // Each index builds a fresh wrapper around the COM session; the ones we
                    // do not keep are disposed here rather than left to the finaliser.
                    var session = sessions[i];
                    if (session.GetProcessID == (uint)Environment.ProcessId ||
                        session.State == AudioSessionState.AudioSessionStateExpired ||
                        session.SimpleAudioVolume.Mute)
                    {
                        session.Dispose();
                        continue;
                    }
                    // Read before muting: a muted session's meter drops to zero at once.
                    if (session.State == AudioSessionState.AudioSessionStateActive)
                    {
                        var level = session.AudioMeterInformation.MasterPeakValue;
                        if (level > peak)
                        {
                            peak = level;
                            source = ProcessNameOf(session.GetProcessID);
                        }
                    }
                    _ducked.Add(session);
                    record.Add(session.GetSessionInstanceIdentifier);
                    record.Add(session.GetSessionIdentifier);
                }

                // Save BEFORE touching Windows audio. If persistence fails, do not mute.
                if (_ducked.Count > 0)
                {
                    Directory.CreateDirectory(AppPaths.Root);
                    var pending = new Dictionary<string, string[]>
                    {
                        [_device.ID] = record.ToArray(),
                    };
                    var temporary = _recoveryPath + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(pending, AudioRecoveryJson.Default.DictionaryStringStringArray));
                    File.Move(temporary, _recoveryPath, overwrite: true);
                    foreach (var session in _ducked) session.SimpleAudioVolume.Mute = true;
                }
            }
            catch (Exception e) when (IsAudioOrFileError(e))
            {
                PlatformDiagnostics.Warn($"could not mute other audio: {e.Message}");
                RestoreLocked();
                return (0, null);
            }
            return (peak, source);
        }
    }

    private static string? ProcessNameOf(uint processId)
    {
        try { return System.Diagnostics.Process.GetProcessById((int)processId).ProcessName; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return null; }
    }

    /// <inheritdoc />
    public void Restore()
    {
        lock (_lock) RestoreLocked();
    }

    private void RestoreLocked()
    {
        foreach (var session in _ducked)
        {
            try { session.SimpleAudioVolume.Mute = false; }
            catch (Exception e) when (IsAudioOrFileError(e)) { PlatformDiagnostics.Warn($"could not unmute a session: {e.Message}"); }
            session.Dispose();
        }
        _ducked.Clear();
        _device?.Dispose();
        _device = null;
        RecoverInterruptedMute();
    }

    private void RecoverInterruptedMute()
    {
        if (!File.Exists(_recoveryPath)) return;
        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(_recoveryPath) > RecoveryMaxAge)
            {
                PlatformDiagnostics.Warn("discarded a stale audio restoration record");
                File.Delete(_recoveryPath);
                return;
            }

            var pending = JsonSerializer.Deserialize(File.ReadAllText(_recoveryPath), AudioRecoveryJson.Default.DictionaryStringStringArray);
            if (pending is null)
            {
                File.Delete(_recoveryPath);
                return;
            }

            using var enumerator = new MMDeviceEnumerator();
            foreach (var (deviceId, ids) in pending)
            {
                MMDevice device;
                try
                {
                    device = enumerator.GetDevice(deviceId);
                }
                catch (COMException e) when (e.HResult == ErrorNotFound)
                {
                    // The output it was muted on is gone. There is nothing left to restore
                    // there, and a record that can never be satisfied must not block
                    // every future duck.
                    PlatformDiagnostics.Warn("audio restoration skipped: the output device is no longer present");
                    continue;
                }

                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                        if (ids.Contains(session.GetSessionInstanceIdentifier, StringComparer.Ordinal) ||
                            ids.Contains(session.GetSessionIdentifier, StringComparer.Ordinal))
                        {
                            session.SimpleAudioVolume.Mute = false;
                        }
                    }
                }
            }
            File.Delete(_recoveryPath);
        }
        catch (JsonException)
        {
            PlatformDiagnostics.Warn("discarded an unreadable audio restoration record");
            try { File.Delete(_recoveryPath); } catch (IOException) { /* next time */ }
        }
        catch (Exception e) when (IsAudioOrFileError(e))
        {
            // Keep the record and retry on the next start/recording if an endpoint is unavailable.
            PlatformDiagnostics.Warn($"audio restoration pending: {e.Message}");
        }
    }

    private static bool IsAudioOrFileError(Exception e) =>
        e is COMException or InvalidCastException or UnauthorizedAccessException or IOException;
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string[]>))]
internal sealed partial class AudioRecoveryJson : System.Text.Json.Serialization.JsonSerializerContext;
