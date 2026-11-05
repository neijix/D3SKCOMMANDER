using D3skCommander.Core.Archives;
using D3skCommander.Core.Common;
using D3skCommander.Core.Localization;

namespace D3skCommander.Core.Providers;

/// <summary>
/// How a copy or move touches an archive. Copying a member out extracts it. Moving a member out
/// is refused. Copying into an archive is refused: the result carries <see cref="UsePackCode"/>,
/// which the UI translates, plus the English and French sentences (<see cref="ILocalizer.Text"/>).
/// </summary>
public static class ArchiveTransfers
{
    public const string UsePackCode = "archive.use_pack";

    public const string UsePackEnglish = "Copying into an archive is not supported. Use Pack.";

    public const string UsePackFrench = "La copie dans une archive n'est pas prise en charge. Utilisez Compresser.";

    public const string MoveOutCode = "archive.move_out";

    public const string MoveOutEnglish = "Moving a file out of an archive is not supported.";

    public const string MoveOutFrench = "Déplacer un fichier hors d'une archive n'est pas pris en charge.";

    /// <summary>Extracts the member <paramref name="path"/> (or the whole archive at its root) into <paramref name="destination"/>.</summary>
    public static ArchiveTransfer CopyOut(string path, string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        var place = ArchiveFileSystemProvider.Locate(path);
        if (place.Error is not null || place.Archive is null)
        {
            return ArchiveTransfer.Failed(place.Error ?? OperationError.NotFound(path));
        }

        IReadOnlyList<string>? members = place.Inner.Length == 0 ? null : new[] { place.Inner };
        return Extract(place.Archive, members, destination, cancellationToken);
    }

    /// <summary>Extracts <paramref name="members"/> (<c>null</c> = everything). Unsafe names fail as the archive core reports them.</summary>
    public static ArchiveTransfer Extract(
        string archive,
        IReadOnlyList<string>? members,
        string destination,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(archive);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        try
        {
            var extracted = ArchiveIo.Extract(archive, members, destination, null, cancellationToken);
            if (!extracted.IsSuccess)
            {
                return ArchiveTransfer.Failed(extracted.Error ?? OperationError.NotFound(archive));
            }

            return ArchiveTransfer.Extracted(extracted.Value);
        }
        catch (OperationCanceledException)
        {
            return ArchiveTransfer.Failed(OperationError.Cancelled(archive));
        }
    }

    /// <summary>A move out of an archive is refused. Nothing is extracted.</summary>
    public static ArchiveTransfer MoveOut(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ArchiveTransfer.Refused(MoveOutCode, MoveOutEnglish, MoveOutFrench, path);
    }

    /// <summary>A copy into an archive is refused. Pack is how files get in.</summary>
    public static ArchiveTransfer CopyIn(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ArchiveTransfer.Refused(UsePackCode, UsePackEnglish, UsePackFrench, path);
    }
}

/// <summary>An extract, or a refusal. <see cref="Code"/> is what the UI translates; English and French travel with it.</summary>
public sealed class ArchiveTransfer
{
    private ArchiveTransfer(
        bool extracted,
        int count,
        OperationError? error,
        string? code,
        string? english,
        string? french)
    {
        IsExtract = extracted;
        Count = count;
        Error = error;
        Code = code;
        English = english;
        French = french;
    }

    public bool IsExtract { get; }

    public int Count { get; }

    public OperationError? Error { get; }

    /// <summary>Catalog code for a refusal. Null when the transfer extracted.</summary>
    public string? Code { get; }

    public string? English { get; }

    public string? French { get; }

    /// <summary>The refusal in the localizer's language. Same pair <see cref="ILocalizer.Text"/> already takes.</summary>
    public string Text(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        if (English is null || French is null)
        {
            throw new InvalidOperationException("This transfer has no bilingual text.");
        }

        return localizer.Text(English, French);
    }

    public static ArchiveTransfer Extracted(int count) => new(true, count, null, null, null, null);

    public static ArchiveTransfer Failed(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ArchiveTransfer(false, 0, error, null, null, null);
    }

    public static ArchiveTransfer Refused(string code, string english, string french, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(english);
        ArgumentException.ThrowIfNullOrEmpty(french);
        return new ArchiveTransfer(false, 0, OperationError.Unsupported(code, path), code, english, french);
    }
}
