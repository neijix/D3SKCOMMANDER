using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelWorkspace
{
    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.SwitchPanel, Switch);
        shell.Handle(CommandId.Parent, () => Run(() => ParentAsync()));
        shell.Handle(CommandId.Back, () => Run(() => BackAsync()));
        shell.Handle(CommandId.Forward, () => Run(() => ForwardAsync()));
        shell.Handle(CommandId.Home, () => Run(() => HomeAsync()));
        shell.Handle(CommandId.Root, () => Run(() => RootAsync()));
        shell.Handle(CommandId.Refresh, () => Run(() => RefreshAsync()));
        shell.Handle(CommandId.BrowseFolder, () => Run(() => BrowseAsync(_active)));
        shell.Handle(CommandId.GoToContainingFolder, () => Run(() => GoToContainingFolderAsync()));
        shell.Handle(CommandId.NewTab, () => Run(() => NewTabAsync()));
        shell.Handle(CommandId.CloseTab, CloseTab);
        shell.Handle(CommandId.NextTab, NextTab);
        shell.Handle(CommandId.Swap, () => Run(() => SwapAsync()));
        shell.Handle(CommandId.SameDir, () => Run(() => SameDirectoryAsync()));
        shell.Handle(CommandId.ViewFull, () => UseView(FileListView.Full));
        shell.Handle(CommandId.ViewBrief, () => UseView(FileListView.Brief));
        shell.Handle(CommandId.ViewThumbs, () => UseView(FileListView.Thumbs));
        shell.Handle(CommandId.ToggleHidden, () => Run(() => ToggleHiddenAsync()));
        shell.Handle(CommandId.ToggleDriveBar, ToggleDriveBar);
        shell.Handle(CommandId.ToggleTree, () => ToggleTree(_active));
        shell.Handle(CommandId.ToggleTreeLeft, () => ToggleTree(PanelSide.Left));
        shell.Handle(CommandId.ToggleTreeRight, () => ToggleTree(PanelSide.Right));
        shell.Checked(CommandId.ToggleTree, () => ShowTree(_active));
        shell.Checked(CommandId.ToggleTreeLeft, () => ShowTree(PanelSide.Left));
        shell.Checked(CommandId.ToggleTreeRight, () => ShowTree(PanelSide.Right));
        shell.Handle(CommandId.SortName, () => Run(() => SortAsync(SortKey.Name)));
        shell.Handle(CommandId.SortExt, () => Run(() => SortAsync(SortKey.Extension)));
        shell.Handle(CommandId.SortDate, () => Run(() => SortAsync(SortKey.Modified)));
        shell.Handle(CommandId.SortSize, () => Run(() => SortAsync(SortKey.Size)));
        shell.Handle(CommandId.NextColumnSet, NextColumnSet);
        shell.Handle(CommandId.SelectAll, SelectAll);
        shell.Handle(CommandId.SelectAllButFirst, SelectAllButFirst);
        shell.Handle(CommandId.UnselectAll, UnselectAll);
        shell.Handle(CommandId.Invert, Invert);
    }

    private void Run(Func<Task> work) => _ = Guard(work);

    private async Task Guard(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (TryEnter())
            {
                try
                {
                    Fail(_active, OperationError.FromException(ex, TabOf(_active)?.Location.Path));
                }
                finally
                {
                    _nav.Release();
                }
            }
        }
    }
}
