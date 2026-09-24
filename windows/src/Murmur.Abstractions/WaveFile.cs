using System.Buffers.Binary;

namespace Murmur.Abstractions;

/// <summary>
/// 16-bit PCM WAV encoding of the app's mono float audio.
/// </summary>
/// <remarks>
/// One format for the two places audio leaves the engine: the recordings kept on disk for
/// accuracy testing, and the audio sent with a dictation to a clean-up model that can
/// listen. Both want the smallest lossless file every tool can read.
/// </remarks>
public static class WaveFile
{
    private const int HeaderBytes = 44;

    /// <summary>Encodes <paramref name="samples"/>, clamped to ±1, as a complete WAV file.</summary>
    public static byte[] Encode(ReadOnlySpan<float> samples, int sampleRate = AudioChunk.SampleRate)
    {
        var dataBytes = samples.Length * 2;
        var bytes = new byte[HeaderBytes + dataBytes];
        var span = bytes.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], HeaderBytes - 8 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1); // mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);

        var data = span[HeaderBytes..];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(data[(i * 2)..], value);
        }
        return bytes;
    }

    /// <summary>Decodes a 16-bit mono PCM WAV written by <see cref="Encode"/>, for tests and replays.</summary>
    /// <exception cref="InvalidDataException">The file is not 16-bit mono PCM.</exception>
    public static float[] Decode(ReadOnlySpan<byte> bytes, out int sampleRate)
    {
        if (bytes.Length < HeaderBytes || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("not a WAV file");

        sampleRate = 0;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = bytes.Slice(offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes[(offset + 4)..]);
            var body = bytes.Slice(offset + 8, Math.Min(size, bytes.Length - offset - 8));
            if (id.SequenceEqual("fmt "u8))
            {
                if (BinaryPrimitives.ReadInt16LittleEndian(body) != 1 || BinaryPrimitives.ReadInt16LittleEndian(body[2..]) != 1 || BinaryPrimitives.ReadInt16LittleEndian(body[14..]) != 16)
                    throw new InvalidDataException("only 16-bit mono PCM is supported");
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                var samples = new float[body.Length / 2];
                for (var i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(body[(i * 2)..]) / (float)short.MaxValue;
                return samples;
            }
            offset += 8 + size + (size & 1);
        }
        throw new InvalidDataException("no audio data");
    }
}
