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

public sealed class ArchiveCase : IProviderCase
{
    public bool ReadOnly => true;

    public ProviderFixture Open() => new ArchiveProviderFixture();

    public override string ToString() => "Archive";
}

public sealed class ListCase : IProviderCase
{
    public bool ReadOnly => false;

    public ProviderFixture Open() => new ListProviderFixture();

    public override string ToString() => "List";
}

public sealed class PanelCase : IProviderCase
{
    public bool ReadOnly => false;

    public ProviderFixture Open() => new PanelProviderFixture();

    public override string ToString() => "Panel";
}
