using System.Globalization;

namespace D3skCommander.Core.Update;

/// <summary>Installs a <c>.deb</c> with <c>pkexec dpkg -i</c>. The password dialog belongs to polkit.</summary>
public static class DebInstall
{
    public static string Manual(string file) => "sudo dpkg -i \"" + file + "\"";

    public static string[] Arguments(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        return ["/usr/bin/dpkg", "-i", file];
    }

    /// <summary>Null when dpkg succeeded. Otherwise the English detail, including the manual command.</summary>
    public static string? Apply(string file, IUpdateProcess process)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        ArgumentNullException.ThrowIfNull(process);
        var manual = Manual(file);
        int code;
        try
        {
            code = process.Run("pkexec", Arguments(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return "pkexec: " + ex.Message + " — install it by hand: " + manual;
        }

        if (code == 0)
        {
            return null;
        }

        if (code == 126 || code == 127)
        {
            return "administrator rights refused — or run: " + manual;
        }

        return "dpkg failed (exit status: " + code.ToString(CultureInfo.InvariantCulture) + ") — try: " + manual;
    }
}
