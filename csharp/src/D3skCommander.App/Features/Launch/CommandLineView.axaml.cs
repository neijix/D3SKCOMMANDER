using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace D3skCommander.App.Features.Launch;

/// <summary>The command line. Follows the active panel's folder for its prompt while attached.</summary>
public sealed partial class CommandLineView : UserControl
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherTimer _timer;

    public CommandLineView()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Model?.Refresh();
    }

    public CommandLineView(CommandLineModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.Refresh();
    }

    private CommandLineModel? Model => DataContext as CommandLineModel;

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

    private void OnFocused(object? sender, FocusChangedEventArgs e) => Model?.Refresh();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Model is null)
        {
            return;
        }

        e.Handled = true;
        _ = Model.RunAsync();
    }
}
