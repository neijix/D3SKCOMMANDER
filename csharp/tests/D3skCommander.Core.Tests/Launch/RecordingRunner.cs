using D3skCommander.Core.Launch;

namespace D3skCommander.Core.Tests.Launch;

/// <summary>Records requests. Programs in <see cref="FailPrograms"/> throw, as a missing executable would.</summary>
internal sealed class RecordingRunner : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public HashSet<string> FailPrograms { get; } = new(StringComparer.Ordinal);

    public bool FailAll { get; set; }

    public void Start(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        if (FailAll)
        {
            throw new IOException("not found");
        }

        if (request is ProcessRequest.Argv argv && FailPrograms.Contains(argv.Program))
        {
            throw new IOException(argv.Program + ": not found");
        }
    }
}
