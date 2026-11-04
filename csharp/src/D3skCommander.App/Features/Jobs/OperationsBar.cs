using D3skCommander.App.Shell;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Features.Jobs;

/// <summary>The one operations line: current file, byte progress and Cancel.</summary>
public sealed class OperationsBar : IShellWidget
{
    private readonly IJobService _jobs;
    private readonly ILocalizer _text;

    public OperationsBar(IJobService jobs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(text);
        _jobs = jobs;
        _text = text;
    }

    public string? Name => "Operations";

    public void Draw()
    {
        var job = Running();
        if (job is null)
        {
            return;
        }

        var progress = job.Progress;
        var current = progress.CurrentName.Length == 0 ? job.Title : progress.CurrentName;
        ImGui.TextUnformatted(job.Title);
        ImGui.SameLine();
        ImGui.TextUnformatted(current);
        ImGui.SameLine();
        ImGui.TextUnformatted(progress.ItemsDone + "/" + progress.ItemsTotal);
        ImGui.SameLine();
        ImGui.TextUnformatted(Size(progress.BytesDone) + " / " + Size(progress.BytesTotal));
        ImGui.SameLine();
        if (ImGui.Button(_text.Get("cancel") + "##job-" + job.Id))
        {
            job.Cancel();
        }
    }

    private IJob? Running()
    {
        IJob? best = null;
        foreach (var job in _jobs.Jobs)
        {
            if (!job.IsRunning)
            {
                continue;
            }

            if (best is null || job.Id > best.Id)
            {
                best = job;
            }
        }

        return best;
    }

    private static string Size(long bytes)
    {
        if (bytes <= 0)
        {
            return LocalFileSystem.FormatSize(0);
        }

        return LocalFileSystem.FormatSize((ulong)bytes);
    }
}
