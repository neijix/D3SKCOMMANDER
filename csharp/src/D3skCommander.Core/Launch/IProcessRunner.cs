namespace D3skCommander.Core.Launch;

/// <summary>Starts a process and returns. The caller does not wait. Failures throw.</summary>
public interface IProcessRunner
{
    void Start(ProcessRequest request);
}
