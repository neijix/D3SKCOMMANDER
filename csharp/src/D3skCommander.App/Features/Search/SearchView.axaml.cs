using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Search;

/// <summary>The search form and result table over <see cref="SearchViewModel"/>. Polls the model while attached.</summary>
public sealed partial class SearchView : UserControl
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly SearchViewModel _model = null!;
    private readonly DispatcherTimer _timer = new() { Interval = Interval };

    public SearchView()
    {
        InitializeComponent();
    }

    public SearchView(SearchViewModel model, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        _model = model;
        InitializeComponent();
        Label(text);
        Results = new FileListControl(model.Results);
        Results.RowActivated += (_, _) => _ = model.GoToCursorAsync();
        Results.EnterPressed += (_, _) => _ = model.GoToCursorAsync();
        ResultsHost.Content = Results;
        DataContext = model;
        _timer.Tick += (_, _) => model.Drain();
        AddHandler(KeyDownEvent, OnFormKey, RoutingStrategies.Bubble);
    }

    /// <summary>The result table, so tests and the shell can reach its rows.</summary>
    public FileListControl Results { get; } = null!;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _model?.Drain();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFormKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is TextBox && !IsDropDownOpen(e.Source))
        {
            _model.Start();
            e.Handled = true;
        }
    }

    private static bool IsDropDownOpen(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<ComboBox>() is { IsDropDownOpen: true };

    private void ShowTemplates(ILocalizer text)
    {
        var menu = new MenuFlyout();
        var names = _model.TemplateNames;
        if (names.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = text.Get("search.template.none"), IsEnabled = false });
        }

        foreach (var name in names)
        {
            var load = new MenuItem { Header = name };
            load.Click += (_, _) => _model.LoadTemplate(name);
            menu.Items.Add(load);
        }

        if (names.Count > 0)
        {
            var delete = new MenuItem { Header = text.Get("search.template.delete") };
            foreach (var name in names)
            {
                var one = new MenuItem { Header = name };
                one.Click += (_, _) => _model.DeleteTemplate(name);
                delete.Items.Add(one);
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(delete);
        }

        menu.ShowAt(TemplatesButton);
    }

    private void Label(ILocalizer text)
    {
        RootsLabel.Text = text.Get("search.roots");
        BrowseButton.Content = text.Get("search.browse");
        ToolTip.SetTip(BrowseButton, text.Get("dialog.browse"));
        NameLabel.Text = text.Get("search.name");
        NameRegexBox.Content = text.Get("search.name_regex");
        TextLabel.Text = text.Get("search.text");
        TextRegexBox.Content = text.Get("search.text_regex");
        CaseBox.Content = text.Get("search.case");
        WordsBox.Content = text.Get("search.whole_words");
        NotBox.Content = text.Get("search.not_containing");
        FilesBox.Content = text.Get("search.files");
        FoldersBox.Content = text.Get("search.folders");
        HiddenBox.Content = text.Get("search.hidden");
        ArchivesBox.Content = text.Get("search.archives");
        DepthBox.Content = text.Get("search.depth_limit");
        MinLabel.Text = text.Get("search.min_size");
        MaxLabel.Text = text.Get("search.max_size");
        NewerLabel.Text = text.Get("search.newer_days");
        OlderLabel.Text = text.Get("search.older_days");
        StartButton.Content = text.Get("start");
        CancelButton.Content = text.Get("cancel");
        GoButton.Content = text.Get("search.go");
        FeedButton.Content = text.Get("search.feed");
        ExcludeFilesLabel.Text = text.Get("search.exclude_files");
        ExcludeFoldersLabel.Text = text.Get("search.exclude_folders");
        LinksBox.Content = text.Get("search.follow_links");
        SaveTemplateButton.Content = text.Get("search.template.save");
        TemplatesButton.Content = text.Get("search.template.menu");
        TemplatesButton.Click += (_, _) => ShowTemplates(text);
    }
}
