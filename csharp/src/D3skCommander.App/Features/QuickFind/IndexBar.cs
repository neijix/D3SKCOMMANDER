using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using D3skCommander.App.Shell;
using D3skCommander.Core.Index;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.QuickFind;

/// <summary>The status-bar widget of the index build: a bar, the entry count and Cancel, shown only while it runs.</summary>
internal sealed class IndexBarWidget(IndexService index, ILocalizer text) : IShellWidget
{
    public string? Name => "IndexBar";

    public Control CreateContent() => new IndexBar(index, text) { Name = Name };
}

/// <summary>Follows <see cref="IndexService.Changed"/> while attached; the worker's many reports become one refresh at a time.</summary>
internal sealed class IndexBar : StackPanel
{
    private readonly IndexService _index;
    private readonly ILocalizer _text;
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private int _posted;

    public IndexBar(IndexService index, ILocalizer text)
    {
        _index = index;
        _text = text;
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        Margin = new Avalonia.Thickness(8, 2);
        var cancel = new Button { Content = text.Get("quickfind.cancel"), Name = "IndexBarCancel" };
        cancel.Click += (_, _) => index.Cancel();
        Children.Add(new ProgressBar { IsIndeterminate = true, Width = 90, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(_label);
        Children.Add(cancel);
        Refresh();
        AttachedToVisualTree += (_, _) =>
        {
            index.Changed += OnChanged;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => index.Changed -= OnChanged;
    }

    private void OnChanged()
    {
        if (Interlocked.Exchange(ref _posted, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _posted, 0);
                Refresh();
            });
        }
    }

    private void Refresh()
    {
        IsVisible = _index.IsBuilding;
        _label.Text = string.Format(CultureInfo.InvariantCulture, _text.Get("quickfind.building"), _index.Progress);
    }
}
