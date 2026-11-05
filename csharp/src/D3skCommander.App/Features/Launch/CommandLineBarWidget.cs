using Avalonia.Controls;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Launch;

/// <summary>The shell widget for the command line under the panels.</summary>
public sealed class CommandLineBarWidget : IShellWidget
{
    private readonly IPanelWorkspace _panels;
    private readonly IProcessRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;

    public CommandLineBarWidget(IPanelWorkspace panels, IProcessRunner runner, IDialogService dialogs, ILocalizer text)
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

    public string? Name => "CommandLine";

    public Control CreateContent() =>
        new CommandLineView(new CommandLineModel(_panels, _runner, _dialogs, _text)) { Name = Name };
}
