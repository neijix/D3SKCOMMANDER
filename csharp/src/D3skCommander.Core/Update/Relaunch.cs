using D3skCommander.Core.Launch;

namespace D3skCommander.Core.Update;

/// <summary>The program to start once this process has closed.</summary>
public sealed class Relaunch : IEquatable<Relaunch>
{
    public Relaunch(string program, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(arguments);
        Program = program;
        Arguments = ArgumentsCopy.Of(arguments);
    }

    public string Program { get; }

    public IReadOnlyList<string> Arguments { get; }

    public static Relaunch Of(string program) => new(program, []);

    public bool Equals(Relaunch? other)
    {
        if (other is null || !string.Equals(Program, other.Program, StringComparison.Ordinal))
        {
            return false;
        }

        if (Arguments.Count != other.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < Arguments.Count; i++)
        {
            if (!string.Equals(Arguments[i], other.Arguments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as Relaunch);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Program, StringComparer.Ordinal);
        foreach (var argument in Arguments)
        {
            hash.Add(argument, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
