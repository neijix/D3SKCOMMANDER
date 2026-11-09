using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.FileOps;

namespace D3skCommander.App.Features.Drives;

public sealed partial class DrivesCommands
{
    /// <summary>Copies files dropped from the OS into the folder shown by <paramref name="side"/>, as a job.</summary>
    public async Task DropAsync(PanelSide side, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var folder = _panels.Read(side).Path;
        if (FileOpGuard.IsTargetRefused(folder))
        {
            await _dialogs.ShowMessageAsync(_text.Get("cmd.copy"), _text.Get("computer.readonly")).ConfigureAwait(false);
            return;
        }

        var sources = await Task.Run(() => Existing(paths)).ConfigureAwait(false);
        if (sources.Count == 0 || !await Task.Run(() => Directory.Exists(folder)).ConfigureAwait(false))
        {
            return;
        }

        var operation = _files.Copy(_provider, sources, _provider, folder, null, AskConflict);
        await operation.Job.Completion.ConfigureAwait(false);
        await _panels.RefreshAsync().ConfigureAwait(false);
        if (operation.ErrorCount > 0)
        {
            var message = _text.Get("drop.failed") + "\n" + string.Join('\n', operation.ErrorMessages());
            await _dialogs.ShowMessageAsync(_text.Get("jobs.failed"), message).ConfigureAwait(false);
        }
    }

    private static List<string> Existing(IReadOnlyList<string> paths)
    {
        var found = new List<string>();
        foreach (var path in paths)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                found.Add(path);
            }
        }

        return found;
    }

    private async Task<ConflictChoice> AskConflict(ConflictQuestion question, CancellationToken cancellationToken)
    {
        var choice = await _dialogs.ChooseAsync(
            _text.Get("dlg.overwrite"),
            ConflictPrompt.Body(_text, question),
            ConflictPrompt.Choices(_text),
            cancellationToken).ConfigureAwait(false);
        return ConflictPrompt.Map(choice);
    }
}
