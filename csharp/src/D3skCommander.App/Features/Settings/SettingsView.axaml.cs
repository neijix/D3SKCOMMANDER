using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace D3skCommander.App.Features.Settings;

/// <summary>The settings window body. OK and Cancel close the tool window that hosts it.</summary>
public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public SettingsView(SettingsViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.CloseRequested += () => (TopLevel.GetTopLevel(this) as Window)?.Close();
        model.ToolbarRequested += () => Dispatcher.UIThread.Post(() => ScrollToTop(this.FindControl<ToolbarEditor>("ToolbarSection")), DispatcherPriority.Background);
        model.FavoritesRequested += () => Dispatcher.UIThread.Post(() => ScrollToTop(this.FindControl<FavoriteTabsEditor>("FavoritesSection")), DispatcherPriority.Background);
        model.ColumnsRequested += () => Dispatcher.UIThread.Post(() => this.FindControl<ColumnSetsEditor>("ColumnSetsSection")?.BringIntoView());
    }

    /// <summary>Scrolls so the top of a section sits at the top of the window: a tall section shows from its title.</summary>
    private void ScrollToTop(Control? section)
    {
        var scroller = this.FindControl<ScrollViewer>("SettingsScroll");
        if (section is null || scroller is null)
        {
            return;
        }

        scroller.UpdateLayout();
        if (section.TranslatePoint(default, scroller.Content as Visual ?? scroller) is Point top)
        {
            scroller.Offset = new Vector(0, top.Y);
        }
    }
}
