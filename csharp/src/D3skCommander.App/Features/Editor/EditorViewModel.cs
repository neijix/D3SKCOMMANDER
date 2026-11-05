using System.Globalization;
using System.Text;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Common;
using D3skCommander.Core.Localization;
using D3skCommander.Core.TextDocument;
using CoreDocument = D3skCommander.Core.TextDocument.TextDocument;
using EditBuffer = AvaloniaEdit.Document.TextDocument;

namespace D3skCommander.App.Features.Editor;

/// <summary>
/// The editor tool window's state over Core's text document. A file opens off the UI thread: the
/// name and a busy state show at once, then the text or its typed error. AvaloniaEdit's buffer
/// feeds <c>TextDocument.Text</c>, so a keystroke marks the document dirty.
/// </summary>
public sealed class EditorViewModel : ObservableObject, IShellWidget, ICloseGuard
{
    private readonly ILocalizer _text;
    private readonly IDialogService _dialogs;
    private readonly Func<Task> _refreshPanels;
    private readonly Func<string, CoreDocument> _open;
    private CoreDocument? _document;
    private int _ticket;
    private bool _syncing;
    private bool _isBusy;
    private string _name = "";
    private string _error = "";
    private string _notice = "";
    private string _findText = "";
    private string _replaceText = "";
    private bool _matchCase;
    private Range? _selection;

    public EditorViewModel(ILocalizer text, IDialogService dialogs, Func<Task> refreshPanels)
        : this(text, dialogs, refreshPanels, CoreDocument.Open)
    {
    }

