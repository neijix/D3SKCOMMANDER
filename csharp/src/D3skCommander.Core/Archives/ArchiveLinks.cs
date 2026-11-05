using SharpCompress.Common.SevenZip;
using SharpCompress.Common.Zip;

namespace D3skCommander.Core.Archives;

internal static class ArchiveLinks
{
    public static bool Zip(ZipEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.LinkTarget))
        {
            return true;
        }

        var mode = UnixMode(entry.Attrib);
        return mode is not null && (mode.Value & 0xF000) == 0xA000;
    }

    public static uint? ZipMode(ZipEntry entry) => UnixMode(entry.Attrib);

    public static bool SevenZip(SevenZipEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.LinkTarget))
        {
            return true;
        }

        if (entry.Attrib is null)
        {
            return false;
        }

        var attrs = unchecked((uint)entry.Attrib.Value);
        var unixSymlink = (attrs & 0x8000) != 0 && ((attrs >> 16) & 0xF000) == 0xA000;
        return unixSymlink || (attrs & 0x400) != 0;
    }

    private static uint? UnixMode(long? attrib)
    {
        if (attrib is null)
        {
            return null;
        }

        var high = (uint)((ulong)attrib.Value >> 16) & 0xFFFF;
        var type = high & 0xF000;
        if (type != 0x8000 && type != 0x4000 && type != 0xA000)
        {
            return null;
        }

        return high;
    }
}
