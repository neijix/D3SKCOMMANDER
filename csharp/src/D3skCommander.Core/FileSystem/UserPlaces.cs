namespace D3skCommander.Core.FileSystem;

/// <summary>A well-known folder of the user: the localizer key of its name and where it is.</summary>
public sealed record UserPlace(string Key, string Path);

/// <summary>Home, Desktop, Documents, Downloads, Pictures, Music, Videos: those that exist.</summary>
public static class UserPlaces
{
    private static readonly (string Key, string Xdg, string Fallback)[] Folders =
    [
        ("places.desktop", "XDG_DESKTOP_DIR", "Desktop"),
        ("places.documents", "XDG_DOCUMENTS_DIR", "Documents"),
        ("places.downloads", "XDG_DOWNLOAD_DIR", "Downloads"),
        ("places.pictures", "XDG_PICTURES_DIR", "Pictures"),
        ("places.music", "XDG_MUSIC_DIR", "Music"),
        ("places.videos", "XDG_VIDEOS_DIR", "Videos"),
    ];

    /// <summary>
    /// The places under <paramref name="home"/>. <paramref name="userDirs"/> is the text of
    /// <c>~/.config/user-dirs.dirs</c> (null when absent): its <c>XDG_*_DIR</c> lines win over the plain names.
    /// </summary>
    public static IReadOnlyList<UserPlace> Standard(string home, string? userDirs, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(exists);
        var places = new List<UserPlace>();
        if (home.Length == 0)
        {
            return places;
        }

        if (exists(home))
        {
            places.Add(new UserPlace("places.home", home));
        }

        var configured = ParseUserDirs(userDirs, home);
        foreach (var (key, xdg, fallback) in Folders)
        {
            var path = configured.TryGetValue(xdg, out var value) ? value : System.IO.Path.Combine(home, fallback);
            if (!string.Equals(path.TrimEnd('/'), home.TrimEnd('/'), StringComparison.Ordinal)
                && exists(path)
                && !places.Exists(place => place.Path == path))
            {
                places.Add(new UserPlace(key, path));
            }
        }

        return places;
    }

    /// <summary>Lines like <c>XDG_MUSIC_DIR="$HOME/Tunes"</c> as name to absolute path.</summary>
    public static Dictionary<string, string> ParseUserDirs(string? text, string home)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (line.StartsWith('#') || equals <= 0)
            {
                continue;
            }

            var name = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim().Trim('"');
            if (value.StartsWith("$HOME", StringComparison.Ordinal))
            {
                value = home.TrimEnd('/') + value["$HOME".Length..];
            }

            if (name.StartsWith("XDG_", StringComparison.Ordinal) && value.StartsWith('/'))
            {
                result[name] = value.Length > 1 ? value.TrimEnd('/') : value;
            }
        }

        return result;
    }
}
