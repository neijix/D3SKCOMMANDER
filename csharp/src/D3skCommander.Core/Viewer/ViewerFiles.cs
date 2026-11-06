using D3skCommander.Core.FileKinds;

namespace D3skCommander.Core.Viewer;

/// <summary>
/// How the lister classifies a path before it reads it. Image extensions are the ones the App
/// decodes with Avalonia (PNG, JPEG, GIF, BMP, WebP, ICO) plus TIFF, which Core decodes
/// (<see cref="TiffImage"/>); <see cref="PictureSignature"/> catches a picture misnamed.
/// </summary>
public static class ViewerFiles
{
    private static readonly string[] ImageExtensions =
    [
        "png", "jpg", "jpeg", "jpe", "jfif", "gif", "bmp", "dib", "webp", "ico", "tif", "tiff",
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

    /// <summary>
    /// True for a picture format the app lists as an image but cannot show (HEIC, AVIF, camera
    /// RAW, PSD, SVG…): the viewer opens its bytes and says why.
    /// </summary>
    public static bool IsUnsupportedPicture(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return !IsImage(path) && FileKindClassifier.OfFile(path) == FileKind.Image;
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
