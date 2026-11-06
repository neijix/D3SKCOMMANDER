using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace D3skCommander.App.Features.Tree;

/// <summary>The folder tree of one panel: a tree of folders, Enter and a context menu on a node.</summary>
public sealed partial class FolderTreeView : UserControl
{
    private readonly MenuItem _openOther = new();
    private readonly MenuItem _refresh = new();
    private FolderTreeModel? _model;
    private FolderTreeNode? _menuNode;

    public FolderTreeView()
    {
        InitializeComponent();
        Tree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        Tree.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Tree.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public FolderTreeView(FolderTreeModel model, Action<FolderTreeNode> openOther)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(openOther);
        _model = model;
        DataContext = model;
        model.ScrollRequested += (_, node) => Dispatcher.UIThread.Post(() => Reveal(node), DispatcherPriority.Background);
        _openOther.Header = model.OpenOtherText;
        _refresh.Header = model.RefreshText;
        _openOther.Click += (_, _) => RunOnMenuNode(openOther);
        _refresh.Click += (_, _) => RunOnMenuNode(node => _ = model.RefreshNodeAsync(node));
        var menu = new ContextMenu { ItemsSource = new[] { _openOther, _refresh } };
        menu.Opening += OnMenuOpening;
        menu.Closed += (_, _) => _menuNode = null;
        Tree.ContextMenu = menu;
    }

    /// <summary>The TreeView, for tests that click or select nodes as a user would.</summary>
    public TreeView View => Tree;

    private void Reveal(FolderTreeNode node)
    {
        if (Tree.TreeContainerFromItem(node) is not Control container)
        {
            return;
        }

        // A rectangle as tall as the viewport, centred on the row itself (22 high; the subtree below an expanded
        // node is not asked for): the row lands in the middle with rows around it.
        var view = Tree.FindDescendantOfType<ScrollViewer>()?.Viewport.Height ?? 0;
        var height = Math.Max(view, 22);
        container.BringIntoView(new Rect(0, 11 - height / 2, 1, height));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _menuNode = e.Source is Visual visual && visual.FindAncestorOfType<TreeViewItem>(true)?.DataContext is FolderTreeNode node ? node : null;
    }

    // A left click released on a node (not on its expander) goes there; the arrow keys only move the selection.
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || e.Source is not Visual visual)
        {
            return;
        }

        if (visual.FindAncestorOfType<ToggleButton>(true) is not null)
        {
            return;
        }

        if (visual.FindAncestorOfType<TreeViewItem>(true)?.DataContext is FolderTreeNode node)
        {
            _model?.Open(node);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Tree.SelectedItem is FolderTreeNode node)
        {
            _model?.Open(node);
            e.Handled = true;
        }
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _menuNode ??= Tree.SelectedItem as FolderTreeNode;
        if (_menuNode is null or { IsPlaceholder: true })
        {
            e.Cancel = true;
        }
    }

    private void RunOnMenuNode(Action<FolderTreeNode> action)
    {
        if (_menuNode is { IsPlaceholder: false } node)
        {
            action(node);
        }
    }
}
