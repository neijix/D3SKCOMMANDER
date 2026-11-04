using Hexa.NET.ImGui;

namespace D3skCommander.App.Dialogs;

/// <summary>The one template every form dialog is shown through.</summary>
public sealed class FormDialogView
{
    public FormDialogView(DialogForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        Form = form;
    }

    public DialogForm Form { get; }

    public IEnumerable<string> Notes => Form.Fields.OfType<NoteDialogField>().Select(note => note.Label);

    public IEnumerable<DialogButton> BrowseButtons =>
        Form.Fields.OfType<FolderDialogField>()
            .Where(folder => folder.BrowseCommand is not null)
            .Select(folder => new DialogButton(folder.BrowseText, folder.BrowseCommand!));

    public void Draw()
    {
        foreach (var field in Form.Fields)
        {
            DrawField(field);
        }
    }

    private static void DrawField(DialogField field)
    {
        switch (field)
        {
            case NoteDialogField note:
                ImGui.TextWrapped(note.Label);
                break;
            case TextDialogField text:
                DrawText(text);
                break;
            case FolderDialogField folder:
                DrawFolder(folder);
                break;
            case CheckDialogField check:
                DrawCheck(check);
                break;
            case ChoiceDialogField choice:
                DrawChoice(choice);
                break;
        }
    }

    private static void DrawText(TextDialogField field)
    {
        ImGui.TextUnformatted(field.Label);
        var value = field.Text;
        var flags = field.IsPassword ? ImGuiInputTextFlags.Password : ImGuiInputTextFlags.None;
        if (ImGui.InputText("##" + field.Label, ref value, 1024, flags))
        {
            field.Text = value;
        }
    }

    private static void DrawFolder(FolderDialogField field)
    {
        ImGui.TextUnformatted(field.Label);
        var value = field.Path;
        if (ImGui.InputText("##path" + field.Label, ref value, 1024))
        {
            field.Path = value;
        }

        if (field.BrowseCommand is not null)
        {
            ImGui.SameLine();
            if (ImGui.Button(field.BrowseText))
            {
                field.BrowseCommand.Execute(null);
            }
        }
    }

    private static void DrawCheck(CheckDialogField field)
    {
        var value = field.IsChecked;
        if (ImGui.Checkbox(field.Label, ref value))
        {
            field.IsChecked = value;
        }
    }

    private static void DrawChoice(ChoiceDialogField field)
    {
        ImGui.TextUnformatted(field.Label);
        var current = field.SelectedIndex >= 0 && field.SelectedIndex < field.Options.Count
            ? field.Options[field.SelectedIndex]
            : "";
        if (!ImGui.BeginCombo("##" + field.Label, current))
        {
            return;
        }

        for (var i = 0; i < field.Options.Count; i++)
        {
            if (ImGui.Selectable(field.Options[i], i == field.SelectedIndex))
            {
                field.SelectedIndex = i;
            }
        }

        ImGui.EndCombo();
    }
}
