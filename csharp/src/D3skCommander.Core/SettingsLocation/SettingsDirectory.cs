using System.Text;
using D3skCommander.Core.Install;

namespace D3skCommander.Core.SettingsLocation;

/// <summary>
/// Where the settings folder is. The first that applies wins:
/// <c>D3SK_CONFIG_DIR</c>, then a portable <c>prefs.json</c> next to the program,
/// then the pointer file, then existing preferences in the platform folder,
/// then <c>&lt;install&gt;/config</c> when that install folder is writable, else the platform folder.
/// A <c>.deb</c>, a macOS app and a source build have no install folder.
/// </summary>
public static class SettingsDirectory
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>Resolves <paramref name="places"/> in the order above.</summary>
    public static SettingsFolder Resolve(SettingsPlaces places)
    {
        ArgumentNullException.ThrowIfNull(places);
        if (places.Env is not null)
        {
            return new SettingsFolder(places.Env, SettingsSource.Env);
        }

        if (places.ExeDir is not null && IsFile(Path.Combine(places.ExeDir, SettingsFileNames.Preferences)))
        {
            return new SettingsFolder(places.ExeDir, SettingsSource.Portable);
        }

        var chosen = ChosenPointer(places);
        if (chosen is not null)
        {
            return new SettingsFolder(chosen, SettingsSource.Chosen);
        }

        if (IsFile(Path.Combine(places.PlatformDir, SettingsFileNames.Preferences)))
        {
            return new SettingsFolder(places.PlatformDir, SettingsSource.Existing);
        }

        return new SettingsFolder(places.DefaultDir(), SettingsSource.Default);
    }

    /// <summary>
    /// The pointer that applies. A writable install folder reads its own pointer first, then the platform one.
    /// An install folder the user cannot write (a per-machine install) reads the platform pointer first,
    /// and honours the install-folder pointer only when its target is writable by this user.
    /// </summary>
    private static string? ChosenPointer(SettingsPlaces places)
    {
        var fromInstall = places.InstallDir is null ? null : ReadPointer(places.InstallDir);
        var fromPlatform = ReadPointer(places.PlatformDir);
        if (places.InstallWritable)
        {
            return fromInstall ?? fromPlatform;
        }

        if (fromPlatform is not null)
        {
            return fromPlatform;
        }

        return fromInstall is not null && CanWrite(fromInstall) ? fromInstall : null;
    }

    /// <summary>
    /// The folder named by <c>config-location.txt</c> in <paramref name="home"/>, or null.
    /// The file is UTF-8 (a BOM is allowed) or UTF-16LE with a BOM. A relative line is relative to <paramref name="home"/>.
    /// </summary>
    public static string? ReadPointer(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(Path.Combine(home, SettingsFileNames.Pointer));
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return null;
        }

        var line = FirstLine(DecodePointer(bytes));
        if (line is null)
        {
            return null;
        }

        return Path.IsPathFullyQualified(line) ? line : Path.Combine(home, line);
    }

    /// <summary>
    /// Writes <c>config-location.txt</c> in <paramref name="home"/> naming <paramref name="dir"/>.
    /// The name is relative when <paramref name="dir"/> lies inside <paramref name="home"/>.
    /// </summary>
    public static void WritePointer(string home, string dir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(dir);
        Directory.CreateDirectory(home);
        var tmp = Path.Combine(home, SettingsFileNames.Pointer + ".tmp");
        var dest = Path.Combine(home, SettingsFileNames.Pointer);
        File.WriteAllText(tmp, PointerText(home, dir) + "\n", Utf8);
        File.Move(tmp, dest, overwrite: true);
    }

    /// <summary>
    /// Moves the settings files from <paramref name="from"/> to <paramref name="to"/>.
    /// A file <paramref name="to"/> already has stays there and stays in <paramref name="from"/> too.
    /// </summary>
    public static void MoveFiles(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        Directory.CreateDirectory(to);
        if (SameDirectory(from, to))
        {
            return;
        }

        foreach (var name in SettingsFileNames.MovedWithTheFolder)
        {
            var src = Path.Combine(from, name);
            var dst = Path.Combine(to, name);
            if (!IsFile(src) || EntryExists(dst))
            {
                continue;
            }

            try
            {
                File.Move(src, dst);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                File.Copy(src, dst);
                File.Delete(src);
            }
        }
    }

    /// <summary>
    /// The installation folder of <paramref name="kind"/>: the parent of an AppImage, a Windows setup
    /// , a portable program or an <c>install.sh</c> folder. A <c>.deb</c>, a macOS app and a source build have none.
    /// </summary>
    public static string? InstallDirectory(InstallKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (kind is InstallKind.LinuxScript script)
        {
            return script.Folder;
        }

        var path = kind switch
        {
            InstallKind.AppImage(var file) => file,
            InstallKind.WindowsSetup(var file) => file,
            InstallKind.Portable(var file) => file,
            _ => null,
        };
        return path is null ? null : Path.GetDirectoryName(path);
    }

    /// <summary>True when a file can be created in <paramref name="dir"/> (tried, then removed).</summary>
    internal static bool CanWrite(string dir)
    {
        var probe = Path.Combine(dir, ".d3sk-write-test");
        try
        {
            File.WriteAllBytes(probe, []);
            return true;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (Exception ex) when (IsFileSystem(ex))
            {
                // A leftover empty probe is overwritten on the next try.
            }
        }
    }

    private static string DecodePointer(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            var length = bytes.Length - 2;
            length -= length % 2;
            return Encoding.Unicode.GetString(bytes, 2, length);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? FirstLine(string text)
    {
        foreach (var line in text.Split(LineBreaks))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return null;
    }

    /// <summary>Relative when <paramref name="dir"/> is inside <paramref name="home"/>; <c>.</c> when they are the same.</summary>
    private static string PointerText(string home, string dir)
    {
        var homeParts = Components(home);
        var dirParts = Components(dir);
        if (dirParts.Count < homeParts.Count)
        {
            return dir;
        }

        for (var i = 0; i < homeParts.Count; i++)
        {
            if (!string.Equals(homeParts[i], dirParts[i], PathComparison))
            {
                return dir;
            }
        }

        if (dirParts.Count == homeParts.Count)
        {
            return ".";
        }

        return string.Join(Path.DirectorySeparatorChar, dirParts.Skip(homeParts.Count));
    }

    private static List<string> Components(string path)
    {
        var parts = new List<string>();
        var rest = path;
        if (OperatingSystem.IsWindows() && rest.Length >= 2 && rest[1] == ':' && char.IsAsciiLetter(rest[0]))
        {
            parts.Add(rest[..2]);
            rest = rest[2..];
        }
        else if (OperatingSystem.IsWindows()
            && (rest.StartsWith(@"\\", StringComparison.Ordinal) || rest.StartsWith("//", StringComparison.Ordinal)))
        {
            parts.Add(@"\\");
            rest = rest[2..];
        }

        var rooted = rest.StartsWith('/') || (OperatingSystem.IsWindows() && rest.StartsWith('\\'));
        if (rooted)
        {
            parts.Add("/");
            rest = rest.TrimStart('/', '\\');
        }

        if (rest.Length > 0)
        {
            var separators = OperatingSystem.IsWindows() ? new[] { '/', '\\' } : new[] { '/' };
            parts.AddRange(rest.Split(separators, StringSplitOptions.RemoveEmptyEntries));
        }

        return parts;
    }

    private static bool SameDirectory(string left, string right)
    {
        var a = TryCanonical(left);
        var b = TryCanonical(right);
        if (a is not null && b is not null)
        {
            return string.Equals(TrimSlash(a), TrimSlash(b), PathComparison);
        }

        return string.Equals(left, right, PathComparison);
    }

    private static string? TryCanonical(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            return (resolved ?? info).FullName;
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return null;
        }
    }

    private static string TrimSlash(string path) =>
        path.Length <= 1 ? path : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsFile(string path)
    {
        try
        {
            return File.Exists(path) && !Directory.Exists(path);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    private static bool EntryExists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return false;
        }
    }

    private static bool IsFileSystem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