    public EditorViewModel(
        ILocalizer text,
        IDialogService dialogs,
        Func<Task> refreshPanels,
        Func<string, CoreDocument> open)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(refreshPanels);
        ArgumentNullException.ThrowIfNull(open);
        _text = text;
        _dialogs = dialogs;
        _refreshPanels = refreshPanels;
        _open = open;
        Buffer.TextChanged += (_, _) => OnBufferChanged();
    }

    /// <summary>Asks the view to select a range of the buffer: UTF-16 start and length.</summary>
    public event Action<int, int>? SelectionRequested;

    public string? Name => "Editor";

    /// <summary>The buffer AvaloniaEdit edits.</summary>
    public EditBuffer Buffer { get; } = new();

    public string FileName
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanEdit));
            }
        }
    }

    /// <summary>The typed error as localized text plus the path; empty when the file opened.</summary>
    public string ErrorMessage
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(CanEdit));
            }
        }
    }

    public bool HasError => _error.Length > 0;

    public bool CanEdit => !IsBusy && !HasError && _document is not null;

    public bool Dirty => _document?.Dirty ?? false;

    /// <summary>Line ending, BOM and the dirty mark, for the status line.</summary>
    public string InfoText
    {
        get
        {
            if (_document is not { } document)
            {
                return "";
            }

            var parts = new List<string> { LineEndings.Label(document.LineEnding) };
            if (document.Bom)
            {
                parts.Add(_text.Get("editor.bom"));
            }

            if (document.Dirty)
            {
                parts.Add(_text.Get("editor.dirty"));
            }

            return string.Join(" | ", parts);
        }
    }

    public string LossyText => _document is { Lossy: true } ? _text.Get("editor.lossy") : "";

    public string Notice
    {
        get => _notice;
        private set => SetProperty(ref _notice, value);
    }

    public string FindText
    {
        get => _findText;
        set => SetProperty(ref _findText, value ?? "");
    }

    public string ReplaceText
    {
        get => _replaceText;
        set => SetProperty(ref _replaceText, value ?? "");
    }

    public bool MatchCase
    {
        get => _matchCase;
        set => SetProperty(ref _matchCase, value);
    }

    public string Path => _document?.FilePath ?? "";

    public string SaveLabel => _text.Get("editor.save");

    public string FindLabel => _text.Get("editor.find");

    public string ReplaceLabel => _text.Get("editor.replace");

    public string NextLabel => _text.Get("editor.find_next");

    public string ReplaceOneLabel => _text.Get("editor.replace_one");

    public string ReplaceAllLabel => _text.Get("editor.replace_all");

    public string MatchCaseLabel => _text.Get("editor.match_case");

    public void Draw()
    {
    }

    public Control CreateContent() => new EditorView { DataContext = this };

    /// <summary>Shows <paramref name="path"/>: the name and busy state at once, the text when the load ends.</summary>
    public async Task OpenAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var ticket = ++_ticket;
        FileName = System.IO.Path.GetFileName(path);
        IsBusy = true;
        ErrorMessage = "";
        Notice = _text.Get("editor.opening").Replace("{name}", FileName, StringComparison.Ordinal);
        var document = await Task.Run(() => _open(path)).ConfigureAwait(true);
        if (ticket == _ticket)
        {
            UiThread.Invoke(() => Show(document));
        }
    }

    /// <summary>
    /// A clean buffer may go. A dirty one asks Save / Don't save / Cancel: Save that succeeds and
    /// Don't save answer yes; a failed Save (its error is shown) and Cancel answer no.
    /// </summary>
    public Task<bool> CanCloseAsync() => Dirty && !IsBusy ? AskUnsavedAsync() : Task.FromResult(true);

    private async Task<bool> AskUnsavedAsync()
    {
        var message = _text.Get("editor.unsaved").Replace("{name}", FileName, StringComparison.Ordinal);
        string[] choices = [_text.Get("editor.save"), _text.Get("editor.dont_save"), _text.Get("cancel")];
        var answer = await _dialogs.ChooseAsync(_text.Get("cmd.edit"), message, choices).ConfigureAwait(true);
        return answer switch
        {
            0 => await SaveAsync().ConfigureAwait(true),
            1 => true,
            _ => false,
        };
    }

    /// <summary>Writes the buffer. Success refreshes the panels; a failure shows its typed error and keeps the buffer.</summary>
    public async Task<bool> SaveAsync()
    {
        if (_document is not { } document || IsBusy)
        {
            return false;
        }

        var saved = await Task.Run(document.Save).ConfigureAwait(true);
        RaiseState();
        if (saved)
        {
            Notice = _text.Get("editor.saved");
            await _refreshPanels().ConfigureAwait(true);
            return true;
        }

        Notice = _text.Get("editor.save_failed");
        var error = document.SaveError;
        var detail = error is null ? "" : _text.Get(ErrorText.Key(error.Kind)) + ": " + (error.Path ?? document.FilePath);
        await _dialogs.ShowMessageAsync(_text.Get("cmd.edit"), detail).ConfigureAwait(true);
        return false;
    }

    /// <summary>The view reports the caret selection as UTF-16 offsets.</summary>
    public void SetSelection(int start, int length)
    {
        var text = Buffer.Text;
        start = Math.Clamp(start, 0, text.Length);
        var end = Math.Clamp(start + length, start, text.Length);
        _selection = new Range(Encoding.UTF8.GetByteCount(text.AsSpan(0, start)), Encoding.UTF8.GetByteCount(text.AsSpan(0, end)));
    }

    public void FindNext() => Run(document => document.FindNext(), Hit);

    public void ReplaceOne() => Run(document => document.ReplaceOne(), Hit);

    public void ReplaceAll() => Run(document => document.ReplaceEverywhere(), document =>
        document.Notice == TextNotice.Replaced
            ? _text.Get("editor.replaced").Replace("{n}", document.NoticeCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : "");

    private string Hit(CoreDocument document) =>
        document.PendingSelect is { } range && range.Start.Value != range.End.Value ? "" : _text.Get("editor.not_found");

    private void Run(Action<CoreDocument> action, Func<CoreDocument, string> notice)
    {
        if (_document is not { } document || !CanEdit)
        {
            return;
        }

        document.Find = _findText;
        document.Replace = _replaceText;
        document.MatchCase = _matchCase;
        document.Selection = _selection;
        action(document);
        Notice = notice(document);
        if (Buffer.Text != document.Text)
        {
            Sync(() => Buffer.Text = document.Text);
        }

        RaiseState();
        var pending = document.PendingSelect;
        if (document.ApplyPendingSelect() && pending is { } range)
        {
            _selection = document.Selection;
            var (start, end) = (ToChar(range.Start.Value), ToChar(range.End.Value));
            SelectionRequested?.Invoke(start, end - start);
        }
    }

    private int ToChar(int byteOffset)
    {
        var bytes = Encoding.UTF8.GetBytes(Buffer.Text);
        return Encoding.UTF8.GetCharCount(bytes, 0, Math.Min(byteOffset, bytes.Length));
    }

    private void Show(CoreDocument document)
    {
        _document = document;
        IsBusy = false;
        Notice = "";
        _selection = null;
        if (document.Refused is { } refused)
        {
            ErrorMessage = _text.Get(ErrorText.Key(refused.Kind)) + ": " + (refused.Path ?? document.FilePath);
            Sync(() => Buffer.Text = "");
        }
        else
        {
            ErrorMessage = "";
            Sync(() => Buffer.Text = document.Text);
        }

        RaiseState();
    }

    private void OnBufferChanged()
    {
        if (_syncing || _document is not { } document)
        {
            return;
        }

        document.Text = Buffer.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        Notice = "";
        RaiseState();
    }

    private void Sync(Action change)
    {
        _syncing = true;
        try
        {
            change();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(Dirty));
        OnPropertyChanged(nameof(InfoText));
        OnPropertyChanged(nameof(LossyText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Path));
    }
}
