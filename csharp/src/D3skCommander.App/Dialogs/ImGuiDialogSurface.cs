using D3skCommander.App.Host;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Dialogs;

/// <summary>Shows the shared dialog as an owned top-level ImGui modal.</summary>
public sealed class ImGuiDialogSurface : IDialogSurface
{
    private readonly IDialogOwner _owner;
    private readonly ILocalizer _localizer;
    private readonly IFolderPicker _folders;
    private DialogWindow? _current;

    public ImGuiDialogSurface(IDialogOwner owner, ILocalizer localizer, IFolderPicker folders)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(folders);
        _owner = owner;
        _localizer = localizer;
        _folders = folders;
    }

    public DialogWindow? Current => _current;

    public Task<DialogResult> ShowAsync(DialogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var done = new TaskCompletionSource<DialogResult>();
        void Open() => OpenCore(request, cancellationToken, done);
        if (UiThread.CheckAccess())
        {
            Open();
        }
        else
        {
            UiThread.Post(Open);
        }

        return done.Task;
    }

    public void Draw() => _current?.Draw();

    private void OpenCore(DialogRequest request, CancellationToken cancellationToken, TaskCompletionSource<DialogResult> done)
    {
        var owner = _owner.Current ?? throw new InvalidOperationException("A dialog needs the main window as its owner.");
        var model = DialogWindowModel.Create(request, _localizer, _folders);
        var window = new DialogWindow(owner, model);
        model.Attach(result =>
        {
            window.Close();
            if (ReferenceEquals(_current, window))
            {
                _current = null;
            }

            done.TrySetResult(result);
        });
        _current = window;
        window.Show();
        cancellationToken.Register(() => UiThread.Post(() =>
        {
            if (window.IsVisible)
            {
                window.Close();
                done.TrySetResult(DialogResult.Cancel());
            }
        }));
    }
}
