using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Sync;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Tests.Features.Sync;

/// <summary>Two temp folders: one file on each side only, one newer on the left, one equal.</summary>
internal sealed class SyncDirs : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("d3sk-sync-").FullName;

    public SyncDirs()
    {
        Left = Path.Combine(_root, "left");
        Right = Path.Combine(_root, "right");
        Directory.CreateDirectory(Left);
        Directory.CreateDirectory(Right);
        Text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(Left, "left.txt"), "l");
        File.WriteAllText(Path.Combine(Right, "right.txt"), "r");
        Write(Left, "newer.txt", "new!", old.AddDays(10));
        Write(Right, "newer.txt", "old", old);
        Write(Left, "same.txt", "same", old);
        Write(Right, "same.txt", "same", old);
    }

    public string Left { get; }

    public string Right { get; }

    public ILocalizer Text { get; }

    public SyncViewModel View(IJobService jobs, IFolderPicker? picker = null) =>
        new(Left, Right, jobs, Text, picker ?? new FakeFolderPicker(), null);

    public void Scan(SyncViewModel view)
    {
        view.Compare();
        Until(view, () => !view.IsBusy);
    }

    public void Until(SyncViewModel view, Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < end)
        {
            view.Refresh();
            if (ready())
            {
                return;
            }

            Thread.Sleep(5);
        }

        throw new TimeoutException("The sync job did not finish.");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private static void Write(string folder, string name, string content, DateTime modified)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, modified);
    }
}
