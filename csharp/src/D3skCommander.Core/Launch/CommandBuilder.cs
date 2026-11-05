using System.Text;

namespace D3skCommander.Core.Launch;

/// <summary>
/// v0.1 <c>split_command_line</c> and <c>build_commands</c>.
/// Words split on whitespace; double quotes group a word and are removed; <c>""</c> is an empty argument.
/// <c>%s</c> in an argument (never in the program word) is replaced by each file, and that argument
/// is repeated once per file. With no <c>%s</c>, the files are appended.
/// </summary>
public static class CommandBuilder
{
    public static IReadOnlyList<string> Split(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var args = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        var quoted = false;
        foreach (var c in command)
        {
            if (c == '"')
            {
                quoted = !quoted;
                inToken = true;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }

                continue;
            }

            current.Append(c);
            inToken = true;
        }

        if (inToken)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    public static IReadOnlyList<LaunchCommand> Build(string command, IReadOnlyList<string> files, LaunchMode mode)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(files);
        RequireMode(mode);
        var words = Split(command);
        if (words.Count == 0 || files.Count == 0)
        {
            return [];
        }

        var program = words[0];
        var templates = new List<string>(words.Count - 1);
        var hasPlaceholder = false;
        for (var i = 1; i < words.Count; i++)
        {
            templates.Add(words[i]);
            if (words[i].Contains("%s", StringComparison.Ordinal))
            {
                hasPlaceholder = true;
            }
        }

        var commands = new List<LaunchCommand>();
        if (mode == LaunchMode.OncePerFile)
        {
            foreach (var file in files)
            {
                commands.Add(new LaunchCommand(program, Expand(templates, [file], hasPlaceholder)));
            }
        }
        else
        {
            commands.Add(new LaunchCommand(program, Expand(templates, files, hasPlaceholder)));
        }

        return commands;
    }

    private static List<string> Expand(List<string> templates, IReadOnlyList<string> group, bool hasPlaceholder)
    {
        var args = new List<string>();
        foreach (var word in templates)
        {
            if (word.Contains("%s", StringComparison.Ordinal))
            {
                foreach (var name in group)
                {
                    args.Add(word.Replace("%s", name, StringComparison.Ordinal));
                }
            }
            else
            {
                args.Add(word);
            }
        }

        if (!hasPlaceholder)
        {
            foreach (var name in group)
            {
                args.Add(name);
            }
        }

        return args;
    }

    private static void RequireMode(LaunchMode mode)
    {
        if (mode != LaunchMode.OncePerFile && mode != LaunchMode.AllAtOnce)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown launch mode.");
        }
    }
}
