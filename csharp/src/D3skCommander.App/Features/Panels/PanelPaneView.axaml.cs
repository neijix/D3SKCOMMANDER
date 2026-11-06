using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using D3skCommander.App.Controls.FileList;

namespace D3skCommander.App.Features.Panels;

/// <summary>Panel bar buttons, path bar, Browse, the file list control and the selection count of one panel.</summary>
public sealed partial class PanelPaneView : UserControl
{
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(100);

    private readonly DispatcherTimer _timer;
    private FileListControl? _files;

    public PanelPaneView()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = Beat };
        _timer.Tick += (_, _) => Model?.Tick();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        PathBox.KeyDown += OnPathKey;
        PathBox.GotFocus += (_, _) => Edit(true);
        PathBox.LostFocus += (_, _) => Edit(false);
        FilterBox.KeyDown += OnFilterKey;
        EnableDrop();
        BrowseButton.Click += (_, _) => _ = Model?.BrowseAsync();
        ListArea.SizeChanged += (_, e) => Model?.Fit(e.NewSize.Width);
    }

    public PanelPaneView(PanelPaneModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        _files = new FileListControl(model.List)
        {
            AcceptKey = e => model.AcceptsKey(e.KeyModifiers.HasFlag(KeyModifiers.Control), e.KeyModifiers.HasFlag(KeyModifiers.Meta)),
        };
        _files.KeyHandled += (_, _) => model.Sync();
        _files.EnterPressed += (_, _) => _ = model.OpenEntryAsync();
        _files.RowActivated += (_, _) => _ = model.OpenEntryAsync();
        BindHeader(model, _files);
        ListHost.Content = _files;
        BuildBar(model);
        ShowDrives(model);
        BuildTree(model);
        model.FilterOpened += (_, _) => Dispatcher.UIThread.Post(() => FilterBox.Focus());
    }

    public FileListControl Files => _files ?? throw new InvalidOperationException("The pane has no model.");

    private PanelPaneModel? Model => DataContext as PanelPaneModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Model?.Show();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ReferenceEquals(e.Source, this))
        {
            _files?.HandleKey(e);
        }
    }

    private void Edit(bool editing)
    {
        if (Model is { } model)
        {
            model.EditingPath = editing;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        model.Activate();
        if (e.Source is Visual source && source.FindAncestorOfType<FileListControl>(true) is not null)
        {
            Focus();
            Dispatcher.UIThread.Post(model.Sync);
        }
    }

    private void OnFilterKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Model?.CloseFilter();
            Focus();
            e.Handled = true;
        }
    }

    private void OnPathKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = Model?.CommitPathAsync();
            e.Handled = true;
        }
    }
}
