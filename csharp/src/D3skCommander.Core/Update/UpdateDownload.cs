using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Update;

/// <summary>A download queued on the job service. <see cref="Path"/> is set when the checksum matches.</summary>
public sealed class UpdateDownload
{
    private readonly DownloadSession _session;

    internal UpdateDownload(IJob job, DownloadSession session)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(session);
        Job = job;
        _session = session;
    }

    public IJob Job { get; }

    public string? Path => _session.Path;
}

internal sealed class DownloadSession
{
    public string? Path { get; set; }
}
