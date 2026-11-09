using System.Globalization;
using System.Text;
using D3skCommander.App.Dialogs;
using D3skCommander.Core.Checksums;
using D3skCommander.Core.FileOps;

namespace D3skCommander.App.Features.Tools;

public sealed partial class ToolsCommands
{
    private async Task CreateCore()
    {
        var title = _text.Get("cmd.checksum_create");
        if (!TryExpand(out var capture, out var files) || capture is null)
        {
            return;
        }

        if (FileOpGuard.IsTargetRefused(capture.ActiveDirectory))
        {
            await _dialogs.ShowMessageAsync(title, _text.Get("computer.readonly")).ConfigureAwait(false);
            return;
        }

        if (files.Count == 0)
        {
            await None(title).ConfigureAwait(false);
            return;
        }

        var choice = AlgorithmField();
        var perFile = files.Count > 1 ? new CheckDialogField(_text.Get("tools.one_per_file")) : null;
        var output = new TextDialogField(
            _text.Get("dlg.out_file"),
            Checksum.OutputPath(files, capture.ActiveDirectory, ChecksumAlgo.Sha256, false));
        HookChecksum(files, capture.ActiveDirectory, choice, perFile, output);
        var form = new DialogForm(title, ChecksumFields(files, choice, perFile, output), _ => OutputReady(perFile, output));
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(false))
        {
            return;
        }

        var once = perFile is not null && perFile.IsChecked;
        var target = output.Text.Trim();
        if (!once && target.Length == 0)
        {
            return;
        }

        var operation = _checksums.Create(files, capture.ActiveDirectory, ChecksumAlgos.All[choice.SelectedIndex], once, target);
        await FinishJob(operation.Job).ConfigureAwait(false);
    }

    private async Task VerifyCore()
    {
        var title = _text.Get("cmd.checksum_verify");
        if (!TryFiles(out _, out var files))
        {
            return;
        }

        if (files.Count == 0)
        {
            await None(title).ConfigureAwait(false);
            return;
        }

        var lists = ChecksumLists(files);
        if (lists.Count == 0)
        {
            await _dialogs.ShowMessageAsync(title, _text.Get("tools.select_checksum")).ConfigureAwait(false);
            return;
        }

        var operation = _checksums.Verify(lists);
        await operation.Job.Completion.ConfigureAwait(false);
        if (operation.Job.Completion.Result.Failed.Count > 0)
        {
            await ShowFailures(operation.Job).ConfigureAwait(false);
            return;
        }

        await _dialogs.ShowMessageAsync(title, Summary(operation)).ConfigureAwait(false);
    }

    private ChoiceDialogField AlgorithmField()
    {
        var algos = ChecksumAlgos.All;
        var labels = new string[algos.Count];
        var selected = 0;
        for (var i = 0; i < algos.Count; i++)
        {
            labels[i] = algos[i].Label();
            if (algos[i] == ChecksumAlgo.Sha256)
            {
                selected = i;
            }
        }

        return new ChoiceDialogField(_text.Get("dlg.algo"), labels, selected);
    }

    private List<DialogField> ChecksumFields(
        IReadOnlyList<string> files,
        ChoiceDialogField choice,
        CheckDialogField? perFile,
        TextDialogField output)
    {
        var fields = new List<DialogField> { new NoteDialogField(FilesLine(files)), choice };
        if (perFile is not null)
        {
            fields.Add(perFile);
        }

        fields.Add(output);
        return fields;
    }

    private static void HookChecksum(
        IReadOnlyList<string> files,
        string baseDirectory,
        ChoiceDialogField choice,
        CheckDialogField? perFile,
        TextDialogField output)
    {
        void Sync()
        {
            var once = perFile is not null && perFile.IsChecked;
            output.Text = Checksum.OutputPath(files, baseDirectory, ChecksumAlgos.All[choice.SelectedIndex], once);
        }

        choice.ValueChanged += (_, _) => Sync();
        if (perFile is not null)
        {
            perFile.ValueChanged += (_, _) => Sync();
        }
    }

    private static bool OutputReady(CheckDialogField? perFile, TextDialogField output)
    {
        if (perFile is not null && perFile.IsChecked)
        {
            return true;
        }

        return output.Text.Trim().Length > 0;
    }

    private static List<string> ChecksumLists(IReadOnlyList<string> files)
    {
        var lists = new List<string>();
        foreach (var file in files)
        {
            if (ChecksumAlgos.FromExtension(Path.GetExtension(file)) is not null)
            {
                lists.Add(file);
            }
        }

        return lists;
    }

    private string Summary(ChecksumOperation operation)
    {
        var ok = 0;
        var bad = new StringBuilder();
        foreach (var verdict in operation.Verdicts)
        {
            var line = VerdictLine(verdict);
            if (line is null)
            {
                ok++;
                continue;
            }

            if (bad.Length > 0)
            {
                bad.Append('\n');
            }

            bad.Append(line);
        }

        var summary = ok.ToString(CultureInfo.InvariantCulture) + " " + _text.Get("ok")
            + ", " + (operation.Verdicts.Count - ok).ToString(CultureInfo.InvariantCulture)
            + " " + _text.Get("tools.problems");
        if (bad.Length == 0)
        {
            return summary;
        }

        return summary + "\n\n" + bad;
    }

    private string? VerdictLine(ChecksumVerdict verdict)
    {
        if (verdict.Kind == ChecksumVerdictKind.Ok)
        {
            return null;
        }

        if (verdict.Kind == ChecksumVerdictKind.Mismatch)
        {
            return "✗ " + verdict.RelativePath + ": " + _text.Get("tools.differs");
        }

        if (verdict.Kind == ChecksumVerdictKind.Missing)
        {
            return "? " + verdict.RelativePath + ": " + _text.Get("tools.missing");
        }

        return "! " + verdict.RelativePath + ": " + (verdict.Detail ?? "");
    }
}
