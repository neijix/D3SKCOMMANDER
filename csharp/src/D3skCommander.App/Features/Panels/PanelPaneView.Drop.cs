using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelPaneView
{
    private void EnableDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static List<string> DroppedPaths(DragEventArgs e)
    {
        var paths = new List<string>();
        var items = e.DataTransfer.TryGetFiles();
        if (items is null)
        {
            return paths;
        }

        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DroppedPaths(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = DroppedPaths(e);
        if (paths.Count > 0)
        {
            Model?.DropFiles(paths);
        }

        e.Handled = true;
    }
}
