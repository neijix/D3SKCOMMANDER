namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelWorkspace
{
    /// <summary>Moves the active panel to the folder holding <paramref name="path"/> and puts the cursor on it.</summary>
    public async Task<bool> RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        await EnsureOpen(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _nav.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var side = _active;
            var tab = TabOf(side);
            var folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
            if (tab is null)
            {
                return false;
            }

            var target = string.IsNullOrEmpty(folder) ? path : folder;
            if (!string.Equals(target, tab.Location.Path, StringComparison.Ordinal)
                && !await GoCore(side, target, linked.Token, announce: true).ConfigureAwait(false))
            {
                return false;
            }

            tab.CursorToPath(Path.TrimEndingDirectorySeparator(path));
            Publish(side, announce: false);
            return true;
        }
        finally
        {
            _nav.Release();
        }
    }
}
