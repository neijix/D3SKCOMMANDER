using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Jobs;

/// <summary>What the operations bar shows: the newest running job's texts. <see cref="Refresh"/> re-reads the jobs.</summary>
public sealed class OperationsBarModel : ObservableObject
{
    private readonly IJobService _jobs;
    private IJob? _job;
    private bool _isVisible;
    private string _title = "";
    private string _current = "";
    private string _items = "";
    private string _bytes = "";

    public OperationsBarModel(IJobService jobs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(text);
        _jobs = jobs;
        CancelText = text.Get("cancel");
    }

    public string CancelText { get; }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>The current name, or the job title when the job reports none.</summary>
    public string Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    public string Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public string Bytes
    {
        get => _bytes;
        private set => SetProperty(ref _bytes, value);
    }

    public void Refresh()
    {
        _job = Newest();
        if (_job is null)
        {
            IsVisible = false;
            return;
        }

        var progress = _job.Progress;
        Title = _job.Title;
        Current = progress.CurrentName.Length == 0 ? _job.Title : progress.CurrentName;
        Items = progress.ItemsDone + "/" + progress.ItemsTotal;
        Bytes = Size(progress.BytesDone) + " / " + Size(progress.BytesTotal);
        IsVisible = true;
    }

    public void Cancel() => _job?.Cancel();

    private IJob? Newest()
    {
        IJob? best = null;
        foreach (var job in _jobs.Jobs)
        {
            if (job.IsRunning && (best is null || job.Id > best.Id))
            {
                best = job;
            }
        }

        return best;
    }

    private static string Size(long bytes) => LocalFileSystem.FormatSize(bytes <= 0 ? 0 : (ulong)bytes);
}
