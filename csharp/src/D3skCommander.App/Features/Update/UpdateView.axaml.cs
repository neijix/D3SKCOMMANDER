using Avalonia.Controls;

namespace D3skCommander.App.Features.Update;

/// <summary>The update window body. It starts a check when it opens.</summary>
public sealed partial class UpdateView : UserControl
{
    public UpdateView()
    {
        InitializeComponent();
    }

    public UpdateView(UpdateViewModel model)
        : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AttachedToVisualTree += (_, _) => _ = model.CheckAsync();
    }
}
