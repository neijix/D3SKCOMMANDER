using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.App.Shell;
using D3skCommander.Core.Localization;
using D3skCommander.Core.OperationLog;

namespace D3skCommander.App.Features.Log;

/// <summary>The operations log window: the recorded lines, newest last, Refresh and Clear view.</summary>
public sealed partial class LogViewModel : ObservableObject, IShellWidget
{
    private readonly IOperationLog _log;
    private readonly ILocalizer _text;
    private int _hidden;
    [ObservableProperty]
    private string _notice = "";
    [ObservableProperty]
    private string _summary = "";

    public LogViewModel(IOperationLog log, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(text);
        _log = log;
        _text = text;
    }

    public string? Name => "Log";

    /// <summary>The lines shown, oldest first.</summary>
    public ObservableCollection<string> Lines { get; } = [];

    public bool HasNotice => Notice.Length > 0;

    public Control CreateContent() => new LogView(this, _text) { Name = Name };

    /// <summary>Re-reads the log. Lines hidden by <see cref="ClearView"/> stay hidden.</summary>
    public void Refresh()
    {
        var all = _log.ReadLines();
        if (all.Count < _hidden)
        {
            _hidden = 0;
        }

        Lines.Clear();
        for (var i = _hidden; i < all.Count; i++)
        {
            Lines.Add(all[i]);
        }

        Notice = _log.Enabled ? "" : _text.Get("log.off");
        Summary = Lines.Count == 0
            ? _text.Get("log.empty")
            : string.Format(CultureInfo.InvariantCulture, _text.Get("log.count"), Lines.Count);
    }

    /// <summary>Hides what is recorded so far; only lines added later show. The file is not touched.</summary>
    public void ClearView()
    {
        _hidden = _log.ReadLines().Count;
        Refresh();
    }

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));
}
