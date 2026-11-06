using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Index;

namespace D3skCommander.App.Features.QuickFind;

/// <summary>
/// Ctrl+Shift+F opens the window. At start the saved index is read, and rebuilt in the background only when
/// it is older than the refresh setting: a first launch never scans the disk.
/// </summary>
public sealed class QuickFindCommands
{
    private readonly IToolWindowHost _tools;
    private readonly QuickFindViewModel _window;
    private readonly IndexService _index;
    private readonly AppSettings _settings;

    public QuickFindCommands(IToolWindowHost tools, QuickFindViewModel window, IndexService index, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(settings);
        _tools = tools;
        _window = window;
        _index = index;
        _settings = settings;
    }

    public void Bind(IShellContributions shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.Handle(CommandId.QuickFind, () => _ = OpenAsync());
        shell.OnStartup(() => _ = RefreshAtStartAsync());
        _tools.Register(ToolKind.QuickFind, () => _window);
    }

    public Task OpenAsync()
    {
        _tools.Open(ToolKind.QuickFind);
        return _window.OpenedAsync();
    }

    public async Task RefreshAtStartAsync()
    {
        try
        {
            if (!_index.HasFile)
            {
                return;
            }

            await _index.LoadAsync().ConfigureAwait(true);
            if (_index.IsDue(_settings.Preferences.Current.IndexRefreshHours))
            {
                await _index.BuildAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The index is a cache: a failed refresh leaves the old one.
        }
    }
}
