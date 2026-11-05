using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace D3skCommander.App.Features.Jobs;

/// <summary>The operations line: polls its <see cref="OperationsBarModel"/> while attached, so it moves without input.</summary>
public sealed partial class OperationsBarView : UserControl
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    private readonly DispatcherTimer _timer;

    public OperationsBarView()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Model?.Refresh();
    }

    public OperationsBarView(OperationsBarModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.Refresh();
    }

    private OperationsBarModel? Model => DataContext as OperationsBarModel;

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

    private void OnCancel(object? sender, RoutedEventArgs e) => Model?.Cancel();
}
