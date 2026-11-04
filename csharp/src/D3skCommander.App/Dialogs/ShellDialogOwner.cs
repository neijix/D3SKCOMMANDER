using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace D3skCommander.App.Dialogs;

/// <summary>The desktop main window, or the modal already open on it (so Browse stays with that dialog).</summary>
public sealed class ShellDialogOwner : IDialogOwner
{
    public Window? Current
    {
        get
        {
            var main = DesktopWindow();
            if (main is null)
            {
                return null;
            }

            return VisibleDialog(main) ?? main;
        }
    }

    private static Window? DesktopWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    private static Window? VisibleDialog(Window main)
    {
        Window? top = null;
        foreach (var owned in main.OwnedWindows)
        {
            if (owned.IsVisible)
            {
                top = owned;
            }
        }

        return top;
    }
}
