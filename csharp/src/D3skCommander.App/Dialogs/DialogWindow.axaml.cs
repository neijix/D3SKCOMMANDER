using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Dialogs;

public partial class DialogWindow : Window
{
    public DialogWindow()
        : this(Empty())
    {
    }

    public DialogWindow(DialogWindowModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        InitializeComponent();
        model.Attach(CloseWith);
        DataContext = model;
        Title = model.Title;
        // A form keeps the full height; a message, a question or a choice list is as tall as its text.
        if (model.ShowForm)
        {
            Height = MaxHeight;
        }
        else
        {
            SizeToContent = SizeToContent.Height;
        }

        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(editor => editor.IsVisible);
        if (box is not null)
        {
            box.Focus();
            return;
        }

        AcceptButton.Focus();
    }

    /// <summary>What the user answered; null when the window closed some other way.</summary>
    public DialogResult? Result { get; private set; }

    private void CloseWith(DialogResult result)
    {
        Result = result;
        if (IsVisible)
        {
            Close(result);
        }
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not DialogWindowModel model)
        {
            return;
        }

        if (e.Key == Key.Enter && model.AcceptCommand.CanExecute(null))
        {
            model.AcceptCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            model.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static DialogWindowModel Empty()
    {
        var localizer = Localizer.Load();
        return DialogWindowModel.Create(DialogRequest.ForMessage(localizer.Get("ok"), localizer.Get("ok")), localizer, new NoFolder());
    }

    private sealed class NoFolder : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
