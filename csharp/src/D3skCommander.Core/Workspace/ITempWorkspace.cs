namespace D3skCommander.Core.Workspace;

/// <summary>One temp folder for this session, removed on <see cref="Dispose"/>.</summary>
public interface ITempWorkspace : IDisposable
{
    /// <summary>Exists until <see cref="Dispose"/>. Callers put extracts and other scratch files here.</summary>
    string SessionFolder { get; }
}
