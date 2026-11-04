using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
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
        shell.Handle(CommandId.NewTab, () => Run(() => NewTabAsync()));
        shell.Handle(CommandId.CloseTab, CloseTab);
        shell.Handle(CommandId.NextTab, NextTab);
        shell.Handle(CommandId.Swap, () => Run(() => SwapAsync()));
        shell.Handle(CommandId.SameDir, () => Run(() => SameDirectoryAsync()));
        shell.Handle(CommandId.ViewFull, () => UseView(FileListView.Full));
        shell.Handle(CommandId.ViewBrief, () => UseView(FileListView.Brief));
        shell.Handle(CommandId.ToggleHidden, () => Run(() => ToggleHiddenAsync()));
        shell.Handle(CommandId.SortName, () => Sort(SortKey.Name));
        shell.Handle(CommandId.SortExt, () => Sort(SortKey.Extension));
        shell.Handle(CommandId.SortDate, () => Sort(SortKey.Modified));
        shell.Handle(CommandId.SortSize, () => Sort(SortKey.Size));
        shell.Handle(CommandId.SelectAll, SelectAll);
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
                    Fail(_active, ex.Message);
                }
                finally
                {
                    _nav.Release();
                }
            }
        }
    }
}
