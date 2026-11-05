using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace D3skCommander.App.Features.Compare;

/// <summary>The compare window body: polls its <see cref="CompareViewModel"/> while attached and scrolls to the current block.</summary>
public sealed partial class CompareView : UserControl
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly DispatcherTimer _timer;

    public CompareView()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Model?.Refresh();
    }

    public CompareView(CompareViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.ScrollRequested += OnScroll;
        model.Refresh();
    }

    private CompareViewModel? Model => DataContext as CompareViewModel;

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

    private void OnScroll(int row)
    {
        if (row >= 0 && row < RowList.ItemCount)
        {
            RowList.ScrollIntoView(row);
        }
    }
}
