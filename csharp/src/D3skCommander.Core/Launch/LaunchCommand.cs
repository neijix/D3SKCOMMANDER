namespace D3skCommander.Core.Launch;

/// <summary>One program and its argv, after placeholder expansion. The program word is never substituted.</summary>
public sealed class LaunchCommand
{
    public LaunchCommand(string program, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(arguments);
        Program = program;
        Arguments = ArgumentsCopy.Of(arguments);
    }

    public string Program { get; }

    public IReadOnlyList<string> Arguments { get; }
}

internal static class ArgumentsCopy
{
    public static string[] Of(IReadOnlyList<string> arguments)
    {
        var copy = new string[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            ArgumentNullException.ThrowIfNull(argument);
            copy[i] = argument;
        }

        return copy;
    }
}
