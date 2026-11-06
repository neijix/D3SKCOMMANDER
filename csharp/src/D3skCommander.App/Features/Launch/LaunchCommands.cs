using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Common;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Toolbar;

namespace D3skCommander.App.Features.Launch;

/// <summary>OpenWith, Terminal, user commands and the default open. Arguments go as argv; nothing here builds a shell string.</summary>
public sealed class LaunchCommands : IFileLauncher
{
    private readonly IPanelWorkspace _panels;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _text;
    private readonly IProcessRunner _runner;
    private readonly AppSettings _settings;

    public LaunchCommands(
        IPanelWorkspace panels,
        IDialogService dialogs,
        ILocalizer text,
        IProcessRunner runner,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(settings);
        _panels = panels;
        _dialogs = dialogs;
        _text = text;
        _runner = runner;
        _settings = settings;
    }

    public LaunchResult OpenDefault(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Report(Launcher.OpenWithDefault(path, _runner));
    }

    /// <summary>The Terminal command: the preference when set, else the platform default, in the active folder.</summary>
    public void OpenTerminal()
    {
        var capture = _panels.Capture();
        if (capture is null)
        {
            return;
        }

        var configured = _settings.Preferences.Current.Terminal;
        Report(Launcher.OpenTerminal(configured, capture.ActiveDirectory, _runner));
    }

    /// <summary>The Tools-menu items of the user commands set in the settings, read when the menu opens.</summary>
    public IReadOnlyList<ShellAction> UserCommandItems()
    {
        var items = new List<ShellAction>();
        foreach (var command in _settings.Preferences.Current.UserCommands)
        {
            items.Add(new ShellAction(command.Name, () => RunUserCommand(command)));
        }

        return items;
    }

    /// <summary>Runs a Tools-menu user command on the selected files.</summary>
    public LaunchResult RunUserCommand(UserCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var capture = _panels.Capture();
        if (capture is null)
        {
            return LaunchResult.Ok(0);
        }

        var files = LocalFiles(capture);
        return Report(Launcher.Launch(command, files, capture.ActiveDirectory, _runner));
    }

    /// <summary>Starts the processes of a toolbar button; a failure is shown to the user.</summary>
    public LaunchResult Run(IReadOnlyList<ProcessRequest> requests) => Report(Launcher.Run(requests, _runner));

    /// <summary>What a toolbar button sees: the selected local files, the active folder (empty when no panel is open) and the user commands.</summary>
    public ToolbarContext ToolbarContext()
    {
        var capture = _panels.Capture();
        return new ToolbarContext(
            capture is null ? [] : LocalFiles(capture),
            capture?.ActiveDirectory ?? "",
            _settings.Preferences.Current.UserCommands);
    }

    public async Task OpenWithAsync()
    {
        var capture = _panels.Capture();
        if (capture is null)
        {
            return;
        }

        var files = LocalFiles(capture);
        if (files.Count == 0)
        {
            return;
        }

        var program = new TextDialogField(_text.Get("launch.openwith.program"));
        var once = new CheckDialogField(_text.Get("dlg.once_per_file"));
        var system = new CheckDialogField(_text.Get("launch.openwith.default"));
        var form = new DialogForm(
            _text.Get("cmd.open_with"),
            [
                new NoteDialogField(Summary(files)),
                program,
                new NoteDialogField(_text.Get("launch.openwith.hint")),
                once,
                system,
            ],
            candidate => system.IsChecked || program.Text.Trim().Length > 0);
        if (!await _dialogs.ShowFormAsync(form).ConfigureAwait(true))
        {
            return;
        }

        if (system.IsChecked)
        {
            foreach (var file in files)
            {
                Report(Launcher.OpenWithDefault(file, _runner));
            }

            return;
        }

        var mode = once.IsChecked ? LaunchMode.OncePerFile : LaunchMode.AllAtOnce;
        Report(Launcher.Launch(program.Text, files, mode, capture.ActiveDirectory, _runner));
    }

    private string Summary(IReadOnlyList<string> files)
    {
        if (files.Count == 1)
        {
            return files[0];
        }

        return files.Count + " " + _text.Get("files");
    }

    private static List<string> LocalFiles(PanelCapture capture)
    {
        var files = new List<string>();
        foreach (var path in capture.Sources)
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        return files;
    }

    private LaunchResult Report(LaunchResult result)
    {
        if (result.Error is not null)
        {
            _ = ShowFailureAsync(result.Error);
        }

        return result;
    }

    private async Task ShowFailureAsync(OperationError error)
    {
        var detail = ErrorLines.Describe(_text, error);
        if (!string.IsNullOrEmpty(error.Message))
        {
            detail += "\n" + error.Message;
        }

        await _dialogs.ShowMessageAsync(_text.Get("launch.failed"), detail).ConfigureAwait(false);
    }
}
