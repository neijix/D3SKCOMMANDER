using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

/// <summary>One menu the shell draws. A null command with <see cref="ShellMenuEntry.IsSeparator"/> is a rule.</summary>
public sealed class ShellMenu
{
    public ShellMenu(string header, IReadOnlyList<ShellMenuEntry> items)
    {
        ArgumentException.ThrowIfNullOrEmpty(header);
        ArgumentNullException.ThrowIfNull(items);
        Header = header;
        Items = items;
    }

    public string Header { get; }

    public IReadOnlyList<ShellMenuEntry> Items { get; }
}

public sealed class ShellMenuEntry
{
    public bool IsSeparator { get; init; }

    public string Header { get; init; } = "";

    public bool IsEnabled { get; init; }

    public bool IsVisible { get; init; } = true;

    public Gesture? Gesture { get; init; }

    public CommandId? Command { get; init; }
}
