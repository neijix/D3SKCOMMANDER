using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Dialogs;

/// <summary>What the shared dialog window binds to. Built from one <see cref="DialogRequest"/>.</summary>
public sealed class DialogWindowModel : INotifyPropertyChanged
{
    private readonly DialogCommand _accept;
    private Action<DialogResult>? _finish;
    private bool _closed;
    private string _input;

    private DialogWindowModel(DialogRequest request, ILocalizer localizer)
    {
        Title = request.Title;
        Message = request.Text;
        _input = request.InitialText;
        OkText = localizer.Get("ok");
        CancelText = localizer.Get("cancel");
        Form = request.Form;
        ShowMessage = Message.Length > 0;
        ShowInput = request.Kind == DialogKind.Text;
        ShowForm = request.Kind == DialogKind.Form;
        ShowChoices = request.Kind == DialogKind.Choice;
        ShowAccept = request.Kind != DialogKind.Choice;
        ShowCancel = request.Kind != DialogKind.Message;
        _accept = new DialogCommand(Accept, CanAccept);
        CancelCommand = new DialogCommand(Cancel);
        Choices = BuildChoices(request);
        if (Form is not null)
        {
            Form.Changed += (_, _) => _accept.NotifyCanExecuteChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string Message { get; }

    public string OkText { get; }

    public string CancelText { get; }

    public bool ShowMessage { get; }

    public bool ShowInput { get; }

    public bool ShowForm { get; }

    public bool ShowChoices { get; }

    public bool ShowAccept { get; }

    public bool ShowCancel { get; }

    public DialogForm? Form { get; }

    public IReadOnlyList<DialogChoiceButton> Choices { get; }

    public ICommand AcceptCommand => _accept;

    public ICommand CancelCommand { get; }

    public string Input
    {
        get => _input;
        set => SetInput(value);
    }

    public static DialogWindowModel Create(DialogRequest request, ILocalizer localizer, IFolderPicker folders)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(folders);
        var model = new DialogWindowModel(request, localizer);
        model.WireFolders(folders, localizer.Get("dialog.browse"));
        return model;
    }

    public void Attach(Action<DialogResult> finish)
    {
        ArgumentNullException.ThrowIfNull(finish);
        _finish = finish;
    }

    private void WireFolders(IFolderPicker folders, string browseText)
    {
        if (Form is null)
        {
            return;
        }

        foreach (var field in Form.Fields)
        {
            if (field is FolderDialogField folder)
            {
                folder.AttachBrowse(browseText, () => BrowseAsync(folders, folder));
            }
        }
    }

    private static async Task BrowseAsync(IFolderPicker folders, FolderDialogField folder)
    {
        var start = string.IsNullOrWhiteSpace(folder.Path) ? null : folder.Path;
        var chosen = await folders.PickAsync(start);
        if (chosen is not null)
        {
            folder.Path = chosen;
        }
    }

    private IReadOnlyList<DialogChoiceButton> BuildChoices(DialogRequest request)
    {
        if (request.Kind != DialogKind.Choice)
        {
            return [];
        }

        var buttons = new List<DialogChoiceButton>(request.Choices.Count);
        for (var i = 0; i < request.Choices.Count; i++)
        {
            var index = i;
            buttons.Add(new DialogChoiceButton(request.Choices[i], new DialogCommand(() => Finish(DialogResult.Picked(index)))));
        }

        return buttons;
    }

    private void Accept()
    {
        if (ShowInput)
        {
            Finish(DialogResult.Ok(Input));
            return;
        }

        Finish(DialogResult.Ok());
    }

    private void Cancel() => Finish(DialogResult.Cancel());

    private bool CanAccept() => ShowAccept && (Form is null || Form.CanAccept);

    private void Finish(DialogResult result)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        var finish = _finish ?? throw new InvalidOperationException("The dialog window is not open.");
        finish(result);
    }

    private void SetInput(string value)
    {
        if (_input == value)
        {
            return;
        }

        _input = value;
        OnPropertyChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One button in a choice dialog. The label is already localized by the caller.</summary>
public sealed class DialogChoiceButton
{
    public DialogChoiceButton(string label, ICommand pick)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentNullException.ThrowIfNull(pick);
        Label = label;
        PickCommand = pick;
    }

    public string Label { get; }

    public ICommand PickCommand { get; }
}
