using Avalonia.Media;
using D3skCommander.Core.DiskUsage;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.DiskUsage;

/// <summary>One line of the list: a child of the node on show, with its size, share of the parent and heat bar.</summary>
public sealed class DiskUsageRow
{
    public const double BarMax = 110;

    public DiskUsageRow(SizeNode node, SizeNode parent, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(text);
        Node = node;
        Name = NameOf(node, text);
        SizeText = LocalFileSystem.FormatSize((ulong)node.Bytes);
        PercentText = SizeShares.PercentText(node.Bytes, parent.Bytes);
        var fraction = SizeShares.Percent(node.Bytes, parent.Bytes) / 100;
        BarWidth = node.Bytes > 0 ? Math.Max(2, fraction * BarMax) : 0;
        Heat = DiskUsageHeat.ForShare(fraction);
        Marker = node.Kind == SizeNodeKind.Folder ? "▸" : "";
    }

    public SizeNode Node { get; }

    public string Name { get; }

    public string SizeText { get; }

    public string PercentText { get; }

    public double BarWidth { get; }

    public IBrush Heat { get; }

    /// <summary>A small arrow in front of the folders, the rows Enter opens.</summary>
    public string Marker { get; }

    public bool IsFolder => Node.Kind == SizeNodeKind.Folder;

    public static string NameOf(SizeNode node, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(text);
        return node.Kind switch
        {
            SizeNodeKind.SmallFiles => string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                text.Get("diskusage.smaller_files"),
                LocalFileSystem.FormatGrouped((ulong)node.Files)),
            _ when node.IsUnreadable => node.Name + " " + text.Get("diskusage.unreadable_mark"),
            _ => node.Name,
        };
    }
}
