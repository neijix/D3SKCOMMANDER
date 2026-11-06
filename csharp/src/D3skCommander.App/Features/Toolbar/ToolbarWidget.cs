using Avalonia.Controls;
using D3skCommander.App.Shell;

namespace D3skCommander.App.Features.Toolbar;

/// <summary>The shell widget of the main toolbar.</summary>
public sealed class ToolbarWidget : IShellWidget
{
    private readonly ToolbarModel _model;

    public ToolbarWidget(ToolbarModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
    }

    public string? Name => "Toolbar";

    public Control CreateContent() => new ToolbarView(_model);
}
