using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Features.Drives;

/// <summary>The drop-down under the panel's drive icon; the modal list when that button is not on screen.</summary>
public sealed class FlyoutDriveMenuPresenter : IDriveMenuPresenter
{
    private readonly IDialogOwner _owner;
    private readonly IDriveMenuPresenter _fallback;

    public FlyoutDriveMenuPresenter(IDialogOwner owner, IDriveMenuPresenter fallback)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(fallback);
        _owner = owner;
        _fallback = fallback;
    }

    /// <summary>The menu shown last (open or closed); tests drive it through its items.</summary>
    public ContextMenu? LastMenu { get; private set; }

    public async Task<DriveMenuItem?> ShowAsync(PanelSide side, IReadOnlyList<DriveMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var anchor = await Dispatcher.UIThread.InvokeAsync(() => FindAnchor(side));
        if (anchor is null)
        {
            return await _fallback.ShowAsync(side, items).ConfigureAwait(false);
        }

        var result = new TaskCompletionSource<DriveMenuItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft, PlacementTarget = anchor };
            menu.ItemsSource = BuildItems(items, item => result.TrySetResult(item), menu);
            menu.Closed += (_, _) => Dispatcher.UIThread.Post(() => result.TrySetResult(null), DispatcherPriority.Background);
            LastMenu = menu;
            menu.Open(anchor);
        });
        return await result.Task.ConfigureAwait(false);
    }

    private Button? FindAnchor(PanelSide side)
    {
        var command = side == PanelSide.Left ? CommandId.DrivesLeft : CommandId.DrivesRight;
        var window = _owner.Current;
        return window?.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.Tag is CommandId id && id == command && button.IsEffectivelyVisible);
    }

    private static List<Control> BuildItems(IReadOnlyList<DriveMenuItem> items, Action<DriveMenuItem> pick, ContextMenu menu)
    {
        var controls = new List<Control>();
        var section = -1;
        foreach (var item in items)
        {
            if (section >= 0 && item.Section != section)
            {
                controls.Add(new Separator());
            }

            section = item.Section;
            var line = item;
            var entry = new MenuItem { Header = Header(line), IsChecked = line.IsCurrent, ToggleType = line.IsCurrent ? MenuItemToggleType.CheckBox : MenuItemToggleType.None };
            entry.Click += (_, _) => pick(line);
            controls.Add(entry);
        }

        return controls;
    }

    private static Control Header(DriveMenuItem item)
    {
        var label = new TextBlock { Text = item.Text, FontWeight = item.IsCurrent ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal };
        if (item.UsedFraction is not { } used)
        {
            return label;
        }

        var stack = new StackPanel { Spacing = 2, MinWidth = 220 };
        stack.Children.Add(label);
        stack.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = used, Height = 4, MinHeight = 4, ShowProgressText = false, HorizontalAlignment = HorizontalAlignment.Stretch });
        return stack;
    }
}
