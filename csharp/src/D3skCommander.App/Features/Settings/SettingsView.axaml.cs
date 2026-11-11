using Avalonia.Controls;

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
    }
}
