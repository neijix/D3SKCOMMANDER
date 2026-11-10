using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Settings;

/// <summary>The localized captions of the settings window, read once when it opens.</summary>
public sealed class SettingsLabels
{
    public SettingsLabels(ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Language = text.Get("language");
        FontSize = text.Get("font_size");
        ShowHidden = text.Get("settings.hidden");
        UseTrash = text.Get("use_trash");
        ConfirmDelete = text.Get("confirm_delete");
        ConfirmTrashFallback = text.Get("confirm_trash_fallback");
        LogOperations = text.Get("log_ops");
        AutoFolderSizes = text.Get("auto_folder_sizes");
        IndexTitle = text.Get("quickfind.settings.title");
        IndexRoots = text.Get("quickfind.settings.roots");
        IndexExcludes = text.Get("quickfind.settings.excludes");
        IndexHours = text.Get("quickfind.settings.hours");
        PreviewStart = text.Get("quickview.settings.start");
        Editor = text.Get("editor");
        Terminal = text.Get("terminal");
        CheckUpdates = text.Get("check_updates");
        UserCommands = text.Get("settings.user_commands");
        Add = text.Get("settings.add");
        Edit = text.Get("settings.edit");
        Remove = text.Get("settings.remove");
        Folder = text.Get("settings.folder");
        ChangeFolder = text.Get("settings.folder_change");
        Ok = text.Get("ok");
        Apply = text.Get("apply");
        Close = text.Get("cancel");
        LanguageEnglish = text.Get("settings.language_en");
        LanguageFrench = text.Get("settings.language_fr");
    }

    public string Language { get; }

    public string FontSize { get; }

    public string ShowHidden { get; }

    public string UseTrash { get; }

    public string ConfirmDelete { get; }

    public string ConfirmTrashFallback { get; }

    public string LogOperations { get; }

    public string AutoFolderSizes { get; }

    public string IndexTitle { get; }

    public string IndexRoots { get; }

    public string IndexExcludes { get; }

    public string IndexHours { get; }

    public string PreviewStart { get; }

    public string Editor { get; }

    public string Terminal { get; }

    public string CheckUpdates { get; }

    public string UserCommands { get; }

    public string Add { get; }

    public string Edit { get; }

    public string Remove { get; }

    public string Folder { get; }

    public string ChangeFolder { get; }

    public string Ok { get; }

    public string Apply { get; }

    public string Close { get; }

    public string LanguageEnglish { get; }

    public string LanguageFrench { get; }
}
