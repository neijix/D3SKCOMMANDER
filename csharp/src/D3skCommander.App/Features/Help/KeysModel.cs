using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Help;

/// <summary>Every command with its localized label and gestures, narrowed by a filter text.</summary>
public sealed partial class KeysModel : ObservableObject
{
    private readonly List<KeyRow> _all = [];
    private readonly string _none;
    [ObservableProperty]
    private string _filter = "";

    public KeysModel(ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _none = text.Get("help.keys.none");
        foreach (var command in CommandCatalog.All)
        {
            var gestures = CommandCatalog.Gestures(command).Select(gesture => gesture.Format());
            _all.Add(new KeyRow(command.ToString(), text.Get(CommandCatalog.LabelKey(command)), string.Join(", ", gestures)));
        }

        Rows = new ObservableCollection<KeyRow>(_all);
    }

    /// <summary>The rows that match <see cref="Filter"/>, in command order.</summary>
    public ObservableCollection<KeyRow> Rows { get; }

    /// <summary>What a row without a gesture shows.</summary>
    public string NoShortcut => _none;

    partial void OnFilterChanged(string value)
    {
        var needle = value.Trim();
        Rows.Clear();
        foreach (var row in _all)
        {
            if (Matches(row, needle))
            {
                Rows.Add(row);
            }
        }
    }

    private static bool Matches(KeyRow row, string needle)
    {
        if (needle.Length == 0)
        {
            return true;
        }

        return row.Label.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || row.Shortcuts.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || row.Command.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
