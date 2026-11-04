using D3skCommander.App.Shell;

namespace D3skCommander.App.Dialogs;

/// <summary>The window a modal is owned by. Not <c>MainWindow</c> injected into the service.</summary>
public interface IDialogOwner
{
    MainWindow? Current { get; }
}
