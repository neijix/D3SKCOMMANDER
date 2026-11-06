namespace D3skCommander.Core.Commands;

/// <summary>Command ids, labels, default gestures and the menu tree. The app binds them; it does not own them.</summary>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandId> All { get; } = Enum.GetValues<CommandId>();

    private static readonly Dictionary<string, CommandId> Names = BuildNames();

    static CommandCatalog()
    {
        if (CommandLabelKeys.All.Count != All.Count)
        {
            throw new InvalidOperationException("Each command needs one label key.");
        }
    }

    public static IReadOnlyList<CommandBinding> Bindings => CommandBindings.All;

    public static IReadOnlyList<MenuDefinition> Menus => MenuLayout.Menus;

    public static IReadOnlyList<FunctionKey> FunctionKeys => FunctionKeyList.All;

    /// <summary>The icon buttons in front of one panel's path box, left to right.</summary>
    public static IReadOnlyList<CommandId> PanelBar(bool leftPanel) => PanelBarLayout.For(leftPanel);

    public static CommandId? FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Names.TryGetValue(name.Trim(), out var command))
        {
            return command;
        }

        return null;
    }

    public static string LabelKey(CommandId command)
    {
        var index = (int)command;
        if (index < 0 || index >= CommandLabelKeys.All.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown command.");
        }

        return CommandLabelKeys.All[index];
    }

    /// <summary>First binding in precedence order. Menu shortcut text uses this one (v0.1 <c>shortcut_text</c>).</summary>
    public static Gesture? PrimaryGesture(CommandId command)
    {
        foreach (var binding in Bindings)
        {
            if (binding.Command == command)
            {
                return binding.Gesture;
            }
        }

        return null;
    }

    public static IReadOnlyList<Gesture> Gestures(CommandId command)
    {
        var found = new List<Gesture>();
        foreach (var binding in Bindings)
        {
            if (binding.Command == command)
            {
                found.Add(binding.Gesture);
            }
        }

        return found;
    }

    /// <summary>Most specific binding wins. Extra Shift or Alt does not hide a more specific chord.</summary>
    public static CommandId? Match(Gesture pressed)
    {
        foreach (var binding in Bindings)
        {
            if (binding.Gesture.Matches(pressed))
            {
                return binding.Command;
            }
        }

        return null;
    }

    private static Dictionary<string, CommandId> BuildNames()
    {
        var names = new Dictionary<string, CommandId>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in All)
        {
            names.Add(command.ToString(), command);
        }

        return names;
    }
}
