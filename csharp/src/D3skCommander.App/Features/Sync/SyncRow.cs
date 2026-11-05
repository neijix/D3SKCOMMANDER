using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Sync;

namespace D3skCommander.App.Features.Sync;

/// <summary>One action a row may take, with its label.</summary>
public sealed record SyncActionOption(SyncAction Action, string Label);

/// <summary>One line of the plan: the item, its status, and the action the review may change.</summary>
public sealed partial class SyncRow : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private SyncActionOption _chosen;

    public SyncRow(SyncItem item, ILocalizer text, Action changed)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(changed);
        Item = item;
        _changed = changed;
        var options = new List<SyncActionOption>();
        foreach (var action in SyncPlan.ValidActions(item.Status))
        {
            options.Add(new SyncActionOption(action, Label(text, action)));
        }

        Options = options;
        _chosen = options.Find(option => option.Action == item.Action) ?? options[0];
        Symbol = SyncPlan.StatusSymbol(item.Status);
        Name = item.IsDir ? item.Rel + Path.DirectorySeparatorChar : item.Rel;
        LeftInfo = Info(item.Left, item.IsDir);
        RightInfo = Info(item.Right, item.IsDir);
    }

    public SyncItem Item { get; }

    public IReadOnlyList<SyncActionOption> Options { get; }

    public string Symbol { get; }

    public string Name { get; }

    public string LeftInfo { get; }

    public string RightInfo { get; }

    public SyncAction Action => Item.Action;

    public static string Label(ILocalizer text, SyncAction action)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (action == SyncAction.CopyToRight)
        {
            return text.Get("sync.action.copy_right");
        }

        if (action == SyncAction.CopyToLeft)
        {
            return text.Get("sync.action.copy_left");
        }

        if (action == SyncAction.DeleteRight)
        {
            return text.Get("sync.action.delete_right");
        }

        if (action == SyncAction.DeleteLeft)
        {
            return text.Get("sync.action.delete_left");
        }

        return text.Get("sync.action.none");
    }

    partial void OnChosenChanged(SyncActionOption value)
    {
        if (value is null || value.Action == Item.Action)
        {
            return;
        }

        Item.Action = value.Action;
        _changed();
    }

    private static string Info(SyncSide? side, bool isDir)
    {
        if (side is null)
        {
            return "";
        }

        var time = LocalFileSystem.FormatTime(side.Modified);
        return isDir ? time : LocalFileSystem.FormatSize(side.Size) + "  " + time;
    }
}
