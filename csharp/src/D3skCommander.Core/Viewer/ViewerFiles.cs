namespace D3skCommander.Core.Viewer;

/// <summary>
/// How the lister classifies a path before it reads it. Image extensions are the seven the App
/// decodes with Avalonia (<c>png</c> through <c>ico</c>) plus TIFF, which Core decodes
/// (<see cref="TiffImage"/>).
/// </summary>
public static class ViewerFiles
{
    private static readonly string[] ImageExtensions =
    [
        "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico", "tif", "tiff",
    ];

    private static readonly string[] TiffExtensions = ["tif", "tiff"];

    public static bool IsImage(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Listed(Extension(path), ImageExtensions);
    }

    /// <summary>True for a TIFF file, which Core decodes (<see cref="TiffImage"/>) instead of the App.</summary>
    public static bool IsTiff(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Listed(Extension(path), TiffExtensions);
    }

    public static bool IsDbf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Extension(path) == "dbf";
    }

    public static string Extension(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        if (extension.Length <= 1)
        {
            return "";
        }

        return extension[1..].ToLowerInvariant();
    }

    private static bool Listed(string extension, string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (extension == names[i])
            {
                return true;
            }
        }

        return false;
    }
}
