namespace D3skCommander.Core.Tests.Providers;

/// <summary>Opens a fresh fixture. One case per provider; rows add theirs beside Local.</summary>
public interface IProviderCase
{
    bool ReadOnly { get; }

    ProviderFixture Open();
}

public sealed class LocalCase : IProviderCase
{
    public LocalCase(bool readOnly) => ReadOnly = readOnly;

    public bool ReadOnly { get; }

    public ProviderFixture Open() => new LocalProviderFixture(ReadOnly);

    public override string ToString() => ReadOnly ? "Local read-only" : "Local";
}
