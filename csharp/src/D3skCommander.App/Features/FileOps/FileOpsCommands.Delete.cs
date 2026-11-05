using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Trash;

namespace D3skCommander.App.Features.FileOps;

public sealed partial class FileOpsCommands
{
    /// <summary>Delete follows the preferences; DeletePermanent always removes the files.</summary>
    public async Task DeleteAsync(CommandId command = CommandId.Delete)
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            var title = _text.Get(command == CommandId.DeletePermanent ? "cmd.delete_perm" : "cmd.delete");
            var capture = _panels.Capture();
            if (capture is null || capture.Sources.Count == 0)
            {
                await None(title).ConfigureAwait(false);
                return;
            }

            var preferences = _settings.Preferences.Current;
            var decision = DeletePolicy.Decide(new DeleteSettings(preferences.UseTrash, preferences.ConfirmDelete), command);
            if (decision.NeedsConfirmation
                && !await _dialogs.ConfirmAsync(title, Count(_text.Get(decision.PromptKey), capture.Sources.Count))
                    .ConfigureAwait(false))
            {
                return;
            }

            if (decision.GoesToTrash)
            {
                await TrashAsync(title, capture.Sources).ConfigureAwait(false);
                return;
            }

            await Finish(_files.Delete(_provider, capture.Sources)).ConfigureAwait(false);
        }
        finally
        {
            End();
        }
    }

    /// <summary>Renames <paramref name="path"/> inside its folder; a typed failure is shown, never thrown.</summary>
    public async Task RenameAsync(string path, string newName)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(newName);
        var name = newName.Trim();
        if (name.Length == 0 || string.Equals(name, Path.GetFileName(path), StringComparison.Ordinal))
        {
            return;
        }

        var renamed = await _provider.RenameAsync(path, name, CancellationToken.None).ConfigureAwait(false);
        if (!renamed.IsSuccess)
        {
            var error = renamed.Error ?? OperationError.NotFound(path);
            await _dialogs.ShowMessageAsync(_text.Get("fileops.rename_failed"), ErrorLines.Describe(_text, error, path))
                .ConfigureAwait(false);
        }

        await _panels.RefreshAsync().ConfigureAwait(false);
    }

    private void BeginRename()
    {
        if (_panels is IInPlaceRename rename)
        {
            rename.Begin();
        }
    }

    private async Task TrashAsync(string title, IReadOnlyList<string> sources)
    {
        var items = new JobItem[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            items[i] = new JobItem(sources[i]);
        }

        var job = _jobs.Start(
            title,
            items,
            (item, _, token) => Task.Run(() => TrashOne(item.Name), token));
        var result = await job.Completion.ConfigureAwait(false);
        var unavailable = new List<string>();
        var lines = new List<string>();
        foreach (var failed in result.Failed)
        {
            if (failed.Error.Message == OsTrash.UnavailableMessage)
            {
                unavailable.Add(failed.Item);
            }
            else
            {
                lines.Add(ErrorLines.Describe(_text, failed.Error, failed.Item));
            }
        }

        await Finish(job, () => lines.Count, () => lines).ConfigureAwait(false);
        if (unavailable.Count == 0)
        {
            return;
        }

        var offer = DeletePolicy.OfferPermanent(new DeleteSettings());
        var message = _text.Get("fileops.trash_failed") + "\n" + Count(_text.Get(offer.PromptKey), unavailable.Count);
        if (await _dialogs.ConfirmAsync(title, message).ConfigureAwait(false))
        {
            await Finish(_files.Delete(_provider, unavailable)).ConfigureAwait(false);
        }
    }

    private ItemResult TrashOne(string path)
    {
        var status = _trash.MoveToTrash(path);
        if (status.IsSuccess)
        {
            return ItemResult.Success;
        }

        var error = status.Error ?? OperationError.NotFound(path);
        return ItemResult.Failed(error, !status.OfferPermanentDelete);
    }
}
