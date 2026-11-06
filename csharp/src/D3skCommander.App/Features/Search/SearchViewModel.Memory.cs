using System.Collections.ObjectModel;
using D3skCommander.Core.Search;

namespace D3skCommander.App.Features.Search;

/// <summary>The search form's memory: the history of its text fields and the saved templates.</summary>
public sealed partial class SearchViewModel
{
    public ObservableCollection<string> RootsHistory { get; } = [];

    public ObservableCollection<string> NameHistory { get; } = [];

    public ObservableCollection<string> TextHistory { get; } = [];

    public ObservableCollection<string> ExcludeFilesHistory { get; } = [];

    public ObservableCollection<string> ExcludeFoldersHistory { get; } = [];

    /// <summary>The saved template names, sorted.</summary>
    public IReadOnlyList<string> TemplateNames => _templates.Current.Names();

    /// <summary>Asks a name and saves the form under it; an existing name is replaced only after a confirmation.</summary>
    public async Task SaveTemplateAsync()
    {
        var name = (await _dialogs.AskTextAsync(
            _text.Get("search.template.save"),
            _text.Get("search.template.name")).ConfigureAwait(true))?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (_templates.Current.Contains(name)
            && !await _dialogs.ConfirmAsync(
                _text.Get("search.template.save"),
                _text.Get("search.template.replace").Replace("{name}", name, StringComparison.Ordinal)).ConfigureAwait(true))
        {
            return;
        }

        _templates.Current.Save(name, ReadForm());
        _templates.Save(_templates.Current);
        OnPropertyChanged(nameof(TemplateNames));
    }

    /// <summary>Fills the form from a saved template; false when there is none of that name.</summary>
    public bool LoadTemplate(string name)
    {
        var form = _templates.Current.Load(name);
        if (form is null)
        {
            return false;
        }

        Roots = form.Roots;
        NameMask = form.Name;
        NameIsRegex = form.NameIsRegex;
        ContentText = form.Text;
        TextIsRegex = form.TextIsRegex;
        CaseSensitive = form.CaseSensitive;
        WholeWords = form.WholeWords;
        NotContaining = form.NotContaining;
        ExcludeFiles = form.ExcludeFiles;
        ExcludeFolders = form.ExcludeFolders;
        FollowLinks = form.FollowLinks;
        DepthLimited = form.DepthLimited;
        Depth = form.Depth;
        IncludeHidden = form.IncludeHidden;
        IncludeFiles = form.Files;
        IncludeFolders = form.Dirs;
        InArchives = form.InArchives;
        MinSize = form.MinSize;
        MaxSize = form.MaxSize;
        NewerDays = form.NewerDays;
        OlderDays = form.OlderDays;
        return true;
    }

    public void DeleteTemplate(string name)
    {
        if (_templates.Current.Delete(name))
        {
            _templates.Save(_templates.Current);
            OnPropertyChanged(nameof(TemplateNames));
        }
    }

    private void Remember(SearchForm form)
    {
        var history = _history.Current;
        history.Add(SearchHistory.Roots, form.Roots);
        history.Add(SearchHistory.Name, form.Name);
        history.Add(SearchHistory.Text, form.Text);
        history.Add(SearchHistory.ExcludeFiles, form.ExcludeFiles);
        history.Add(SearchHistory.ExcludeFolders, form.ExcludeFolders);
        _history.Save(history);
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        Fill(RootsHistory, SearchHistory.Roots);
        Fill(NameHistory, SearchHistory.Name);
        Fill(TextHistory, SearchHistory.Text);
        Fill(ExcludeFilesHistory, SearchHistory.ExcludeFiles);
        Fill(ExcludeFoldersHistory, SearchHistory.ExcludeFolders);
    }

    private void Fill(ObservableCollection<string> target, string field)
    {
        var values = _history.Current.Get(field);
        if (target.SequenceEqual(values))
        {
            return;
        }

        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
