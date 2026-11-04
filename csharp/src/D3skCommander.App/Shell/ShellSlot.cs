namespace D3skCommander.App.Shell;

/// <summary>One named place in the shell. The slot exists even when no feature has filled it.</summary>
public sealed class ShellSlot
{
    public ShellSlot(string name) => Name = name;

    public string Name { get; }

    public IShellWidget? Content { get; internal set; }
}
