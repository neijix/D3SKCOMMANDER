using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace D3skCommander.App.Features.Sync;

/// <summary>The sync window body: polls its <see cref="SyncViewModel"/> while attached to pick up job progress.</summary>
public sealed partial class SyncView : UserControl
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly DispatcherTimer _timer;

    public SyncView()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Model?.Refresh();
    }

    public SyncView(SyncViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
    }

    private SyncViewModel? Model => DataContext as SyncViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Model?.Refresh();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
