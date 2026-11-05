using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using D3skCommander.App.Controls.FileList;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.MultiRename;

/// <summary>The multi-rename form and preview over <see cref="RenameViewModel"/>.</summary>
public partial class RenameView : UserControl
{
    private readonly RenameViewModel _model = null!;

    public RenameView()
    {
        InitializeComponent();
    }

    public RenameView(RenameViewModel model, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        _model = model;
        InitializeComponent();
        NameLabel.Text = text.Get("rename.name");
        ExtLabel.Text = text.Get("rename.extension");
        SearchLabel.Text = text.Get("rename.search");
        ReplaceLabel.Text = text.Get("rename.replace");
        RegexBox.Content = text.Get("rename.regex");
        SensitiveBox.Content = text.Get("rename.case_sensitive");
        CaseLabel.Text = text.Get("rename.case");
        CounterLabel.Text = text.Get("rename.counter");
        StartLabel.Text = text.Get("rename.counter.start");
        StepLabel.Text = text.Get("rename.counter.step");
        DigitsLabel.Text = text.Get("rename.counter.digits");
        RunButton.Content = text.Get("start");
        UndoButton.Content = text.Get("rename.undo");
        CaseBox.ItemsSource = model.LocalizedCases();
        Preview = new FileListControl(model.Preview);
        PreviewHost.Content = Preview;
        DataContext = model;
        AddTokens(text);
        model.PropertyChanged += (_, e) => OnModelChanged(e.PropertyName);
        ShowStatusColor();
    }

    /// <summary>The preview list, so tests and the shell can reach its rows.</summary>
    public FileListControl Preview { get; } = null!;

    private void AddTokens(ILocalizer text)
    {
        foreach (var token in RenameToken.All)
        {
            var label = text.Get(token.LabelKey);
            var button = new Button { Content = label, Margin = new(0, 0, 4, 4), Padding = new(8, 2) };
            ToolTip.SetTip(button, text.Get(token.HintKey));
            button.Click += (_, e) => Insert(label, e);
            TokenPanel.Children.Add(button);
        }
    }

    private void Insert(string token, RoutedEventArgs e)
    {
        var caret = _model.InsertToken(token, MaskBox.SelectionStart, MaskBox.SelectionEnd);
        MaskBox.Text = _model.NameMask;
        MaskBox.SelectionStart = caret;
        MaskBox.SelectionEnd = caret;
        MaskBox.CaretIndex = caret;
        MaskBox.Focus();
        e.Handled = true;
    }

    private void OnModelChanged(string? property)
    {
        if (property is nameof(RenameViewModel.StatusIsError) or null or "")
        {
            ShowStatusColor();
        }
    }

    private void ShowStatusColor() =>
        StatusLine.Foreground = _model.StatusIsError ? new SolidColorBrush(Color.FromRgb(0xDC, 0x47, 0x3D)) : null;
}
