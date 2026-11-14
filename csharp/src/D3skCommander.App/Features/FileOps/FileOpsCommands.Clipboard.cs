using D3skCommander.App.Features.Panels;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Panels;

namespace D3skCommander.App.Features.FileOps;

/// <summary>Ctrl+C / Ctrl+X / Ctrl+V on panel files (Core's <see cref="FileClipboard"/> plans the paste).</summary>
public sealed partial class FileOpsCommands
{
    /// <summary>The paths copied or cut, shared by both panels.</summary>
    public FileClipboard Clipboard { get; } = new();

    public async Task ClipAsync(bool cut)
    {
        var capture = await _panels.CaptureAsync().ConfigureAwait(false);
        if (cut && capture is not null && await Refused(GuardedOp.Cut, capture.ActiveDirectory, null, _text.Get("cmd.clip_cut")).ConfigureAwait(false))
        {
            return;
        }

        if (capture is null || capture.Sources.Count == 0)
        {
            await None(_text.Get(cut ? "cmd.clip_cut" : "cmd.clip_copy")).ConfigureAwait(false);
            return;
        }

        Clipboard.Set(_panels.ContextTarget is { } target ? ContextTargetPaths.Of(target) : capture.Sources, cut);
    }

    /// <summary>Copies or moves the clipboard into the active folder; a copy into its own folder gets a numbered name.</summary>
    public async Task PasteAsync()
    {
        if (Clipboard.IsEmpty || !Begin())
        {
            return;
        }

        // "Paste into" on a folder row goes into that folder; empty space pastes into the panel folder.
        var into = _panels.ContextTarget is { Kind: ContextTargetKind.Folder, EntryPath: { } folder } ? folder : null;
        try
        {
            var capture = await _panels.CaptureAsync().ConfigureAwait(false);
            if (capture is null)
            {
                return;
            }

            var directory = into ?? capture.ActiveDirectory;
            if (await Refused(GuardedOp.Paste, capture.ActiveDirectory, into, _text.Get("cmd.clip_paste")).ConfigureAwait(false))
            {
                return;
            }

            var plan = await Task.Run(() => Clipboard.Plan(directory, path => File.Exists(path) || Directory.Exists(path), Directory.Exists))
                .ConfigureAwait(false);
            if (plan.IsEmpty)
            {
                return;
            }

            if (plan.Into.Count > 0)
            {
                var kind = plan.Move ? FileOpKind.Move : FileOpKind.Copy;
                await Finish(_files.TransferToTarget(kind, _provider, plan.Into, _provider, directory, true, AskConflict)).ConfigureAwait(false);
            }

            foreach (var copy in plan.Duplicates)
            {
                await Finish(_files.TransferToTarget(FileOpKind.Copy, _provider, [copy.Source], _provider, copy.Target, false, AskConflict))
                    .ConfigureAwait(false);
            }

            Clipboard.Pasted();
        }
        finally
        {
            End();
        }
    }
}
