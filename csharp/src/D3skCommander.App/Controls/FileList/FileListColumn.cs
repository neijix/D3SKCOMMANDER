namespace D3skCommander.App.Controls.FileList;

/// <summary>One column. <see cref="HeaderKey"/> is an en/fr catalog key, not display text.</summary>
public sealed class FileListColumn
{
    public FileListColumn(string headerKey, double width, bool star = false)
    {
        ArgumentNullException.ThrowIfNull(headerKey);
        HeaderKey = headerKey;
        Width = width;
        Star = star;
    }

    public string HeaderKey { get; }

    public double Width { get; }

    public bool Star { get; }
}
