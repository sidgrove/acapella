using Murmur.Abstractions;
using NAudio.Wave;

namespace Murmur.Platform.Windows;

/// <summary>Plays short cues through the default Windows output device.</summary>
/// <remarks>
/// <para>
/// The whole cue is handed to the device before playback starts. With 60 ms of buffering
/// a 90 ms cue needed a refill from a managed thread-pool thread part-way through, with
/// under 5 ms of slack; a garbage collection or the speech model starting up landed in
/// that window often enough that the note broke into two clicks, which is what "the sound
/// effects crack up" was. Buffering was measured to make no difference to when the cue
/// starts (35 ms either way), only to whether it survives.
/// </para>
/// <para>
/// Playback is opened on a pool thread rather than the caller's. <c>WaveOutEvent</c>
/// captures the creating thread's synchronisation context and posts its stop event back
/// to it, so a cue opened on the UI thread also closed on the UI thread, in the middle of
/// the overlay being shown.
/// </para>
/// </remarks>
public sealed class FeedbackAudio : IFeedbackAudio
{
    /// <summary>Longer than any cue, so nothing is ever refilled mid-note.</summary>
    private const int BufferMilliseconds = 250;

    /// <inheritdoc />
    public void Play(byte[] wave)
    {
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var reader = new WaveFileReader(new MemoryStream(state, writable: false));
            var output = new WaveOutEvent { DesiredLatency = BufferMilliseconds, NumberOfBuffers = 2 };
            output.PlaybackStopped += (_, _) => { output.Dispose(); reader.Dispose(); };
            try { output.Init(reader); output.Play(); }
            catch (Exception e)
            {
                output.Dispose();
                reader.Dispose();
                PlatformDiagnostics.Warn($"feedback sound unavailable: {e.Message}");
            }
        }, wave, preferLocal: false);
    }
}
