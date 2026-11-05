using System.Globalization;
using System.Text;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Checksums;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Splitter;
using D3skCommander.Core.Timestamps;
using D3skCommander.Core.Tools;

namespace D3skCommander.App.Features.Tools;

/// <summary>The Tools menu. Each command is a dialog; the file work is a job.</summary>
public sealed partial class ToolsCommands
{
    private readonly IPanelWorkspace _panels;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IJobService _jobs;
    private readonly SplitterService _splitter;
    private readonly ChecksumService _checksums;
    private int _busy;

    public ToolsCommands(
        IPanelWorkspace panels,
        IDialogService dialogs,
        ILocalizer text,
        IJobService jobs,
        SplitterService splitter,
        ChecksumService checksums)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(splitter);
        ArgumentNullException.ThrowIfNull(checksums);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _jobs = jobs;
        _splitter = splitter;
        _checksums = checksums;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.Split, () => _ = Run(SplitCore));
        shell.Handle(CommandId.Combine, () => _ = Run(CombineCore));
        shell.Handle(CommandId.ChecksumCreate, () => _ = Run(CreateCore));
        shell.Handle(CommandId.ChecksumVerify, () => _ = Run(VerifyCore));
        shell.Handle(CommandId.AddTime, () => _ = Run(AddTimeCore));
        shell.Handle(CommandId.ReDate, () => _ = Run(ReDateCore));
        shell.Handle(CommandId.Touch, () => _ = Run(TouchCore));
    }

    public Task SplitAsync() => Run(SplitCore);

    public Task CombineAsync() => Run(CombineCore);

    public Task ChecksumCreateAsync() => Run(CreateCore);

    public Task ChecksumVerifyAsync() => Run(VerifyCore);

    public Task AddTimeAsync() => Run(AddTimeCore);

    public Task ReDateAsync() => Run(ReDateCore);

    public Task TouchAsync() => Run(TouchCore);

    private async Task Run(Func<Task> work)
    {
        if (!Begin())
        {
            return;
        }

        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            End();
        }
    }

    private bool TryFiles(out PanelCapture? capture, out List<string> files)
    {
        capture = _panels.Capture();
        files = [];
        if (capture is null)
        {
            return false;
        }

        foreach (var path in capture.Sources)
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        return true;
    }

    private bool TryExpand(out PanelCapture? capture, out List<string> files)
    {
        capture = _panels.Capture();
        if (capture is null)
        {
            files = [];
            return false;
        }

        files = ToolInput.ExpandFiles(capture.Sources);
        return true;
    }

    private string FilesLine(IReadOnlyList<string> files)
    {
        if (files.Count == 1)
        {
            return files[0];
        }

        return files.Count.ToString(CultureInfo.InvariantCulture) + " " + _text.Get("files");
    }

    private Task None(string title) => _dialogs.ShowMessageAsync(title, _text.Get("dlg.no_selection"));

    private Task Fail(string message) => _dialogs.ShowMessageAsync(_text.Get("error"), message);

    private async Task FinishJob(IJob job)
    {
        await job.Completion.ConfigureAwait(false);
        await _panels.RefreshAsync().ConfigureAwait(false);
        await ShowFailures(job).ConfigureAwait(false);
    }

    private async Task ShowFailures(IJob job)
    {
        var result = await job.Completion.ConfigureAwait(false);
        if (result.Outcome == JobOutcome.Cancelled || result.Failed.Count == 0)
        {
            return;
        }

        await Fail(Join(result.Failed)).ConfigureAwait(false);
    }

    private static string Join(IReadOnlyList<ItemReport> failed)
    {
        var text = new StringBuilder();
        foreach (var item in failed)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(item.Error.Message);
        }

        return text.ToString();
    }

    private static string ErrorText(IReadOnlyList<FileTimeResult> results)
    {
        var text = new StringBuilder();
        foreach (var result in results)
        {
            if (result.Error is null)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(result.Error.Message);
        }

        return text.ToString();
    }

    private bool Begin() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    private void End() => Interlocked.Exchange(ref _busy, 0);
}
