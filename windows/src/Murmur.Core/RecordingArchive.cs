using System.Globalization;
using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// Keeps the audio of recent dictations on disk, next to the history.
/// </summary>
/// <remarks>
/// <para>
/// Every accuracy argument before 2026-09-24 was fought over text alone: the history says
/// what Parakeet heard and what was typed, never what was said. With the audio kept, a
/// change of model, prompt or decoder can be replayed over real dictations and scored,
/// rather than judged by how the last hour felt.
/// </para>
/// <para>
/// Files are named after the dictation's key-release time, the <c>At</c> of its history
/// row, so the two can be matched without a shared id. Only recent audio is kept: anything
/// older than <see cref="KeepFor"/>, and the oldest files past <see cref="MaxBytes"/>, are
/// deleted after each save. Nothing leaves the machine.
/// </para>
/// </remarks>
public sealed class RecordingArchive
{
    /// <summary>The file name format, sortable and matching the history's local timestamps.</summary>
    public const string NameFormat = "yyyy-MM-dd'T'HH-mm-ss.fff";

    private readonly object _gate = new();

    /// <summary>Uses <paramref name="folder"/>, created on first save.</summary>
    public RecordingArchive(string folder) => Folder = folder;

    /// <summary>The default folder, under the app's data root.</summary>
    public static string DefaultFolder => Path.Combine(AppPaths.Root, "recordings");

    /// <summary>Where the recordings are kept.</summary>
    public string Folder { get; }

    /// <summary>How long a recording is kept.</summary>
    public TimeSpan KeepFor { get; init; } = TimeSpan.FromDays(30);

    /// <summary>The most the folder may hold before the oldest recordings go.</summary>
    public long MaxBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>The file a dictation released at <paramref name="at"/> is saved to.</summary>
    public string PathFor(DateTimeOffset at) =>
        Path.Combine(Folder, at.ToLocalTime().ToString(NameFormat, CultureInfo.InvariantCulture) + ".wav");

    /// <summary>Saves the audio and prunes old files. Failures go to the log, never to the caller.</summary>
    public void Save(DateTimeOffset at, ReadOnlyMemory<float> audio)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(PathFor(at), WaveFile.Encode(audio.Span));
                Prune(DateTimeOffset.Now);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"could not keep the recording: {e.Message}");
        }
    }

    private void Prune(DateTimeOffset now)
    {
        var files = new DirectoryInfo(Folder).GetFiles("*.wav").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
        long total = 0;
        foreach (var file in files)
        {
            total += file.Length;
            if (total > MaxBytes || now - file.LastWriteTime > KeepFor) file.Delete();
        }
    }
}
