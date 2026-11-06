using System.Text;

namespace D3skCommander.Core.Launch;

/// <summary>
/// v0.1 <c>split_command_line</c> and <c>build_commands</c>.
/// Words split on whitespace; double quotes group a word and are removed; <c>""</c> is an empty argument.
/// A placeholder in an argument (never in the program word) is replaced by each file, and that argument
/// is repeated once per file: <c>%s</c> and <c>%p</c> the full path, <c>%f</c> the file name, <c>%d</c> its folder.
/// With no placeholder, the files are appended.
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

        return BuildWords(words, files, mode);
    }

    /// <summary>
    /// One command for <paramref name="command"/> with the same placeholder rules as <see cref="Build"/>,
    /// also when no file is selected (the <c>%s</c> arguments then drop out). Empty when the command has no words.
    /// </summary>
    public static IReadOnlyList<LaunchCommand> BuildAnySelection(string command, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(files);
        var words = Split(command);
        return words.Count == 0 ? [] : BuildWords(words, files, LaunchMode.AllAtOnce);
    }

    private static List<LaunchCommand> BuildWords(IReadOnlyList<string> words, IReadOnlyList<string> files, LaunchMode mode)
    {
        var program = words[0];
        var templates = new List<string>(words.Count - 1);
        var hasPlaceholder = false;
        for (var i = 1; i < words.Count; i++)
        {
            templates.Add(words[i]);
            if (HasPlaceholder(words[i]))
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
            if (HasPlaceholder(word))
            {
                foreach (var name in group)
                {
                    args.Add(ExpandPlaceholders(word, name));
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

    /// <summary>
    /// The arguments of <paramref name="arguments"/> (split like a command line) for the files, with the
    /// same placeholder rules as <see cref="Build"/>: no program word, and no placeholder appends every file.
    /// </summary>
    public static List<string> ExpandArguments(string arguments, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(files);
        var templates = Split(arguments).ToList();
        return Expand(templates, files, templates.Any(HasPlaceholder));
    }

    /// <summary>Replaces <c>%s</c> and <c>%p</c> (full path), <c>%f</c> (file name) and <c>%d</c> (folder) in one pass, so text a file name contains is never expanded again.</summary>
    public static string ExpandPlaceholders(string text, string file)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(file);
        if (!HasPlaceholder(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + file.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 1 < text.Length && IsPlaceholder(text[i + 1]))
            {
                builder.Append(text[++i] switch
                {
                    'f' => Path.GetFileName(file),
                    'd' => Path.GetDirectoryName(file) ?? "",
                    _ => file,
                });
                continue;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    private static bool HasPlaceholder(string word)
    {
        for (var i = 0; i + 1 < word.Length; i++)
        {
            if (word[i] == '%' && IsPlaceholder(word[i + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlaceholder(char c) => c is 's' or 'p' or 'f' or 'd';

    private static void RequireMode(LaunchMode mode)
    {
        if (mode != LaunchMode.OncePerFile && mode != LaunchMode.AllAtOnce)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown launch mode.");
        }
    }
}
