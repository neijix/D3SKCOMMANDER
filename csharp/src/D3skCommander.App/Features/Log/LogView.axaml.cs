using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Log;

/// <summary>The operations log over <see cref="LogViewModel"/>; the newest line stays in view.</summary>
public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
    }

    public LogView(LogViewModel model, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        InitializeComponent();
        DataContext = model;
        RefreshButton.Content = text.Get("log.refresh");
        ClearButton.Content = text.Get("log.clear_view");
        RefreshButton.Click += (_, _) => model.Refresh();
        ClearButton.Click += (_, _) => model.ClearView();
        model.Lines.CollectionChanged += OnLinesChanged;
        DetachedFromVisualTree += (_, _) => model.Lines.CollectionChanged -= OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || sender is not IList<string> lines || lines.Count == 0)
        {
            return;
        }

        var last = lines[^1];
        Dispatcher.UIThread.Post(() => LineList.ScrollIntoView(last), DispatcherPriority.Background);
    }
}
