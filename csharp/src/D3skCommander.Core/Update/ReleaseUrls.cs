namespace D3skCommander.Core.Update;

/// <summary>The GitHub API address of the latest release of a <c>https://github.com/owner/name</c> repository.</summary>
public static class ReleaseUrls
{
    /// <summary>Null when <paramref name="repository"/> is not an <c>https://github.com/owner/name</c> address.</summary>
    public static string? Latest(string? repository)
    {
        if (string.IsNullOrEmpty(repository))
        {
            return null;
        }

        var rest = repository;
        while (rest.EndsWith('/'))
        {
            rest = rest[..^1];
        }

        const string Prefix = "https://github.com/";
        if (!rest.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = rest[Prefix.Length..].Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return null;
        }

        return "https://api.github.com/repos/" + parts[0] + "/" + parts[1] + "/releases/latest";
    }
}
