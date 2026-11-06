using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Index;

namespace D3skCommander.App.Features.QuickFind;

/// <summary>One line of the quick find list, formatted once when the results arrive.</summary>
public sealed class QuickFindRow
{
    public QuickFindRow(IndexHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        Path = hit.Path;
        IsFolder = hit.IsFolder;
        Marker = hit.IsFolder ? "▸" : "";
        Name = hit.Name;
        Folder = hit.Folder;
        SizeText = hit.IsFolder ? "" : LocalFileSystem.FormatSize((ulong)Math.Max(0, hit.Size));
        DateText = LocalFileSystem.FormatTime(hit.Modified);
    }

    public string Path { get; }

    public bool IsFolder { get; }

    public string Marker { get; }

    public string Name { get; }

    public string Folder { get; }

    public string SizeText { get; }

    public string DateText { get; }
}
