using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Tests.Providers;

/// <summary>A folder a provider can list, plus one sample file inside it.</summary>
public abstract class ProviderFixture : IDisposable
{
    public abstract IFileSystemProvider Provider { get; }

    public abstract string Directory { get; }

    public abstract IReadOnlyList<string> FileNames { get; }

    public abstract string SampleFileName { get; }

    public abstract string SampleFilePath { get; }

    public abstract byte[] SampleBytes { get; }

    public abstract string Combine(string directory, string name);

    public abstract void Dispose();
}
