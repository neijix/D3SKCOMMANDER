using D3skCommander.Core.Launch;

namespace D3skCommander.App.Features.Settings;

/// <summary>One line of the user-commands list.</summary>
public sealed class UserCommandRow
{
    public UserCommandRow(UserCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Command = command;
    }

    public UserCommand Command { get; }

    public string Display => Command.Name + " — " + Command.Command;
}
