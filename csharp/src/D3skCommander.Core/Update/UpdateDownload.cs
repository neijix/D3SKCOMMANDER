using D3sk.Update.Signature;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Update;

/// <summary>
/// A download queued on the job service. <see cref="Path"/> is set when the file is verified; a release the signature
/// check refused sets <see cref="Refusal"/> and <see cref="Reason"/> instead, and no file is left.
/// </summary>
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

    /// <summary>Why the signature check refused the release; null when it did not (or the download has not ended).</summary>
    public SumsRefusal? Refusal => _session.Refusal;

    /// <summary>A sentence for a person (English, no token) saying why no verified file came out.</summary>
    public string? Reason => _session.Reason;

    /// <summary>The localizer key of <see cref="Refusal"/>, or null when there is none.</summary>
    public string? RefusalKey => Refusal switch
    {
        SumsRefusal.NotSigned => "update.release_not_signed",
        SumsRefusal.SignatureInvalid => "update.signature_invalid",
        SumsRefusal.InvalidKey => "update.key_invalid",
        SumsRefusal.HashMismatch => "update.checksum_mismatch",
        _ => null,
    };
}

internal sealed class DownloadSession
{
    public string? Path { get; set; }

    public SumsRefusal? Refusal { get; set; }

    public string? Reason { get; set; }
}
