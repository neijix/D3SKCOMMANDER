using D3skCommander.Core.Common;

namespace D3skCommander.Core.FileSystem;

/// <summary>Stem and extension from <see cref="LocalFileSystem.SplitExtension"/>.</summary>
public readonly record struct ExtensionParts(string Stem, string Extension);

public static partial class LocalFileSystem
{
    /// <summary>Splits "archive.tar.gz" into ("archive.tar", "gz"). ".bashrc" has no extension.</summary>
    public static ExtensionParts SplitExtension(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dot = name.LastIndexOf('.');
        if (dot <= 0)
        {
            return new ExtensionParts(name, "");
        }

        return new ExtensionParts(name[..dot], name[(dot + 1)..]);
    }

    /// <summary>A typed name: not empty, not "." or "..", and no separator.</summary>
    public static OperationError? ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed == "." || trimmed == "..")
        {
            return OperationError.InvalidData("invalid name");
        }

        if (ContainsForbidden(name))
        {
            return OperationError.InvalidData("the name contains a forbidden character");
        }

        return null;
    }

    private static bool ContainsForbidden(string name)
    {
        if (name.Contains('/'))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return name.IndexOfAny(['\\', ':', '*', '?', '"', '<', '>', '|']) >= 0;
    }
}
