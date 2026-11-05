using System.Text.Json;

namespace D3skCommander.Core.Update;

/// <summary>
/// Reads a GitHub "release" API document. Drafts, pre-releases and unreadable tags are
/// refused; only fully uploaded files are kept. Checksums come from each asset's <c>digest</c>.
/// </summary>
public static class ReleaseParser
{
    public static UpdateStep<GitHubRelease> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException ex)
        {
            return UpdateStep<GitHubRelease>.Fail(ex.Message);
        }
    }

    private static UpdateStep<GitHubRelease> Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return UpdateStep<GitHubRelease>.Fail("unreadable release");
        }

        var tag = Text(root, "tag_name");
        if (tag is null)
        {
            return UpdateStep<GitHubRelease>.Fail("missing tag_name");
        }

        var flagError = Flags(root, out var skipped);
        if (flagError is not null)
        {
            return UpdateStep<GitHubRelease>.Fail(flagError);
        }

        if (skipped)
        {
            return UpdateStep<GitHubRelease>.Fail(tag + " is not a final release");
        }

        if (ReleaseVersion.Parse(tag) is null)
        {
            return UpdateStep<GitHubRelease>.Fail("unreadable version: " + tag);
        }

        var page = Text(root, "html_url");
        if (page is null)
        {
            return UpdateStep<GitHubRelease>.Fail("missing html_url");
        }

        var notes = Notes(root, out var notesError);
        if (notesError is not null)
        {
            return UpdateStep<GitHubRelease>.Fail(notesError);
        }

        var assets = Assets(root, out var assetError);
        if (assetError is not null)
        {
            return UpdateStep<GitHubRelease>.Fail(assetError);
        }

        return UpdateStep<GitHubRelease>.Ok(new GitHubRelease(StripTag(tag), notes, page, assets));
    }

    private static string? Flags(JsonElement root, out bool skipped)
    {
        skipped = false;
        var draft = Flag(root, "draft", out var draftError);
        if (draftError is not null)
        {
            return draftError;
        }

        var pre = Flag(root, "prerelease", out var preError);
        if (preError is not null)
        {
            return preError;
        }

        skipped = draft || pre;
        return null;
    }

    private static bool Flag(JsonElement root, string name, out string? error)
    {
        error = null;
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
        {
            error = "unreadable " + name;
            return false;
        }

        return value.GetBoolean();
    }

    private static string Notes(JsonElement root, out string? error)
    {
        error = null;
        if (!root.TryGetProperty("body", out var body) || body.ValueKind == JsonValueKind.Null)
        {
            return "";
        }

        if (body.ValueKind != JsonValueKind.String)
        {
            error = "unreadable body";
            return "";
        }

        return (body.GetString() ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static List<ReleaseAsset> Assets(JsonElement root, out string? error)
    {
        error = null;
        var list = new List<ReleaseAsset>();
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind == JsonValueKind.Null)
        {
            return list;
        }

        if (assets.ValueKind != JsonValueKind.Array)
        {
            error = "unreadable assets";
            return list;
        }

        foreach (var element in assets.EnumerateArray())
        {
            var asset = ReadAsset(element, out var keep, out error);
            if (error is not null)
            {
                return list;
            }

            if (keep && asset is not null)
            {
                list.Add(asset);
            }
        }

        return list;
    }

    private static ReleaseAsset? ReadAsset(JsonElement element, out bool keep, out string? error)
    {
        keep = false;
        error = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "unreadable asset";
            return null;
        }

        var name = Text(element, "name");
        var url = Text(element, "browser_download_url");
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
        {
            error = "unreadable asset";
            return null;
        }

        if (!element.TryGetProperty("size", out var sizeElement) || !sizeElement.TryGetInt64(out var size) || size < 0)
        {
            error = "unreadable asset";
            return null;
        }

        var uploaded = Uploaded(element, out error);
        if (error is not null)
        {
            return null;
        }

        var sha = Digest(element, out error);
        if (error is not null)
        {
            return null;
        }

        keep = uploaded;
        return new ReleaseAsset(name, url, size, sha);
    }

    private static bool Uploaded(JsonElement element, out string? error)
    {
        error = null;
        if (!element.TryGetProperty("state", out var state) || state.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (state.ValueKind != JsonValueKind.String)
        {
            error = "unreadable asset";
            return false;
        }

        return state.GetString() == "uploaded";
    }

    private static string? Digest(JsonElement element, out string? error)
    {
        error = null;
        if (!element.TryGetProperty("digest", out var digest) || digest.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (digest.ValueKind != JsonValueKind.String)
        {
            error = "unreadable asset";
            return null;
        }

        var text = digest.GetString();
        const string Prefix = "sha256:";
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return text[Prefix.Length..].ToLowerInvariant();
    }

    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static string StripTag(string tag)
    {
        var start = 0;
        while (start < tag.Length && (tag[start] == 'v' || tag[start] == 'V'))
        {
            start++;
        }

        return tag[start..];
    }
}
