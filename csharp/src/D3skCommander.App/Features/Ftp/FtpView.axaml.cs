using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Ftp;

/// <summary>The site list and the remote browser over <see cref="FtpViewModel"/>.</summary>
public sealed partial class FtpView : UserControl
{
    private readonly FtpViewModel _model = null!;

    public FtpView()
    {
        InitializeComponent();
    }

    public FtpView(FtpViewModel model, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        _model = model;
        InitializeComponent();
        Label(text);
        Remote = new FileListControl(model.Remote);
        Remote.RowActivated += (_, _) => _ = model.OpenCursorAsync();
        Remote.EnterPressed += (_, _) => _ = model.OpenCursorAsync();
        RemoteHost.Content = Remote;
        DataContext = model;
        AddHandler(KeyDownEvent, OnViewKey, RoutingStrategies.Tunnel);
    }

    /// <summary>The remote table, so tests and the shell can reach its rows.</summary>
    public FileListControl Remote { get; } = null!;

    private void OnViewKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && _model.IsConnected && e.Source is not TextBox)
        {
            _ = _model.GoUpAsync();
            e.Handled = true;
        }
    }

    private void Label(ILocalizer text)
    {
        ConnectButton.Content = text.Get("ftp.connect");
        AddButton.Content = text.Get("ftp.add");
        EditButton.Content = text.Get("ftp.edit");
        DeleteButton.Content = text.Get("ftp.delete");
        UpButton.Content = text.Get("ftp.up");
        RefreshButton.Content = text.Get("ftp.refresh");
        DownloadButton.Content = text.Get("ftp.download");
        UploadButton.Content = text.Get("ftp.upload");
        DisconnectButton.Content = text.Get("ftp.disconnect");
    }
}
