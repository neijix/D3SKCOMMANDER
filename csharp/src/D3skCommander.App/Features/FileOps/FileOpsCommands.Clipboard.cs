using D3skCommander.Core.FileOps;

namespace D3skCommander.App.Features.FileOps;

/// <summary>Ctrl+C / Ctrl+X / Ctrl+V on panel files (Core's <see cref="FileClipboard"/> plans the paste).</summary>
public sealed partial class FileOpsCommands
{
    /// <summary>The paths copied or cut, shared by both panels.</summary>
    public FileClipboard Clipboard { get; } = new();

    public async Task ClipAsync(bool cut)
    {
        var capture = _panels.Capture();
        if (capture is null || capture.Sources.Count == 0)
        {
            await None(_text.Get(cut ? "cmd.clip_cut" : "cmd.clip_copy")).ConfigureAwait(false);
            return;
        }

        Clipboard.Set(capture.Sources, cut);
    }

    /// <summary>Copies or moves the clipboard into the active folder; a copy into its own folder gets a numbered name.</summary>
    public async Task PasteAsync()
    {
        if (Clipboard.IsEmpty || !Begin())
        {
            return;
        }

        try
        {
            var capture = _panels.Capture();
            if (capture is null)
            {
                return;
            }

            var directory = capture.ActiveDirectory;
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
