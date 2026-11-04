using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

/// <summary>One button on the function-key bar.</summary>
public sealed class FunctionButton
{
    private readonly Action _invoke;

    public FunctionButton(string content, CommandId command, bool enabled, Action invoke)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        ArgumentNullException.ThrowIfNull(invoke);
        Content = content;
        Command = command;
        IsEnabled = enabled;
        _invoke = invoke;
    }

    public string Content { get; }

    public CommandId Command { get; }

    public bool IsEnabled { get; }

    public void Click()
    {
        if (IsEnabled)
        {
            _invoke();
        }
    }
}
