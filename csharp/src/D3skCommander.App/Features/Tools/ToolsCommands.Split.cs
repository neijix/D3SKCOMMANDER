using D3skCommander.App.Dialogs;
using D3skCommander.Core.Splitter;

namespace D3skCommander.App.Features.Tools;

public sealed partial class ToolsCommands
{
    private async Task SplitCore()
    {
        var title = _text.Get("cmd.split");
        if (!TryFiles(out var capture, out var files))
        {
            return;
        }

        if (files.Count == 0 || capture is null)
        {
            await None(title).ConfigureAwait(false);
            return;
        }

        var size = new TextDialogField(_text.Get("dlg.part_size"), "100M");
        var dest = new FolderDialogField(_text.Get("target"), capture.OtherDirectory);
        var fields = new DialogField[] { new NoteDialogField(files[0]), dest, size };
        var form = new DialogForm(title, fields, SplitReady);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(false))
        {
            return;
        }

        var folder = dest.Path.Trim();
        var parsed = FileSplitter.ParseSize(size.Text);
        if (folder.Length == 0 || !parsed.IsSuccess || parsed.Bytes == 0)
        {
            await RejectSize(folder, parsed).ConfigureAwait(false);
            return;
        }

        var operation = _splitter.Split(files[0], folder, parsed.Bytes);
        await FinishJob(operation.Job).ConfigureAwait(false);
    }

    private async Task CombineCore()
    {
        var title = _text.Get("cmd.combine");
        if (!TryFiles(out var capture, out var files))
        {
            return;
        }

        if (files.Count == 0 || capture is null)
        {
            await None(title).ConfigureAwait(false);
            return;
        }

        var dest = new FolderDialogField(_text.Get("target"), capture.OtherDirectory);
        var form = new DialogForm(title, new DialogField[] { dest }, FolderReady);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(false))
        {
            return;
        }

        var folder = dest.Path.Trim();
        if (folder.Length == 0)
        {
            return;
        }

        var operation = _splitter.Combine(files[0], folder);
        await FinishJob(operation.Job).ConfigureAwait(false);
    }

    private Task RejectSize(string folder, ParsedSize parsed)
    {
        if (folder.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (parsed.Error is not null)
        {
            return Fail(parsed.Error.Message);
        }

        return Fail(_text.Get("dlg.part_size"));
    }

    private static bool SplitReady(DialogForm form)
    {
        var dest = form.Fields[1] as FolderDialogField;
        var size = form.Fields[2] as TextDialogField;
        if (dest is null || size is null || dest.Path.Trim().Length == 0)
        {
            return false;
        }

        var parsed = FileSplitter.ParseSize(size.Text);
        return parsed.IsSuccess && parsed.Bytes > 0;
    }

    private static bool FolderReady(DialogForm form)
    {
        var dest = form.Fields[0] as FolderDialogField;
        return dest is not null && dest.Path.Trim().Length > 0;
    }
}
