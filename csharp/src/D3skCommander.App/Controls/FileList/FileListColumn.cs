using D3skCommander.Core.Panels;

namespace D3skCommander.App.Controls.FileList;

/// <summary>One column. <see cref="HeaderKey"/> is an en/fr catalog key, not display text.</summary>
public sealed class FileListColumn
{
    public FileListColumn(string headerKey, double width, bool star = false, ColumnId? id = null)
    {
        ArgumentNullException.ThrowIfNull(headerKey);
        HeaderKey = headerKey;
        Width = width;
        Star = star;
        Id = id ?? ColumnInfo.FromHeaderKey(headerKey);
    }

    public string HeaderKey { get; }

    public double Width { get; }

    public bool Star { get; }

    /// <summary>The panel column this shows, or null for a custom table column.</summary>
    public ColumnId? Id { get; }
}
