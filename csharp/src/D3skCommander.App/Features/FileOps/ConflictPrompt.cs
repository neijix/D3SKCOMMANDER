using D3skCommander.Core.FileOps;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.FileOps;

/// <summary>The overwrite question: both files with size and date, and the seven answers.</summary>
public static class ConflictPrompt
{
    public static string Body(ILocalizer text, ConflictQuestion question)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(question);
        return text.Get("dlg.source") + ": " + question.SourcePath + "\n    "
            + Detail(question.SourceSize, question.SourceModified) + "\n"
            + text.Get("dlg.existing") + ": " + question.TargetPath + "\n    "
            + Detail(question.TargetSize, question.TargetModified);
    }

    /// <summary>Button order is the <see cref="Map"/> index: 0 to 6.</summary>
    public static string[] Choices(ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return
        [
            text.Get("dlg.overwrite_btn"),
            text.Get("dlg.skip"),
            text.Get("cancel"),
            text.Get("dlg.overwrite_all"),
            text.Get("dlg.skip_all"),
            text.Get("dlg.overwrite_older"),
            text.Get("dlg.rename_new"),
        ];
    }

    public static ConflictChoice Map(int? index)
    {
        if (index == 0)
        {
            return ConflictChoice.Overwrite;
        }

        if (index == 1)
        {
            return ConflictChoice.Skip;
        }

        if (index == 3)
        {
            return ConflictChoice.OverwriteAll;
        }

        if (index == 4)
        {
            return ConflictChoice.SkipAll;
        }

        if (index == 5)
        {
            return ConflictChoice.OverwriteAllOlder;
        }

        return index == 6 ? ConflictChoice.RenameNew : ConflictChoice.Cancel;
    }

    private static string Detail(long size, DateTimeOffset? modified) =>
        LocalFileSystem.FormatSize(size < 0 ? 0 : (ulong)size) + "   " + LocalFileSystem.FormatTime(modified);
}
