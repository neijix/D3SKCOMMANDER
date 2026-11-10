using D3skCommander.App.Features.Settings;
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
        var capture = _panels.Capture();
        await DeleteAsync(command, capture?.ActiveDirectory, capture?.Sources ?? []).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<bool> DeletePathsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var folder = paths.Count == 0 ? null : Path.GetDirectoryName(paths[0]);
        return DeleteAsync(CommandId.Delete, folder, paths);
    }

    private async Task<bool> DeleteAsync(CommandId command, string? folder, IReadOnlyList<string> sources)
    {
        if (!Begin())
        {
            return false;
        }

        try
        {
            var title = _text.Get(command == CommandId.DeletePermanent ? "cmd.delete_perm" : "cmd.delete");
            if (folder is not null && await Refused(GuardedOp.Delete, folder, null, title).ConfigureAwait(false))
            {
                return false;
            }

            if (sources.Count == 0)
            {
                await None(title).ConfigureAwait(false);
                return false;
            }

            var preferences = _settings.Preferences.Current;
            var decision = DeletePolicy.Decide(new DeleteSettings(preferences.UseTrash, preferences.ConfirmDelete), command);
            if (decision.NeedsConfirmation
                && !await _dialogs.ConfirmAsync(title, Count(_text.Get(decision.PromptKey), sources.Count))
                    .ConfigureAwait(false))
            {
                return false;
            }

            // A video playing in the preview keeps its file open: stop it before the file goes.
            foreach (var holder in _holders)
            {
                await holder.LetGoAsync(sources).ConfigureAwait(false);
            }

            if (decision.GoesToTrash)
            {
                await TrashAsync(title, sources).ConfigureAwait(false);
                return true;
            }

            await Finish(_files.Delete(_provider, sources)).ConfigureAwait(false);
            return true;
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

        if (_panels.Capture() is { } at && await Refused(GuardedOp.Rename, at.ActiveDirectory, null, _text.Get("cmd.rename")).ConfigureAwait(false))
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
        if (_panels is not IInPlaceRename rename)
        {
            return;
        }

        if (_panels.Capture() is { } at && FileOpGuard.IsRefused(GuardedOp.Rename, at.ActiveDirectory))
        {
            var shown = Refused(GuardedOp.Rename, at.ActiveDirectory, null, _text.Get("cmd.rename"));
            _ = shown.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            return;
        }

        rename.Begin();
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

        if (await ConfirmPermanentFallback(title, unavailable.Count).ConfigureAwait(false))
        {
            await Finish(_files.Delete(_provider, unavailable)).ConfigureAwait(false);
        }
    }

    /// <summary>The trash is unavailable: Delete, "Delete, don't ask again" (saved), or Cancel.</summary>
    private async Task<bool> ConfirmPermanentFallback(string title, int count)
    {
        var preferences = _settings.Preferences.Current;
        var offer = DeletePolicy.OfferPermanent(new DeleteSettings(ConfirmTrashFallback: preferences.ConfirmTrashFallback));
        if (!offer.NeedsConfirmation)
        {
            return true;
        }

        var message = _text.Get(TrashFallback.MessageKey) + "\n" + Count(_text.Get(offer.PromptKey), count);
        var choices = TrashFallback.ChoiceKeys.Select(key => _text.Get(key)).ToArray();
        var answer = TrashFallback.Map(await _dialogs.ChooseAsync(title, message, choices).ConfigureAwait(false));
        if (answer == TrashFallbackAnswer.DeleteAndStopAsking)
        {
            var next = PreferencesCopy.Of(_settings.Preferences.Current);
            next.ConfirmTrashFallback = false;
            _settings.Preferences.Save(next);
        }

        return answer != TrashFallbackAnswer.Cancel;
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
