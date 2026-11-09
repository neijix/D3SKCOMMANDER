using System.Buffers.Binary;

namespace D3skCommander.Core.Preview;

/// <summary>
/// Finds the largest ordinary JPEG stored inside another file: camera RAW files (CR2, CR3, NEF, ARW, DNG,
/// RAF, ORF…) all keep a large preview the camera made. Each candidate is walked marker by marker to its
/// end, so stray FF D8 bytes do not count; lossless JPEG (the sensor data of some cameras) is skipped, an
/// ordinary decoder cannot read it.
/// </summary>
public static class EmbeddedJpeg
{
    /// <summary>The bytes of the largest embedded baseline or progressive JPEG; empty when there is none.</summary>
    public static ReadOnlyMemory<byte> Largest(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bestStart = 0;
        var bestLength = 0;
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if (data[i] != 0xFF || data[i + 1] != 0xD8 || data[i + 2] != 0xFF)
            {
                continue;
            }

            var length = Measure(data, i);
            if (length > bestLength)
            {
                bestStart = i;
                bestLength = length;
            }

            if (length > 0)
            {
                i += length - 1;
            }
        }

        return data.AsMemory(bestStart, bestLength);
    }

    // The length of the JPEG starting at start (FF D8), or 0 when it is broken, truncated or lossless.
    private static int Measure(byte[] data, int start)
    {
        var at = start + 2;
        var frame = false;
        while (at + 1 < data.Length)
        {
            if (data[at] != 0xFF)
            {
                return 0;
            }

            var marker = data[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            if (marker == 0xD9)
            {
                return frame ? at + 2 - start : 0;
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                at += 2;
                continue;
            }

            if (marker is 0xC3 or 0xC7 or 0xCB or 0xCF || at + 3 >= data.Length)
            {
                return 0;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at + 2, 2));
            if (length < 2 || at + 2 + length > data.Length)
            {
                return 0;
            }

            frame |= marker is 0xC0 or 0xC1 or 0xC2;
            at += 2 + length;
            if (marker == 0xDA)
            {
                if (!frame)
                {
                    return 0;
                }

                at = SkipScan(data, at);
            }
        }

        return 0;
    }

    // Entropy-coded data runs to the next marker that is not a stuffed zero or a restart.
    private static int SkipScan(byte[] data, int at)
    {
        while (at + 1 < data.Length)
        {
            if (data[at] == 0xFF && data[at + 1] != 0x00 && data[at + 1] is not (>= 0xD0 and <= 0xD7))
            {
                return at;
            }

            at++;
        }

        return data.Length;
    }
}
