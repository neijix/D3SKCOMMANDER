using D3skCommander.Core.Common;
using D3skCommander.Core.Dbf;

namespace D3skCommander.Core.Viewer;

/// <summary>
/// Lister content (F3): text with wrap, find and a cursor, hex, image detection, and DBF or
/// catalog tables. A file past <see cref="ViewerLimits.MaxRead"/> is truncated. The window
/// around this object is drawn by the app.
/// </summary>
public sealed partial class FileViewer
{
    private string _find = "";
    private byte[] _bytes;
    private bool _bytesLoaded;
    private string? _text;
    private List<LineSpan> _lines;
    private (int Columns, List<VisualRow> Rows)? _wrapped;

    private FileViewer(string path)
    {
        Path = path;
        Size = LengthOf(path);
        _bytes = [];
        _lines = [];
        Modes = [ViewerMode.Text, ViewerMode.Hex];
        Mode = ViewerMode.Text;
        Wrap = true;
        ImageFit = true;
        FilteredRows = [];
    }

    public string Path { get; }

    public ulong Size { get; }

    public bool Truncated { get; private set; }

    public ViewerMode Mode { get; private set; }

    public IReadOnlyList<ViewerMode> Modes { get; private set; }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public OperationError? Error { get; private set; }

    public ViewerEncoding Encoding { get; private set; }

    public string EncodingName => ViewerEncodingNames.Name(Encoding);

    /// <summary>Decoded text after <see cref="Prepare"/> in text mode. Null until then, and for a pure hex open.</summary>
    public string? Text => _text;

    public IReadOnlyList<LineSpan> Lines => _lines;

    public bool Wrap { get; set; }

    public bool ImageFit { get; set; }

    public bool MatchCase { get; set; }

    /// <summary>Highlighted text line (v0.1 <c>text_match</c>). Find moves it.</summary>
    public int? TextCursor { get; private set; }

    /// <summary>Byte offset of the hex hit (v0.1 <c>hex_match</c>).</summary>
    public int? HexCursor { get; private set; }

    /// <summary>Display row to bring into view. <see cref="TakeScrollRow"/> consumes it.</summary>
    public int? ScrollRow { get; private set; }

    public ViewerNotice Notice { get; private set; }

    public ViewerTable? Table { get; private set; }

    public string Filter { get; private set; } = "";

    public IReadOnlyList<int> FilteredRows { get; private set; }

