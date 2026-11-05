using System.Text;

namespace D3skCommander.Core.Compare;

/// <summary>Compares two files as text, unless either looks binary.</summary>
public static class FileCompare
{
    /// <summary>A NUL in the first 8 KiB makes the file binary.</summary>
    public const int BinarySniffBytes = 8 * 1024;

    /// <summary>Files larger than this are compared byte by byte, never as text.</summary>
    public const long MaxTextBytes = 32 * 1024 * 1024;

    private const int ChunkBytes = 64 * 1024;

    /// <summary>Byte-wise equality. Sizes are checked first, then a streamed compare.</summary>
    public static bool FilesIdentical(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (new FileInfo(left).Length != new FileInfo(right).Length)
        {
            return false;
        }

        return FirstDifference(left, right) is null;
    }

    /// <summary>Offset of the first differing byte, or null when the files are identical.</summary>
    public static long? FirstDifference(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        var leftBuffer = new byte[ChunkBytes];
        var rightBuffer = new byte[ChunkBytes];
        long offset = 0;
        while (true)
        {
            var leftCount = ByteRead.Full(leftStream, leftBuffer);
            var rightCount = ByteRead.Full(rightStream, rightBuffer);
            var found = DifferenceAt(leftBuffer, leftCount, rightBuffer, rightCount);
            if (found is int index)
            {
                return offset + index;
            }

            if (leftCount != rightCount)
            {
                return offset + Math.Min(leftCount, rightCount);
            }

            if (leftCount == 0)
            {
                return null;
            }

            offset += leftCount;
        }
    }

    /// <summary>Text, unless either file holds a NUL in its first 8 KiB or exceeds 32 MiB.</summary>
    public static FileDiff DiffFiles(string left, string right, DiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var leftLength = new FileInfo(left).Length;
        var rightLength = new FileInfo(right).Length;
        if (leftLength > MaxTextBytes || rightLength > MaxTextBytes || HasNul(left) || HasNul(right))
        {
            var first = FirstDifference(left, right);
            return FileDiff.Binary(first is null, first);
        }

        var leftText = Encoding.UTF8.GetString(File.ReadAllBytes(left));
        var rightText = Encoding.UTF8.GetString(File.ReadAllBytes(right));
        return FileDiff.Text(TextCompare.DiffText(leftText, rightText, options));
    }

    private static int? DifferenceAt(byte[] left, int leftCount, byte[] right, int rightCount)
    {
        var common = Math.Min(leftCount, rightCount);
        for (var i = 0; i < common; i++)
        {
            if (left[i] != right[i])
            {
                return i;
            }
        }

        return null;
    }

    private static bool HasNul(string path)
    {
        var buffer = new byte[BinarySniffBytes];
        using var stream = File.OpenRead(path);
        var count = ByteRead.Full(stream, buffer);
        for (var i = 0; i < count; i++)
        {
            if (buffer[i] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
