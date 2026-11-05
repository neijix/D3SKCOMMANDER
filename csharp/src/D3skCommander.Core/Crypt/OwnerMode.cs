namespace D3skCommander.Core.Crypt;

/// <summary>Unix mode 0600. Windows gets no ACL (parity decision).</summary>
internal static class OwnerMode
{
    public static void Apply(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
