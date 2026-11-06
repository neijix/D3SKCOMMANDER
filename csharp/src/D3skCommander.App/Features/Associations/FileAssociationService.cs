using D3skCommander.App.Features.Launch;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Associations;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Associations;

/// <summary>Looks the rules up in the live preferences and starts their programs through the launch feature (its process runner, its failure message).</summary>
public sealed class FileAssociationService : IFileAssociations
{
    private readonly AppSettings _settings;
    private readonly LaunchCommands _launch;
    private readonly ILocalizer _text;

    public FileAssociationService(AppSettings settings, LaunchCommands launch, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(text);
        _settings = settings;
        _launch = launch;
        _text = text;
    }

    private IReadOnlyList<AssociationRule> Rules => _settings.Preferences.Current.Associations;

    public bool Has(AssociationKind kind, string path) =>
        Rules.Count > 0 && FileAssociations.Find(Rules, Path.GetFileName(path), kind) is not null;

    public Task? TryStart(AssociationKind kind, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Rules.Count > 0 && FileAssociations.Find(Rules, Path.GetFileName(path), kind) is { } match
            ? Start(match.Action, path)
            : null;
    }

    public IReadOnlyList<ShellAction> MenuFor(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var items = new List<ShellAction>();
        if (Rules.Count == 0)
        {
            return items;
        }

        var name = Path.GetFileName(path);
        AddKind(items, AssociationKind.Open, "assoc.open_with", name, path);
        AddKind(items, AssociationKind.View, "assoc.view_with", name, path);
        AddKind(items, AssociationKind.Edit, "assoc.edit_with", name, path);
        var extras = FileAssociations.ExtraActions(Rules, name);
        if (items.Count > 0 && extras.Count > 0)
        {
            items.Add(ShellAction.Rule);
        }

        foreach (var extra in extras)
        {
            var action = extra.Action;
            items.Add(new ShellAction(action.Name, () => _ = Start(action, path)));
        }

        return items;
    }

    private void AddKind(List<ShellAction> items, AssociationKind kind, string key, string name, string path)
    {
        if (FileAssociations.Find(Rules, name, kind) is { } match)
        {
            var action = match.Action;
            var header = _text.Get(key).Replace("{program}", action.ProgramName, StringComparison.Ordinal);
            items.Add(new ShellAction(header, () => _ = Start(action, path)));
        }
    }

    private Task Start(AssociationAction action, string path)
    {
        var start = FileAssociations.BuildStart(action, new AssociationContext(path));
        var folder = start.WorkingFolder.Length > 0 ? start.WorkingFolder : Environment.CurrentDirectory;
        var request = new ProcessRequest.Argv(start.Program, start.Arguments, folder, false, false);
        return _launch.RunAsync([request]);
    }
}
