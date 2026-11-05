using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3skCommander.App.Controls.FileList;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Crypt;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Ftp;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Ftp;

/// <summary>
/// The FTP window: the site list while disconnected, the remote browser while connected.
/// The password of a site is never saved with it: it comes from the password vault or is asked at connect time.
/// </summary>
public sealed partial class FtpViewModel : ObservableObject, IShellWidget, ICloseGuard
{
    private const string CertificatePrefix = "server certificate refused: ";

    private readonly ILocalizer _text;
    private readonly FtpSiteStore _store;
    private readonly IFtpGateway _gateway;
    private readonly IPasswordVault _vault;
    private readonly IDialogService _dialogs;
    private readonly IJobService _jobs;
    private readonly IPanelWorkspace _panels;
    private readonly FileList _remote;
    private List<Entry> _rows = [];
    private IFtpSession? _session;
    private CancellationTokenSource? _connecting;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _remotePath = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _errorLine = "";

    public FtpViewModel(
        ILocalizer text,
        FtpSiteStore store,
        IFtpGateway gateway,
        IPasswordVault vault,
        IDialogService dialogs,
        IJobService jobs,
        IPanelWorkspace panels)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(panels);
        _text = text;
        _store = store;
        _gateway = gateway;
        _vault = vault;
        _dialogs = dialogs;
        _jobs = jobs;
        _panels = panels;
        _remote = new FileList(text);
        _status = text.Get("ftp.status.ready");
        AddCommand = new AsyncRelayCommand(AddSiteAsync);
        EditCommand = new AsyncRelayCommand(EditSiteAsync, () => SelectedSite is not null);
        DeleteCommand = new AsyncRelayCommand(DeleteSiteAsync, () => SelectedSite is not null);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => SelectedSite is not null && !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        UpCommand = new AsyncRelayCommand(GoUpAsync);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync);
        UploadCommand = new AsyncRelayCommand(UploadAsync);
        LoadSites();
    }

    public string? Name => "Ftp";

    /// <summary>The remote table: the shared file list control's model.</summary>
    public FileList Remote => _remote;

    /// <summary>The names of the saved sites, in file order.</summary>
    public ObservableCollection<string> SiteNames { get; } = [];

    public IAsyncRelayCommand AddCommand { get; }

    public IAsyncRelayCommand EditCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand ConnectCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand UpCommand { get; }

    public IAsyncRelayCommand DownloadCommand { get; }

    public IAsyncRelayCommand UploadCommand { get; }

    /// <summary>The remote entries now shown (the parent row first when there is one).</summary>
    public IReadOnlyList<Entry> Rows => _rows;

    public FtpSite? SelectedSite
    {
        get
        {
            var items = _store.Current.Items;
            return SelectedIndex >= 0 && SelectedIndex < items.Count ? items[SelectedIndex] : null;
        }
    }

    public FtpView CreateView() => new(this, _text);

    public Avalonia.Controls.Control CreateContent() => CreateView();

    /// <summary>The window closes: the connection ends first. Always agrees.</summary>
    public async Task<bool> CanCloseAsync()
    {
        await DisconnectAsync().ConfigureAwait(true);
        return true;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => ConnectCommand.NotifyCanExecuteChanged();

    /// <summary>The line for an error: the typed text, and for a refused certificate its reason.</summary>
    internal string Describe(OperationError error)
    {
        if (error.Kind == ErrorKind.Network && error.Message.StartsWith(CertificatePrefix, StringComparison.Ordinal))
        {
            return _text.Get("ftp.cert_refused") + ": " + error.Message[CertificatePrefix.Length..];
        }

        return ErrorLines.Describe(_text, error);
    }

    private void Fail(OperationError error) => ErrorLine = Describe(error);
}
