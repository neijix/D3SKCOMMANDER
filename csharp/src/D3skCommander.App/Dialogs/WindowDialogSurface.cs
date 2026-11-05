using Avalonia.Controls;
using D3skCommander.App.Host;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Dialogs;

/// <summary>Shows the shared dialog window as a modal owned by the main window, one at a time.</summary>
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

    /// <summary>How the window is shown; tests replace it to simulate a show that faults.</summary>
    internal Func<Window, Window, Task> ShowWindow { get; set; } = static (window, owner) => window.ShowDialog(owner);

    public Task<DialogResult> ShowAsync(DialogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var done = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        UiThread.Post(() => Open(request, cancellationToken, done));
        return done.Task;
    }

    private void Open(DialogRequest request, CancellationToken cancellationToken, TaskCompletionSource<DialogResult> done)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            done.TrySetResult(DialogResult.Cancel());
            return;
        }

        CancellationTokenRegistration registration = default;
        try
        {
            var owner = _owner.Current ?? throw new InvalidOperationException("A dialog needs the main window as its owner.");
            var model = DialogWindowModel.Create(request, _localizer, _folders);
            var window = new DialogWindow(model);
            registration = cancellationToken.Register(() => UiThread.Post(() => CloseIfOpen(window)));
            window.Closed += (_, _) =>
            {
                registration.Dispose();
                done.TrySetResult(window.Result ?? DialogResult.Cancel());
            };
            var shown = ShowWindow(window, owner);
            _ = shown.ContinueWith(
                task =>
                {
                    registration.Dispose();
                    done.TrySetException(task.Exception!.GetBaseException());
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            registration.Dispose();
            done.TrySetException(ex);
        }
    }

    private static void CloseIfOpen(Window window)
    {
        if (window.IsVisible)
        {
            window.Close();
        }
    }
}