    public string FindText
    {
        get => _find;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _find = value;
            Notice = ViewerNotice.None;
        }
    }

    /// <summary>Reads at most 16 MiB. A missing file is <see cref="Error"/>, never a throw.</summary>
    public static FileViewer Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var viewer = new FileViewer(path);
        viewer.Choose();
        return viewer;
    }

    public void SetMode(ViewerMode mode)
    {
        Mode = mode;
        Prepare();
    }

    /// <summary>
    /// Switches to text or hex for the paged reader (<see cref="ViewerPages"/>). Nothing is read
    /// beyond the byte order mark; the pages come from the file on demand.
    /// </summary>
    public void UsePagedMode(ViewerMode mode)
    {
        if (mode != ViewerMode.Text && mode != ViewerMode.Hex)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Mode = mode;
        if (!_bytesLoaded)
        {
            Encoding = ViewerPages.DetectEncoding(Path);
        }
    }

    /// <summary>Loads whatever the current mode needs. Text and hex read the file; a table does not.</summary>
    public void Prepare()
    {
        if (Mode != ViewerMode.Text && Mode != ViewerMode.Hex)
        {
            return;
        }

        LoadBytes();
        if (Error is null && Mode == ViewerMode.Text)
        {
            EnsureText();
        }
    }

    /// <summary>Next (<paramref name="forward"/>) or previous hit. An empty query does nothing.</summary>
    public bool Find(bool forward)
    {
        Notice = ViewerNotice.None;
        if (FindText.Length == 0)
        {
            return false;
        }

        Prepare();
        if (Mode == ViewerMode.Text)
        {
            return Finish(FindInText(forward));
        }

        if (Mode == ViewerMode.Hex)
        {
            return Finish(FindInHex(forward));
        }

        return false;
    }

    public int? TakeScrollRow()
    {
        var row = ScrollRow;
        ScrollRow = null;
        return row;
    }

    public void SetFilter(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Filter = filter;
        FilteredRows = Table is null ? [] : Table.Filtered(filter);
    }

    private void Choose()
    {
        if (ViewerFiles.IsImage(Path))
        {
            Use(ViewerMode.Image, [ViewerMode.Image, ViewerMode.Hex]);
            return;
        }

        if (ViewerFiles.IsDbf(Path))
        {
            OpenDbf();
            return;
        }

        var catalog = ViewerTables.TryCatalog(Path);
        if (catalog is not null)
        {
            UseTable(catalog, [ViewerMode.Table, ViewerMode.Text]);
            return;
        }

        LoadBytes();
        if (Error is null && ViewerText.IsBinary(_bytes))
        {
            Mode = ViewerMode.Hex;
        }

        if (Error is null && Mode == ViewerMode.Text)
        {
            EnsureText();
        }
    }

    private void OpenDbf()
    {
        var read = DbfReader.Read(Path, ViewerLimits.MaxDbfRecords);
        if (!read.IsSuccess)
        {
            Error = read.Error;
            return;
        }

        UseTable(ViewerTables.FromDbf(read.Value), [ViewerMode.Table, ViewerMode.Hex]);
    }

    private void Use(ViewerMode mode, ViewerMode[] modes)
    {
        Mode = mode;
        Modes = modes;
    }

    private void UseTable(ViewerTable table, ViewerMode[] modes)
    {
        Table = table;
        Filter = "";
        FilteredRows = table.Filtered("");
        Use(ViewerMode.Table, modes);
    }

    private void LoadBytes()
    {
        if (_bytesLoaded)
        {
            return;
        }

        _bytesLoaded = true;
        var read = ViewerRead.Load(Path);
        if (read.Error is not null)
        {
            Error = read.Error;
            return;
        }

        _bytes = read.Bytes;
        Truncated = read.Truncated;
        Encoding = ViewerText.DetectEncoding(_bytes).Encoding;
    }

    private void EnsureText()
    {
        if (_text is not null)
        {
            return;
        }

        var decoded = ViewerText.Decode(_bytes);
        _text = decoded.Text;
        Encoding = decoded.Encoding;
        _lines = ViewerText.LineRanges(_text);
        _wrapped = null;
    }

    private bool Finish(bool found)
    {
        if (!found)
        {
            Notice = ViewerNotice.NotFound;
        }

        return found;
    }

    private bool FindInText(bool forward)
    {
        var text = _text;
        if (text is null || _lines.Count == 0)
        {
            return false;
        }

        var from = TextFrom(forward, _lines.Count);
        var hit = ViewerText.FindLine(text, _lines, FindText, MatchCase, from, forward);
        if (hit is int line)
        {
            TextCursor = line;
            ScrollRow = ScrollTarget(line);
            return true;
        }

        return false;
    }

    private bool FindInHex(bool forward)
    {
        var needle = System.Text.Encoding.UTF8.GetBytes(FindText);
        var hit = ViewerText.FindBytes(_bytes, needle, MatchCase, HexFrom(forward), forward);
        if (hit is int offset)
        {
            HexCursor = offset;
            ScrollRow = offset / ViewerLimits.HexWidth;
            return true;
        }

        return false;
    }

    private int TextFrom(bool forward, int count)
    {
        if (TextCursor is int line)
        {
            return forward ? line + 1 : line + count - 1;
        }

        return forward ? 0 : count - 1;
    }

    private int HexFrom(bool forward)
    {
        if (HexCursor is not int offset)
        {
            return forward ? 0 : int.MaxValue;
        }

        if (!forward)
        {
            return offset == 0 ? int.MaxValue : offset - 1;
        }

        return offset + 1;
    }

    private int ScrollTarget(int line)
    {
        if (!Wrap || _wrapped is null)
        {
            return line;
        }

        var rows = _wrapped.Value.Rows;
        var index = 0;
        while (index < rows.Count && rows[index].Line < line)
        {
            index++;
        }

        return index;
    }

    private static ulong LengthOf(string path)
    {
        try
        {
            var length = new FileInfo(path).Length;
            if (length < 0)
            {
                return 0;
            }

            return (ulong)length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
