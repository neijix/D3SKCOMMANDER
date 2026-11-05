using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace D3skCommander.App.Features.Lister;

/// <summary>Draws <see cref="ViewerViewModel"/>: mode switch, find or filter bar, virtualized rows, status line.</summary>
public partial class ViewerView : UserControl
{
    private const double CharWidth = 8;

    public ViewerView()
    {
        InitializeComponent();
        ModeBox.SelectionChanged += OnModeChanged;
        NextButton.Click += (_, _) => Model?.Find(true);
        PreviousButton.Click += (_, _) => Model?.Find(false);
        FindBox.KeyDown += OnFindKey;
        RowList.SelectionChanged += OnRowSelected;
    }

    private ViewerViewModel? Model => DataContext as ViewerViewModel;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Model?.SetColumns((int)((e.NewSize.Width - 80) / CharWidth));
    }

    private void OnModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ModeBox.SelectedItem is ViewerModeChoice choice && Model is { } model && model.SelectedMode != choice)
        {
            _ = model.SetModeAsync(choice.Mode);
        }
    }

    private void OnFindKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Model?.Find(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
    }

    private void OnRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RowList.SelectedIndex >= 0)
        {
            RowList.ScrollIntoView(RowList.SelectedIndex);
        }
    }
}
