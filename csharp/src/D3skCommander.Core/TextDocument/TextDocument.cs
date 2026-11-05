using D3skCommander.Core.Common;

namespace D3skCommander.Core.TextDocument;

/// <summary>
/// One file open in the built-in editor. The buffer is LF-only; save writes the line ending
/// and BOM the file was loaded with. A missing file starts empty and save creates it.
/// </summary>
public sealed partial class TextDocument
{
    /// <summary>Larger files are not loaded. v0.1 <c>MAX_EDIT_SIZE</c>.</summary>
    public const long MaxEditSize = 8L * 1024 * 1024;

    public const string TooBigMessage =
        "This file is larger than 8 MiB: it is not opened in the built-in editor.";

    private string _text = "";

    private TextDocument(string filePath)
    {
        FilePath = filePath;
        var name = Path.GetFileName(filePath);
        Name = name.Length == 0 ? filePath : name;
    }

    public string FilePath { get; }

    public string Name { get; }

    /// <summary>LF-only buffer. Assigning it marks the document dirty.</summary>
    public string Text
    {
        get => _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _text = value;
            Dirty = true;
            SetNotice(TextNotice.None, 0, false, "");
        }
    }

    public LineEnding LineEnding { get; private set; } = LineEnding.Lf;

    public bool Bom { get; private set; }

    /// <summary>The file held invalid UTF-8, replaced by U+FFFD.</summary>
    public bool Lossy { get; private set; }

    public bool Dirty { get; private set; }

    public bool TooBig { get; private set; }

    /// <summary>Why the file was not loaded. Null when it loaded or the path is missing.</summary>
    public OperationError? Refused { get; private set; }

    /// <summary>The error behind the last failed <see cref="Save"/>. Null before any save and after a successful one.</summary>
    public OperationError? SaveError { get; private set; }

    /// <summary>True matches case. False, the default, folds each letter.</summary>
    public bool MatchCase { get; set; }

    public string Find { get; set; } = "";

    public string Replace { get; set; } = "";

    /// <summary>The caret selection as UTF-8 byte offsets. The view keeps this in step with the editor.</summary>
    public Range? Selection { get; set; }

    /// <summary>Byte range to select on the next frame. <see cref="ApplyPendingSelect"/> consumes it.</summary>
    public Range? PendingSelect { get; private set; }

    public TextNotice Notice { get; private set; }

    public int NoticeCount { get; private set; }

    public bool StatusIsError { get; private set; }

    public string StatusDetail { get; private set; } = "";

    /// <summary>Loads <paramref name="filePath"/> when it exists. A missing file starts empty.</summary>
    public static TextDocument Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var document = new TextDocument(filePath);
        document.Load();
        return document;
    }

    /// <summary>Byte offset of the rune at <paramref name="charIndex"/>.</summary>
    public int CharToByte(int charIndex) => TextIndex.CharToByte(_text, charIndex);

    /// <summary>How many runes are in the prefix of <paramref name="byteIndex"/> bytes.</summary>
    public int ByteToChar(int byteIndex) => TextIndex.ByteToChar(_text, byteIndex);

    /// <summary>Writes the buffer atomically. The view reloads the panel when this returns true.</summary>
    public bool Save()
    {
        SaveError = null;
        if (TooBig || Refused is not null)
        {
            SaveError = Refused ?? OperationError.Other(TooBigMessage, FilePath);
            SetNotice(TextNotice.Failed, 0, true, Refused?.Message ?? TooBigMessage);
            return false;
        }

        var status = TextAtomicWrite.Write(FilePath, TextCodec.Encode(_text, LineEnding, Bom));
        if (!status.IsSuccess)
        {
            SaveError = status.Error;
            SetNotice(TextNotice.Failed, 0, true, status.Error?.Message ?? "error");
            return false;
        }

        Dirty = false;
        SetNotice(TextNotice.Saved, 0, false, "");
        return true;
    }

    private void Load()
    {
        try
        {
            _ = File.GetAttributes(FilePath);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (Exception ex) when (IsIo(ex))
        {
            Refused = OperationError.FromException(ex, FilePath);
            return;
        }

        try
        {
            ReadFile();
        }
        catch (FileNotFoundException)
        {
            // Gone between the attribute check and the read: same as missing.
        }
        catch (Exception ex) when (IsIo(ex))
        {
            Refused = OperationError.FromException(ex, FilePath);
        }
    }

    private void ReadFile()
    {
        var length = new FileInfo(FilePath).Length;
        if (length > MaxEditSize)
        {
            TooBig = true;
            Refused = OperationError.Other(TooBigMessage, FilePath);
            return;
        }

        var decoded = TextCodec.Decode(File.ReadAllBytes(FilePath));
        _text = decoded.Text;
        LineEnding = decoded.LineEnding;
        Bom = decoded.Bom;
        Lossy = decoded.Lossy;
    }

    private void SetNotice(TextNotice notice, int count, bool error, string detail)
    {
        Notice = notice;
        NoticeCount = count;
        StatusIsError = error;
        StatusDetail = detail;
    }

    private static bool IsIo(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException;
}
