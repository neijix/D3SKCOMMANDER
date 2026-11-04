using Avalonia.Controls;
using Avalonia.Threading;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Dialogs;

/// <summary>Shows the shared dialog window as an owned top-level modal.</summary>
public sealed class WindowDialogSurface : IDialogSurface
{
    private readonly IDialogOwner _owner;
    private readonly ILocalizer _localizer;
    private readonly IFolderPicker _folders;

    public WindowDialogSurface(IDialogOwner owner, ILocalizer localizer, IFolderPicker folders)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(folders);
        _owner = owner;
        _localizer = localizer;
        _folders = folders;
    }

    public Task<DialogResult> ShowAsync(DialogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowOnUiAsync(request, cancellationToken);
        }

        return Dispatcher.UIThread.InvokeAsync(() => ShowOnUiAsync(request, cancellationToken));
    }

    private async Task<DialogResult> ShowOnUiAsync(DialogRequest request, CancellationToken cancellationToken)
    {
        var owner = _owner.Current ?? throw new InvalidOperationException("A dialog needs the main window as its owner.");
        var model = DialogWindowModel.Create(request, _localizer, _folders);
        var window = new DialogWindow(model);
        using var registration = cancellationToken.Register(() => CloseCancelled(window));
        var result = await window.ShowDialog<DialogResult?>(owner);
        return result ?? DialogResult.Cancel();
    }

    private static void CloseCancelled(DialogWindow window)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CloseIfOpen(window);
            return;
        }

        Dispatcher.UIThread.Post(() => CloseIfOpen(window));
    }

    private static void CloseIfOpen(Window window)
    {
        if (window.IsVisible)
        {
            window.Close(DialogResult.Cancel());
        }
    }
}
