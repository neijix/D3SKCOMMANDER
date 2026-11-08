using System.Text;

namespace D3skCommander.Core.Update;

/// <summary>
/// Joins a <see cref="Relaunch"/>'s arguments into one raw command line. An argument holding a
/// space or a quote is quoted, except a final <c>/D=</c> one: NSIS reads <c>/D=</c> only last and unquoted.
/// </summary>
public static class RelaunchCommandLine
{
    public static string Build(Relaunch relaunch)
    {
        ArgumentNullException.ThrowIfNull(relaunch);
        var line = new StringBuilder();
        var last = relaunch.Arguments.Count - 1;
        for (var i = 0; i <= last; i++)
        {
            if (i > 0)
            {
                line.Append(' ');
            }

            var argument = relaunch.Arguments[i];
            if (i == last && argument.StartsWith("/D=", StringComparison.Ordinal))
            {
                line.Append(argument);
            }
            else
            {
                line.Append(Quote(argument));
            }
        }

        return line.ToString();
    }

    private static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        return "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
