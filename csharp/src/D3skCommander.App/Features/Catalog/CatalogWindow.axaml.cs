using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Catalog;

/// <summary>The catalog as an owned modal window over <see cref="CatalogDialog"/>: fields, search, hits, actions.</summary>
public partial class CatalogWindow : Window
{
    private readonly CatalogDialog _dialog = null!;

    public CatalogWindow()
    {
        InitializeComponent();
    }

    public CatalogWindow(CatalogDialog dialog, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(text);
        _dialog = dialog;
        InitializeComponent();
        DataContext = dialog;
        Title = text.Get("cmd.catalog");
        RootLabel.Text = text.Get("catalog.root");
        OutputLabel.Text = text.Get("dlg.out_file");
        FindLabel.Text = text.Get("catalog.find");
        SearchButton.Content = text.Get("catalog.search");
        CreateButton.Content = text.Get("catalog.create");
        OpenButton.Content = text.Get("catalog.open");
        CloseButton.Content = text.Get("close");
        Hits = new FileListControl(dialog.Hits);
        HitsHost.Content = Hits;
        Hits.RowActivated += (_, _) => _dialog.OpenSelected();
        SearchButton.Click += (_, _) => _ = _dialog.SearchAsync();
        CreateButton.Click += (_, _) => _ = _dialog.CreateAsync();
        OpenButton.Click += (_, _) => _dialog.OpenSelected();
        CloseButton.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) => _dialog.Close();
    }

    /// <summary>The hits list, so tests and the shell can reach its rows.</summary>
    public FileListControl Hits { get; } = null!;

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
        }
    }
}
