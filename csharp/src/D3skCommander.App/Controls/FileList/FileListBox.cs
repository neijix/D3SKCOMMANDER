using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace D3skCommander.App.Controls.FileList;

/// <summary>
/// The one virtualized list. Selection is applied by <see cref="FileList"/> on press,
/// so a move or a long hold is not dropped the way a click gesture would drop it.
/// </summary>
public sealed class FileListBox : ListBox
{
    public FileListBox()
    {
        SelectionMode = SelectionMode.Single;
    }

    public Control? ContainerFromSource(object? source) => GetContainerFromEventSource(source);

    public override bool UpdateSelectionFromEvent(Control container, RoutedEventArgs eventArgs) => false;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Headless tests load no theme, so ListBox would have no presenter and would realize nothing.
        if (Template is not null || !this.TryFindResource("FileListFallbackTemplate", out var found))
        {
            return;
        }

        if (found is IControlTemplate template)
        {
            Template = template;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled || FileListNavigation.IsNavigationKey(e.Key))
        {
            return;
        }

        base.OnKeyDown(e);
    }
}
