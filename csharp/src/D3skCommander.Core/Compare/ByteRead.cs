namespace D3skCommander.Core.Compare;

/// <summary>Fills a buffer, retrying short reads the way v0.1's <c>read_full</c> does.</summary>
internal static class ByteRead
{
    public static int Full(Stream stream, byte[] buffer)
    {
        var count = 0;
        while (count < buffer.Length)
        {
            var read = stream.Read(buffer, count, buffer.Length - count);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        return count;
    }
}
