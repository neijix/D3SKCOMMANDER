using System.Globalization;
using D3skCommander.App.Dialogs;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Timestamps;
using D3skCommander.Core.Tools;

namespace D3skCommander.App.Features.Tools;

public sealed partial class ToolsCommands
{
    private Task AddTimeCore() => TimeCore(CommandId.AddTime);

    private Task ReDateCore() => TimeCore(CommandId.ReDate);

    private Task TouchCore() => TimeCore(CommandId.Touch);

    private async Task TimeCore(CommandId command)
    {
        var title = _text.Get(TitleKey(command));
        if (!TryExpand(out _, out var files))
        {
            return;
        }

        if (files.Count == 0)
        {
            await None(title).ConfigureAwait(false);
            return;
        }

        var value = new TextDialogField(_text.Get(ValueKey(command)), DefaultValue(command));
        var step = command == CommandId.ReDate ? new TextDialogField(_text.Get("dlg.step"), "60s") : null;
        var modified = command == CommandId.ReDate ? null : new CheckDialogField(_text.Get("dlg.modified"), true);
        var accessed = command == CommandId.ReDate ? null : new CheckDialogField(_text.Get("dlg.accessed"));
        var form = new DialogForm(title, TimeFields(files, value, step, modified, accessed), ValueReady);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(false))
        {
            return;
        }

        var entered = value.Text;
        var stepText = step is null ? "" : step.Text;
        var reason = RejectReason(command, entered, stepText);
        if (reason is not null)
        {
            await Fail(reason).ConfigureAwait(false);
            return;
        }

        var changeModified = modified is not null && modified.IsChecked;
        var changeAccessed = accessed is not null && accessed.IsChecked;
        var job = _jobs.Start(title, (progress, token) =>
            Apply(command, files, entered, stepText, changeModified, changeAccessed, progress, token));
        await FinishJob(job).ConfigureAwait(false);
    }

    private List<DialogField> TimeFields(
        IReadOnlyList<string> files,
        TextDialogField value,
        TextDialogField? step,
        CheckDialogField? modified,
        CheckDialogField? accessed)
    {
        var fields = new List<DialogField> { new NoteDialogField(FilesLine(files)), value };
        if (step is not null)
        {
            fields.Add(step);
        }

        if (modified is not null)
        {
            fields.Add(modified);
        }

        if (accessed is not null)
        {
            fields.Add(accessed);
        }

        return fields;
    }

    private static Task Apply(
        CommandId command,
        IReadOnlyList<string> files,
        string entered,
        string stepText,
        bool modified,
        bool accessed,
        IJobProgress progress,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        progress.Report(new JobProgress(0, files.Count, 0, files.Count, files[0]));
        var results = Change(command, files, entered, stepText, modified, accessed);
        progress.Report(new JobProgress(files.Count, files.Count, files.Count, files.Count, files[files.Count - 1]));
        var errors = ErrorText(results);
        if (errors.Length > 0)
        {
            throw new InvalidDataException(errors);
        }

        return Task.CompletedTask;
    }

    private static List<FileTimeResult> Change(
        CommandId command,
        IReadOnlyList<string> files,
        string entered,
        string stepText,
        bool modified,
        bool accessed)
    {
        if (command == CommandId.AddTime)
        {
            var offset = FileTimes.ParseOffset(entered);
            if (!offset.IsSuccess)
            {
                throw new InvalidDataException(offset.Error!.Message);
            }

            return FileTimes.ShiftTimes(files, offset.Seconds, modified, accessed);
        }

        var when = ToolInput.ParseDate(entered);
        if (!when.IsSuccess)
        {
            throw new InvalidDataException(when.Error!.Message);
        }

        if (command == CommandId.ReDate)
        {
            var step = FileTimes.ParseOffset(stepText);
            if (!step.IsSuccess)
            {
                throw new InvalidDataException(step.Error!.Message);
            }

            return FileTimes.ReDate(files, when.Time, step.Seconds);
        }

        DateTimeOffset? write = modified ? when.Time : null;
        DateTimeOffset? read = accessed ? when.Time : null;
        return FileTimes.SetTimes(files, write, read);
    }

    private static string? RejectReason(CommandId command, string entered, string stepText)
    {
        if (command == CommandId.AddTime)
        {
            var offset = FileTimes.ParseOffset(entered);
            return offset.IsSuccess ? null : offset.Error!.Message;
        }

        var when = ToolInput.ParseDate(entered);
        if (!when.IsSuccess)
        {
            return when.Error!.Message;
        }

        if (command != CommandId.ReDate)
        {
            return null;
        }

        var step = FileTimes.ParseOffset(stepText);
        return step.IsSuccess ? null : step.Error!.Message;
    }

    private static bool ValueReady(DialogForm form)
    {
        var value = form.Fields[1] as TextDialogField;
        return value is not null && value.Text.Trim().Length > 0;
    }

    private static string TitleKey(CommandId command)
    {
        if (command == CommandId.AddTime)
        {
            return "cmd.addtime";
        }

        if (command == CommandId.ReDate)
        {
            return "cmd.redate";
        }

        return "cmd.touch";
    }

    private static string ValueKey(CommandId command)
    {
        if (command == CommandId.AddTime)
        {
            return "dlg.offset";
        }

        if (command == CommandId.ReDate)
        {
            return "dlg.start_date";
        }

        return "date";
    }

    private static string DefaultValue(CommandId command)
    {
        if (command == CommandId.AddTime)
        {
            return "+1h";
        }

        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
