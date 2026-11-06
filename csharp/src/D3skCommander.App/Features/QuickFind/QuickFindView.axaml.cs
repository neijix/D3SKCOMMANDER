using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.QuickFind;

/// <summary>The quick find window over <see cref="QuickFindViewModel"/>: the box, the list and the buttons.</summary>
public partial class QuickFindView : UserControl
{
    private readonly QuickFindViewModel _model = null!;

    public QuickFindView()
    {
        InitializeComponent();
    }

    public QuickFindView(QuickFindViewModel model, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        _model = model;
        InitializeComponent();
        DataContext = model;
        Label(text);
        QueryBox.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        ResultList.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        ResultList.DoubleTapped += (_, _) => _ = model.RevealSelectedAsync();
        CancelButton.Click += (_, _) => model.CancelBuild();
        RebuildButton.Click += (_, _) => model.RebuildIndex();
        FeedButton.Click += (_, _) => _ = model.FeedToPanelAsync();
        model.FocusRequested += FocusBox;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuickFindViewModel.Selected) && model.Selected is { } row)
            {
                ResultList.ScrollIntoView(row);
            }
        };
        AttachedToVisualTree += (_, _) => FocusBox();
        DetachedFromVisualTree += (_, _) => model.FocusRequested -= FocusBox;
    }

    private void Label(ILocalizer text)
    {
        QueryBox.PlaceholderText = text.Get("quickfind.hint");
        NameHeader.Text = text.Get("quickfind.col.name");
        FolderHeader.Text = text.Get("quickfind.col.folder");
        SizeHeader.Text = text.Get("quickfind.col.size");
        DateHeader.Text = text.Get("quickfind.col.date");
        CancelButton.Content = text.Get("quickfind.cancel");
        RebuildButton.Content = text.Get("quickfind.rebuild");
        FeedButton.Content = text.Get("quickfind.feed");
    }

    private void FocusBox() => Dispatcher.UIThread.Post(() => QueryBox.Focus(), DispatcherPriority.Background);

    // Up and Down move the list from the box; Enter shows the hit, Ctrl+Enter feeds all hits to a panel.
    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _model.MoveSelection(1);
                break;
            case Key.Up:
                _model.MoveSelection(-1);
                break;
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _ = _model.FeedToPanelAsync();
                break;
            case Key.Enter:
                _ = _model.RevealSelectedAsync();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
