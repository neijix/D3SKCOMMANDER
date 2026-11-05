namespace D3skCommander.Core.Crypt;

internal static class StreamBytes
{
    public static bool ReadExact(Stream input, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = input.Read(buffer[read..]);
            if (n == 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }
}
