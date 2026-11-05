using D3skCommander.Core.Update;

namespace D3skCommander.Core.Tests.Update;

internal sealed class RecordingProcess : IUpdateProcess
{
    private readonly int _code;

    public RecordingProcess(int code) => _code = code;

    public string? Program { get; private set; }

    public IReadOnlyList<string> Arguments { get; private set; } = [];

    public int Run(string program, IReadOnlyList<string> arguments)
    {
        Program = program;
        Arguments = arguments.ToArray();
        return _code;
    }
}
