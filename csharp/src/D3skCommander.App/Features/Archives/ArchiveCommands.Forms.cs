using D3skCommander.App.Dialogs;
using D3skCommander.Core.Archives;

namespace D3skCommander.App.Features.Archives;

public sealed partial class ArchiveCommands
{
    private const int DefaultLevel = 6;

    private async Task PackCore(CancellationToken cancellationToken)
    {
        var title = _text.Get("cmd.pack");
        var capture = _panels.Capture();
        if (capture is null)
        {
            return;
        }

        var sources = Existing(capture.Sources);
        if (sources.Count == 0)
        {
            await None(title).ConfigureAwait(false);
            return;
        }

        var kinds = ArchiveKinds.Creatable();
        var labels = new string[kinds.Length];
        for (var i = 0; i < kinds.Length; i++)
        {
            labels[i] = ArchiveKinds.Label(kinds[i]);
        }

        var note = _text.Get("dlg.pack").Replace("{n}", sources.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var format = new ChoiceDialogField(_text.Get("dlg.format"), labels);
        var level = new ChoiceDialogField(_text.Get("dlg.level"), Levels(), DefaultLevel);
        var folder = new FolderDialogField(_text.Get("target"), capture.OtherDirectory);
        var baseName = BackupZip.BaseName(sources, capture.ActiveDirectory);
        var name = new TextDialogField(
            _text.Get("archives.archive_name"),
            PackRules.WithArchiveExtension(baseName, kinds[0]));
        var fields = new DialogField[] { new NoteDialogField(note), format, level, folder, name };
        var form = new DialogForm(title, fields, PackReady);
        var shown = format.SelectedIndex;
        form.Changed += (_, _) =>
        {
            if (format.SelectedIndex == shown || format.SelectedIndex < 0)
            {
                return;
            }

            shown = format.SelectedIndex;
            name.Text = PackRules.WithArchiveExtension(name.Text, kinds[shown]);
        };
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(false))
        {
            return;
        }

        var archive = Path.Combine(folder.Path.Trim(), name.Text.Trim());
        var job = _archives.Pack(archive, kinds[format.SelectedIndex], sources, level.SelectedIndex, cancellationToken);
        await FinishJob(job.Job).ConfigureAwait(false);
    }

    private async Task UnpackCore(CancellationToken cancellationToken)
    {
        var title = _text.Get("cmd.unpack");
        var capture = _panels.Capture();
        if (capture is null)
        {
            return;
        }

        var archives = new List<string>();
        foreach (var path in capture.Sources)
        {
            if (File.Exists(path) && ArchiveKinds.FromPath(path) is not null)
            {
                archives.Add(path);
            }
        }

        if (archives.Count == 0)
        {
            var anything = capture.Sources.Count > 0;
            await _dialogs.ShowMessageAsync(
                title,
                _text.Get(anything ? "archives.not_archive" : "dlg.no_selection")).ConfigureAwait(false);
            return;
        }

        var dest = new FolderDialogField(_text.Get("dlg.unpack"), capture.OtherDirectory);
        var fields = new DialogField[] { new NoteDialogField(archives[0]), dest };
        var form = new DialogForm(title, fields, UnpackReady);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(false))
        {
            return;
        }

        foreach (var archive in archives)
        {
            var job = _archives.Extract(archive, null, dest.Path.Trim(), cancellationToken);
            await FinishJob(job.Job).ConfigureAwait(false);
        }
    }

    private static string[] Levels()
    {
        var levels = new string[10];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return levels;
    }

    private static bool PackReady(DialogForm form)
    {
        var folder = form.Fields[3] as FolderDialogField;
        var name = form.Fields[4] as TextDialogField;
        return folder is not null && name is not null
            && folder.Path.Trim().Length > 0 && name.Text.Trim().Length > 0;
    }

    private static bool UnpackReady(DialogForm form)
    {
        var dest = form.Fields[1] as FolderDialogField;
        return dest is not null && dest.Path.Trim().Length > 0;
    }
}
