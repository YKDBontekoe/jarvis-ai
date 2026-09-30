using System.Buffers.Binary;

namespace Jarvis.Api.Realtime;

internal static class VoicePcm
{
    public const int Rate = 24_000;
    public const int OpusRate = 48_000;

    public static short[] ToInt16(ReadOnlySpan<byte> bytes)
    {
        var count = bytes.Length / 2;
        var samples = new short[count];
        for (var i = 0; i < count; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(i * 2, 2));
        return samples;
    }

    /// <summary>Removes every whole frame from the front of the buffer; a partial frame waits for more audio.</summary>
    public static List<short[]> TakeFrames(List<short> buffer, int frameSize)
    {
        var count = buffer.Count / frameSize;
        var frames = new List<short[]>(count);
        for (var i = 0; i < count; i++)
            frames.Add(buffer.GetRange(i * frameSize, frameSize).ToArray());
        if (count > 0) buffer.RemoveRange(0, count * frameSize);
        return frames;
    }

    public static byte[] ToBytes(ReadOnlySpan<short> samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), samples[i]);
        return bytes;
    }

    public static short[] ToOpusPcm(ReadOnlySpan<short> mono24)
    {
        var output = new short[mono24.Length * 4];
        var offset = 0;
        foreach (var sample in mono24)
        {
            output[offset++] = sample;
            output[offset++] = sample;
            output[offset++] = sample;
            output[offset++] = sample;
        }

        return output;
    }

    public static short[] FromOpusPcm(ReadOnlySpan<short> stereo48)
    {
        var frames = stereo48.Length / 4;
        if (frames <= 0) return [];
        var output = new short[frames];
        for (var i = 0; i < frames; i++)
        {
            var index = i * 4;
            var first = ((int)stereo48[index] + stereo48[index + 1]) / 2;
            var second = ((int)stereo48[index + 2] + stereo48[index + 3]) / 2;
            output[i] = (short)((first + second) / 2);
        }

        return output;
    }
}
