using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.Drives;

public sealed partial class DrivesCommands
{
    /// <summary>Alt+Enter: facts about the entry under the cursor, or the totals of the selection.</summary>
    public async Task PropertiesAsync()
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            var title = _text.Get("cmd.properties");
            var capture = _panels.Capture();
            var sources = _panels.ContextTarget is { } target ? ContextTargetPaths.Of(target) : capture?.Sources;
            if (capture is null || sources is null || sources.Count == 0)
            {
                await _dialogs.ShowMessageAsync(title, _text.Get("props.none")).ConfigureAwait(false);
                return;
            }

            var lines = await Describe(title, sources, capture.ActiveDirectory).ConfigureAwait(false);
            if (lines is not null)
            {
                var notes = lines.Select(line => (DialogField)new NoteDialogField(line)).ToList();
                await _dialogs.ShowFormAsync(new DialogForm(title, notes)).ConfigureAwait(false);
            }
        }
        finally
        {
            End();
        }
    }

    // Reading and measuring run as a job, so a big folder never blocks the window and can be cancelled from the jobs list.
    private async Task<IReadOnlyList<string>?> Describe(string title, IReadOnlyList<string> sources, string directory)
    {
        IReadOnlyList<string>? lines = null;
        var cancelled = false;
        var job = _jobs.Start(
            _text.Get("props.job") + " " + title,
            (_, cancel) =>
            {
                lines = Build(sources, directory, cancel);
                cancelled = cancel.IsCancellationRequested;
                return Task.CompletedTask;
            });
        await job.Completion.ConfigureAwait(false);
        if (cancelled || lines is null)
        {
            return null;
        }

        return lines;
    }

    private List<string> Build(IReadOnlyList<string> sources, string directory, CancellationToken cancel)
    {
        if (sources.Count > 1)
        {
            var total = EntryProperties.Measure(sources, cancel);
            return [.. EntryPropertiesText.ForSelection(_text, sources.Count, directory, total)];
        }

        var read = EntryProperties.Read(sources[0]);
        if (!read.IsSuccess)
        {
            return [_text.Get("error") + ": " + sources[0]];
        }

        var props = read.Value;
        SelectionSize? inside = null;
        if (props.Kind == EntryKind.Folder)
        {
            var tree = LocalFileSystem.DirectorySize(props.FullPath, cancel);
            inside = new SelectionSize(tree.Bytes, tree.Files, tree.Directories);
        }

        return [.. EntryPropertiesText.ForEntry(_text, props, inside)];
    }
}
