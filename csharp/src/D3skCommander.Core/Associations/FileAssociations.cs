using D3skCommander.Core.Launch;

namespace D3skCommander.Core.Associations;

/// <summary>A rule that answered, with the action to start.</summary>
public sealed record AssociationMatch(AssociationRule Rule, AssociationAction Action);

/// <summary>The file a rule is started for. <see cref="Selection"/> (default: the file alone) is what <c>%s</c> stands for.</summary>
public sealed record AssociationContext(string Path, IReadOnlyList<string>? Selection = null);

/// <summary>A program, its argv and its working folder, after the placeholders are expanded.</summary>
public sealed record AssociationStart(string Program, IReadOnlyList<string> Arguments, string WorkingFolder);

/// <summary>Finding the rule for a file and expanding its action. No process is started here.</summary>
public static class FileAssociations
{
    /// <summary>
    /// The first rule, in list order, whose masks match <paramref name="fileName"/> and which defines
    /// <paramref name="kind"/>; a matching rule without it is skipped. Null when none does.
    /// </summary>
    public static AssociationMatch? Find(IEnumerable<AssociationRule> rules, string fileName, AssociationKind kind)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(fileName);
        foreach (var rule in rules)
        {
            if (rule.Matches(fileName) && rule.Action(kind) is { } action)
            {
                return new AssociationMatch(rule, action);
            }
        }

        return null;
    }

    /// <summary>The named extra actions of every rule that matches <paramref name="fileName"/>, in list order.</summary>
    public static IReadOnlyList<AssociationMatch> ExtraActions(IEnumerable<AssociationRule> rules, string fileName)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(fileName);
        var found = new List<AssociationMatch>();
        foreach (var rule in rules)
        {
            if (rule.Extras.Count > 0 && rule.Matches(fileName))
            {
                found.AddRange(rule.Extras.Select(extra => new AssociationMatch(rule, extra)));
            }
        }

        return found;
    }

    /// <summary>
    /// The program and argv of <paramref name="action"/> for the file. Arguments split like a user command
    /// (double quotes group a word); with no placeholder the full path is appended. The working
    /// folder is the action's (placeholders expanded) or, when empty, the folder of the file.
    /// </summary>
    public static AssociationStart BuildStart(AssociationAction action, AssociationContext context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(context.Path);
        var files = context.Selection is { Count: > 0 } ? context.Selection : [context.Path];
        var arguments = CommandBuilder.ExpandArguments(action.Arguments, files);
        var folder = action.WorkingFolder.Trim();
        folder = folder.Length == 0
            ? Path.GetDirectoryName(context.Path) ?? ""
            : CommandBuilder.ExpandPlaceholders(folder, context.Path);
        return new AssociationStart(action.Program.Trim(), arguments, folder);
    }
}
