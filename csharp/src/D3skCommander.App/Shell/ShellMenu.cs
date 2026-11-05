using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

/// <summary>One menu the shell draws. A null command with <see cref="ShellMenuEntry.IsSeparator"/> is a rule.</summary>
public sealed class ShellMenu
{
    public ShellMenu(string header, IReadOnlyList<ShellMenuEntry> items, IReadOnlyList<Func<IReadOnlyList<ShellAction>>>? dynamicItems = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(header);
        ArgumentNullException.ThrowIfNull(items);
        Header = header;
        Items = items;
        DynamicItems = dynamicItems ?? [];
    }

    public string Header { get; }

    public IReadOnlyList<ShellMenuEntry> Items { get; }

    /// <summary>Items read again each time the menu opens (user commands from the settings), after <see cref="Items"/>.</summary>
    public IReadOnlyList<Func<IReadOnlyList<ShellAction>>> DynamicItems { get; }
}

/// <summary>A menu item that is not a catalog command: a text the user chose and what it runs.</summary>
public sealed record ShellAction(string Header, Action Invoke);

public sealed class ShellMenuEntry
{
    public bool IsSeparator { get; init; }

    public string Header { get; init; } = "";

    public bool IsEnabled { get; init; }

    public bool IsVisible { get; init; } = true;

    public Gesture? Gesture { get; init; }

    public CommandId? Command { get; init; }
}
