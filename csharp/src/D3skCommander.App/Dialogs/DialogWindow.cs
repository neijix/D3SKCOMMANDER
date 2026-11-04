using D3skCommander.App.Shell;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Dialogs;

/// <summary>The owned modal the shell draws for one <see cref="DialogWindowModel"/>.</summary>
public sealed class DialogWindow : IOwnedWindow
{
    public DialogWindow(MainWindow owner, DialogWindowModel model)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(model);
        Owner = owner;
        Model = model;
        if (model.Form is not null)
        {
            Form = new FormDialogView(model.Form);
        }
    }

    public MainWindow Owner { get; }

    public DialogWindowModel Model { get; }

    public FormDialogView? Form { get; }

    public string Title => Model.Title;

    public object? Parent => null;

    public bool IsVisible { get; private set; }

    public DialogButton AcceptButton => new(Model.OkText, Model.AcceptCommand);

    public DialogButton CancelButton => new(Model.CancelText, Model.CancelCommand);

    public IEnumerable<string> Labels => Form?.Notes ?? [];

    public void Show()
    {
        IsVisible = true;
        Owner.Attach(this);
    }

    public void Close()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        Owner.Detach(this);
    }

    public void Draw()
    {
        if (!IsVisible)
        {
            return;
        }

        ImGui.OpenPopup(Title);
        var open = true;
        if (!ImGui.BeginPopupModal(Title, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!open)
            {
                Cancel();
            }

            return;
        }

        DrawBody();
        DrawButtons();
        ImGui.EndPopup();
        if (!open)
        {
            Cancel();
        }
    }

    private void DrawBody()
    {
        if (Model.ShowMessage)
        {
            ImGui.TextWrapped(Model.Message);
        }

        if (Model.ShowInput)
        {
            var input = Model.Input;
            if (ImGui.InputText("##dialog-input", ref input, 1024))
            {
                Model.Input = input;
            }
        }

        if (Model.ShowChoices)
        {
            foreach (var choice in Model.Choices)
            {
                if (ImGui.Button(choice.Label))
                {
                    choice.PickCommand.Execute(null);
                }

                ImGui.SameLine();
            }
        }

        Form?.Draw();
    }

    private void DrawButtons()
    {
        if (Model.ShowAccept && ImGui.Button(Model.OkText) && Model.AcceptCommand.CanExecute(null))
        {
            Model.AcceptCommand.Execute(null);
        }

        if (!Model.ShowCancel)
        {
            return;
        }

        if (Model.ShowAccept)
        {
            ImGui.SameLine();
        }

        if (ImGui.Button(Model.CancelText))
        {
            Cancel();
        }
    }

    private void Cancel()
    {
        if (Model.CancelCommand.CanExecute(null))
        {
            Model.CancelCommand.Execute(null);
        }
    }
}
