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
        model.AssociationsRequested += () => Dispatcher.UIThread.Post(() => ScrollToTop(this.FindControl<AssociationsEditor>("AssociationsSection")), DispatcherPriority.Background);
        model.ColumnsRequested += () => Dispatcher.UIThread.Post(() => ScrollToTop(this.FindControl<ColumnSetsEditor>("ColumnSetsSection")), DispatcherPriority.Background);
    }

    /// <summary>Scrolls so the top of a section sits at the top of the window: a tall section shows from its title.</summary>
    private void ScrollToTop(Control? section)
    {
        var scroller = this.FindControl<ScrollViewer>("SettingsScroll");
        if (section is null || scroller is null)
        {
            return;
        }

        // The window may not be laid out yet when the request comes: wait for a layout pass that
        // places the section, then apply once more after a list inside another section has
        // brought its selected row into view (that moves this scroller too).
        if (!Apply())
        {
            void OnLayout(object? sender, EventArgs e)
            {
                if (Apply())
                {
                    scroller.LayoutUpdated -= OnLayout;
                    DispatcherTimer.RunOnce(() => Apply(), TimeSpan.FromMilliseconds(300));
                }
            }

            scroller.LayoutUpdated += OnLayout;
            return;
        }

        DispatcherTimer.RunOnce(() => Apply(), TimeSpan.FromMilliseconds(300));

        bool Apply()
        {
            if (scroller.Extent.Height <= 0
                || section.TranslatePoint(default, scroller.Content as Visual ?? scroller) is not Point top)
            {
                return false;
            }

            scroller.Offset = new Vector(0, top.Y);
            return true;
        }
    }
}
