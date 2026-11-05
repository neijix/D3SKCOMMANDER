namespace D3skCommander.Core.Launch;

/// <summary>
/// A Tools-menu command from v0.1 <c>UserCommand</c>.
/// <see cref="Command"/> is a program plus arguments; <c>%s</c> is the selected files.
/// </summary>
public sealed class UserCommand
{
    public UserCommand(string name, string command, bool oncePerFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(command);
        Name = name;
        Command = command;
        OncePerFile = oncePerFile;
    }

    public string Name { get; }

    public string Command { get; }

    public bool OncePerFile { get; }

    public LaunchMode Mode => OncePerFile ? LaunchMode.OncePerFile : LaunchMode.AllAtOnce;
}
