using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.Core.Common;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Launch;

/// <summary>
/// The line under the panels. Enter hands the text to <see cref="CommandLine.Interpret"/>:
/// <c>cd</c> moves the active panel, anything else runs through the shell in the active folder.
/// </summary>
public sealed partial class CommandLineModel : ObservableObject
{
    private readonly IPanelWorkspace _panels;
    private readonly IProcessRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;

    [ObservableProperty]
    private string _prompt = ">";

    [ObservableProperty]
    private string _line = "";

    public CommandLineModel(IPanelWorkspace panels, IProcessRunner runner, IDialogService dialogs, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        _panels = panels;
        _runner = runner;
        _dialogs = dialogs;
        _text = text;
    }

    public string Hint => _text.Get("launch.hint");

    /// <summary>Shows the active panel's folder as <c>folder&gt;</c>.</summary>
    public void Refresh()
    {
        var path = _panels.Read(_panels.ActiveSide).Path;
        Prompt = path.Length == 0 ? ">" : path + ">";
    }

    public async Task RunAsync()
    {
        var path = _panels.Read(_panels.ActiveSide).Path;
        var decision = CommandLine.Interpret(Line, path.Length == 0 ? null : path);
        if (decision.Kind == CommandLineKind.None)
        {
            return;
        }

        if (decision.Kind == CommandLineKind.Navigate)
        {
            await _panels.GoAsync(_panels.ActiveSide, decision.Path!).ConfigureAwait(true);
            Line = "";
        }
        else if (decision.Kind == CommandLineKind.Error)
        {
            await Fail(decision.Error!).ConfigureAwait(true);
        }
        else
        {
            var result = Launcher.RunInShell(decision.Line, decision.WorkingDirectory!, _runner);
            if (result.Error is not null)
            {
                await Fail(result.Error).ConfigureAwait(true);
            }
            else
            {
                Line = "";
            }
        }

        Refresh();
    }

    private Task Fail(OperationError error) =>
        _dialogs.ShowMessageAsync(_text.Get("error"), ErrorLines.Describe(_text, error));
}
