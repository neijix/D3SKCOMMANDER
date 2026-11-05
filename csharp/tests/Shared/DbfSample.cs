using System.Text;

namespace D3skCommander.Tests.Shared;

/// <summary>A two-record dBase file (NAME column: "Ann", and "Bob" marked deleted), compiled into both test projects.</summary>
internal static class DbfSample
{
    public static byte[] NameFile()
    {
        var bytes = new byte[32 + 32 + 1 + 5 + 5 + 1];
        bytes[0] = 0x03;
        bytes[4] = 2;
        bytes[8] = 65;
        bytes[10] = 5;
        Encoding.ASCII.GetBytes("NAME").CopyTo(bytes, 32);
        bytes[32 + 11] = (byte)'C';
        bytes[32 + 16] = 4;
        bytes[64] = 0x0D;
        Encoding.ASCII.GetBytes(" Ann ").CopyTo(bytes, 65);
        Encoding.ASCII.GetBytes("*Bob ").CopyTo(bytes, 70);
        bytes[^1] = 0x1A;
        return bytes;
    }
}
