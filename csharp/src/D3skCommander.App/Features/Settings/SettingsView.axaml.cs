using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using D3skCommander.Core.Settings;

namespace D3skCommander.App.Features.Settings;

/// <summary>
/// The settings window body: a category list and one page per category (all pages stay in the tree, only the
/// selected one is visible). OK and Cancel close the tool window that hosts it.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private WindowBase? _top;

    public SettingsView()
    {
        InitializeComponent();
        foreach (var category in SettingsCategories.All)
        {
            if (this.FindControl<Control>(PageName(category.Id)) is ScrollViewer scroller)
            {
                scroller.PropertyChanged += (_, e) =>
                {
                    if (e.Property == IsVisibleProperty && scroller.IsVisible)
                    {
                        scroller.ScrollToHome();
                    }
                };
            }
        }
    }

    public SettingsView(SettingsViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.CloseRequested += () => (TopLevel.GetTopLevel(this) as Window)?.Close();
    }

    /// <summary>The control name of a category's page root: the id in Pascal case plus "Page" ("file-operations" gives "FileOperationsPage").</summary>
    public static string PageName(string categoryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(categoryId);
        var words = categoryId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + w[1..])) + "Page";
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LayoutUpdated += OnLayoutUpdated;
        _top = TopLevel.GetTopLevel(this) as WindowBase;
        if (_top is not null)
        {
            _top.Activated += OnActivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_top is not null)
        {
            _top.Activated -= OnActivated;
            _top = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// A hidden control is never measured, so its template would stay unapplied and its content out of the visual
    /// tree: apply the templates of every page now, so each field can be found by name on any page.
    /// </summary>
    private void EnsureTemplates()
    {
        int before;
        int after = this.GetVisualDescendants().Count();
        do
        {
            before = after;
            foreach (var presenter in this.GetVisualDescendants().OfType<ContentPresenter>().ToList())
            {
                presenter.UpdateChild();
            }

            foreach (var control in this.GetVisualDescendants().OfType<TemplatedControl>().ToList())
            {
                control.ApplyTemplate();
            }

            after = this.GetVisualDescendants().Count();
        }
        while (after != before);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        EnsureTemplates();
    }

    private void OnActivated(object? sender, EventArgs e) => (DataContext as SettingsViewModel)?.RefreshDraftState();
}
