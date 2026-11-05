using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Crypt;
using D3skCommander.App.Features.Ftp;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.Crypt;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Ftp;

namespace D3skCommander.App.Tests.Features.Ftp;

/// <summary>An in-memory remote tree: folders map to their entries; file bytes by path.</summary>
internal sealed class FakeSession : IFtpSession
{
    public Dictionary<string, List<Entry>> Folders { get; } = [];

    public Dictionary<string, byte[]> Files { get; } = [];

    public List<string> Created { get; } = [];

    public List<string> Uploaded { get; } = [];

    public bool Disposed { get; private set; }

    public bool IsConnected => !Disposed;

    public Task<FileResult<string>> WorkingDirectoryAsync(CancellationToken cancellationToken) =>
        Task.FromResult(FileResult<string>.Ok("/"));

    public Task<FileResult<IReadOnlyList<Entry>>> ListAsync(string directory, CancellationToken cancellationToken)
    {
        if (!Folders.TryGetValue(directory, out var entries))
        {
            return Task.FromResult(FileResult<IReadOnlyList<Entry>>.Fail(OperationError.NotFound(directory)));
        }

        return Task.FromResult(FileResult<IReadOnlyList<Entry>>.Ok(entries));
    }

    public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        Created.Add(path);
        return Task.FromResult(FileStatus.Success);
    }

    public Task<FileResult<FtpTransfer>> DownloadAsync(string remote, string local, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var bytes = Files[remote];
        File.WriteAllBytes(local, bytes);
        progress?.Report(bytes.Length);
        return Task.FromResult(FileResult<FtpTransfer>.Ok(new FtpTransfer(bytes.Length)));
    }

    public Task<FileResult<FtpTransfer>> UploadAsync(string local, string remote, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var bytes = File.ReadAllBytes(local);
        Files[remote] = bytes;
        Uploaded.Add(remote);
        return Task.FromResult(FileResult<FtpTransfer>.Ok(new FtpTransfer(bytes.Length)));
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public static Entry Dir(string folder, string name) =>
        new(name, folder.TrimEnd('/') + "/" + name, true, false, false, 0, null, false, false, "");

    public static Entry Leaf(string folder, string name, ulong size) =>
        new(name, folder.TrimEnd('/') + "/" + name, false, false, false, size, null, false, false, "");
}

internal sealed class FakeGateway(FakeSession session) : IFtpGateway
{
    public OperationError? Refusal { get; set; }

    public string? Password { get; private set; }

    public FtpSite? Site { get; private set; }

    public Task<FileResult<IFtpSession>> ConnectAsync(FtpSite site, string password, CancellationToken cancellationToken)
    {
        Site = site;
        Password = password;
        if (Refusal is not null)
        {
            return Task.FromResult(FileResult<IFtpSession>.Fail(Refusal));
        }

        return Task.FromResult(FileResult<IFtpSession>.Ok(session));
    }
}

/// <summary>Answers each form through a callback; records what was shown.</summary>
internal sealed class ScriptedDialogs : IDialogService
{
    public Func<DialogForm, bool> OnForm { get; set; } = _ => false;

    public bool Confirm { get; set; } = true;

    public List<DialogForm> Forms { get; } = [];

    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default) => Task.FromResult(Confirm);

    public Task<string?> AskTextAsync(string title, string label, string initialText = "", CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices, CancellationToken cancellationToken = default) =>
        Task.FromResult<int?>(null);

    public Task<bool> ShowFormAsync(DialogForm form, CancellationToken cancellationToken = default)
    {
        Forms.Add(form);
        return Task.FromResult(OnForm(form));
    }
}

internal sealed class FakeVault : IPasswordVault
{
    public PasswordStore? Current { get; private set; }

    public int Saves { get; private set; }

    public async Task<PasswordStore?> UnlockAsync()
    {
        await Task.Yield();
        Current ??= PasswordStore.Open(Path.Combine(Path.GetTempPath(), "d3sk-ftp-vault-" + Guid.NewGuid().ToString("N")), "master").Value;
        return Current;
    }

    public Task<OperationError?> SaveAsync()
    {
        Saves++;
        return Task.FromResult<OperationError?>(null);
    }

    public void Lock() => Current = null;
}

internal sealed class FtpPanels(string activeFolder, params string[] selection) : IPanelWorkspace
{
    public int Refreshes { get; private set; }

    public PanelSide ActiveSide => PanelSide.Left;

    public PanelSnapshot Read(PanelSide side) => PanelSnapshot.Empty with { Path = activeFolder };

    public PanelCapture? Capture() => new(selection, activeFolder, activeFolder);

    public Task<bool> GoAsync(PanelSide side, string path, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Refreshes++;
        return Task.CompletedTask;
    }
}
