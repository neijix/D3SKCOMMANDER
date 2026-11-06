using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Tree;

/// <summary>Reads the folders under one tree node through a provider. Never throws: a folder that cannot be read is a failed, empty result.</summary>
public sealed class FolderTreeLoader
{
    private readonly IFileSystemProvider _provider;

    public FolderTreeLoader(IFileSystemProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public async Task<FolderChildren> LoadChildrenAsync(string path, bool showHidden, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var entries = new List<Entry>();
        try
        {
            await foreach (var item in _provider.ListAsync(path, showHidden, cancellationToken).ConfigureAwait(false))
            {
                if (!item.IsSuccess)
                {
                    return FolderChildren.Failure;
                }

                entries.Add(item.Value);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException or NotSupportedException)
        {
            return FolderChildren.Failure;
        }

        return new FolderChildren(FolderTree.ChildFolders(entries, showHidden), false);
    }
}
