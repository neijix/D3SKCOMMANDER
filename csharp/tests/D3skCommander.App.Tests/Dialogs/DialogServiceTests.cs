using D3skCommander.App;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Dialogs;

public class DialogServiceTests
{
    [Fact]
    public async Task OneModalAtATimeIsAwaitedAndAnsweredByTheFake()
    {
        var stop = TestContext.Current.CancellationToken;
        var dialogs = new FakeDialogService();
        var first = dialogs.ConfirmAsync("Delete", "Delete these files?", stop);
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Confirm, shown.Kind);
        Assert.False(first.IsCompleted);
        Assert.Equal(1, dialogs.ShownCount);

        var second = dialogs.ShowMessageAsync("Later", "After the first.", stop);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, dialogs.ShownCount);
        Assert.Equal(DialogKind.Confirm, dialogs.Current!.Kind);

        dialogs.Answer(DialogResult.Ok());
        Assert.True(await first);

        var next = await dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Message, next.Kind);
        Assert.False(second.IsCompleted);
        Assert.Equal(2, dialogs.ShownCount);
        dialogs.Answer(DialogResult.Ok());
        await second;
        Assert.Null(dialogs.Current);
    }

    [Fact]
    public async Task TextChoiceAndFormReturnTheFakeAnswer()
    {
        var stop = TestContext.Current.CancellationToken;
        var dialogs = new FakeDialogService();
        var text = dialogs.AskTextAsync("Rename", "Name", "old", stop);
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Text, shown.Kind);
        Assert.Equal("old", shown.InitialText);
        dialogs.Answer(DialogResult.Ok("new"));
        Assert.Equal("new", await text);

        var choice = dialogs.ChooseAsync("Conflict", "Exists", ["Overwrite", "Skip"], stop);
        await dialogs.WaitShownAsync();
        dialogs.Answer(DialogResult.Picked(1));
        Assert.Equal(1, await choice);

        var cancelled = dialogs.ChooseAsync("Conflict", "Exists", ["Overwrite"], stop);
        await dialogs.WaitShownAsync();
        dialogs.Answer(DialogResult.Cancel());
        Assert.Null(await cancelled);

        var name = new TextDialogField("Name", "old");
        var form = new DialogForm("Rename", [name]);
        var formTask = dialogs.ShowFormAsync(form, stop);
        var formShown = await dialogs.WaitShownAsync();
        Assert.Same(form, formShown.Form);
        name.Text = "renamed";
        dialogs.Answer(DialogResult.Ok());
        Assert.True(await formTask);
        Assert.Equal("renamed", name.Text);
    }

    [Fact]
    public async Task FakeAnswersTheFolderPicker()
    {
        var stop = TestContext.Current.CancellationToken;
        var picker = new FakeFolderPicker();
        var pending = picker.PickAsync("/start", stop);
        Assert.False(pending.IsCompleted);
        Assert.Equal("/start", picker.LastStart);
        picker.Answer("/chosen");
        Assert.Equal("/chosen", await pending);

        picker.Enqueue(null);
        Assert.Null(await picker.PickAsync("/other", stop));
        Assert.Equal(2, picker.Calls);
    }

    [Fact]
    public void FormAcceptFollowsTheFields()
    {
        var folder = new FolderDialogField("Target", "");
        var form = new DialogForm("Unpack", [folder], HasFolder);
        Assert.False(form.CanAccept);
        folder.Path = "/tmp";
        Assert.True(form.CanAccept);
    }

    [Fact]
    public void DialogStringsAreBilingual()
    {
        var localizer = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        Assert.Equal("Choose a folder", localizer.Get("dialog.choose_folder", Language.En));
        Assert.Equal("Choisir un dossier", localizer.Get("dialog.choose_folder", Language.Fr));
        Assert.Equal("Choose a folder…", localizer.Get("dialog.browse", Language.En));
        Assert.Equal("Choisir un dossier…", localizer.Get("dialog.browse", Language.Fr));
        Assert.NotEqual(localizer.Get("dialog.browse", Language.En), localizer.Get("dialog.browse", Language.Fr));
    }

    [Fact]
    public void ModuleRegistersDialogServiceAndFolderPicker()
    {
        using var provider = Program.BuildProvider(Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En));
        Assert.IsType<DialogService>(provider.GetRequiredService<IDialogService>());
        Assert.IsType<OsFolderPicker>(provider.GetRequiredService<IFolderPicker>());
        Assert.IsType<ImGuiDialogSurface>(provider.GetRequiredService<IDialogSurface>());
        Assert.IsType<DialogsModule>(provider.GetRequiredService<DialogsModule>());
    }

    [Fact]
    public async Task OwnedModalWindowsOpenOneAtATime()
    {
        using var host = ShellHost.Open(Language.Fr);
        var surface = new ImGuiDialogSurface(new FixedOwner(host.Window), host.Localizer, new FakeFolderPicker());
        var dialogs = new DialogService(surface);
        var stop = TestContext.Current.CancellationToken;
        var text = dialogs.AskTextAsync(host.Localizer.Get("dialog.choose_folder"), host.Localizer.Get("dialog.browse"), "dossier", stop);
        var first = await WaitForCurrent(surface);
        Assert.Same(host.Window, first.Owner);
        Assert.Equal("Choisir un dossier", first.Title);
        Assert.Equal("Annuler", first.CancelButton.Content);
        Assert.False(text.IsCompleted);

        var confirm = dialogs.ConfirmAsync("Confirmer", "Continuer", stop);
        Assert.Single(host.Window.OwnedWindows);
        Assert.False(confirm.IsCompleted);

        Accept(first);
        Assert.Equal("dossier", await text);
        var second = await WaitForCurrent(surface, first);
        Assert.NotSame(first, second);
        Cancel(second);
        Assert.False(await confirm);
        Assert.Empty(host.Window.OwnedWindows);
    }

    [Fact]
    public async Task FormDialogUsesTheSharedTemplate()
    {
        using var host = ShellHost.Open(Language.Fr);
        var picker = new FakeFolderPicker();
        picker.Enqueue("/picked");
        var folder = new FolderDialogField("Cible", "");
        var form = new DialogForm("Décompresser", FormFields(folder), FolderIsFilled);
        var surface = new ImGuiDialogSurface(new FixedOwner(host.Window), host.Localizer, picker);
        var dialogs = new DialogService(surface);
        var pending = dialogs.ShowFormAsync(form, TestContext.Current.CancellationToken);
        var window = await WaitForCurrent(surface);
        Assert.Same(host.Window, window.Owner);
        Assert.IsType<FormDialogView>(window.Form);
        Assert.Equal("Annuler", window.CancelButton.Content);
        Assert.Contains("deux fichiers", window.Labels);
        var browse = Assert.Single(window.Form!.BrowseButtons);
        Assert.Equal(host.Localizer.Get("dialog.browse"), browse.Content);
        Assert.False(window.AcceptButton.Command.CanExecute(null));
        browse.Command.Execute(null);
        Assert.Equal("/picked", folder.Path);
        Assert.True(window.AcceptButton.Command.CanExecute(null));
        Accept(window);
        Assert.True(await pending);
    }

    private static bool HasFolder(DialogForm form) =>
        Assert.IsType<FolderDialogField>(Assert.Single(form.Fields)).Path.Length > 0;

    private static bool FolderIsFilled(DialogForm form) =>
        form.Fields.OfType<FolderDialogField>().Single().Path.Length > 0;

    private static IReadOnlyList<DialogField> FormFields(FolderDialogField folder) =>
    [
        new NoteDialogField("deux fichiers"),
        new TextDialogField("Nom", "archive"),
        folder,
        new CheckDialogField("Une fois"),
        new ChoiceDialogField("Format", ["zip", "7z"]),
    ];

    private static void Accept(DialogWindow window)
    {
        Assert.True(window.AcceptButton.Command.CanExecute(null));
        window.AcceptButton.Command.Execute(null);
    }

    private static void Cancel(DialogWindow window) => window.CancelButton.Command.Execute(null);

    private static async Task<DialogWindow> WaitForCurrent(ImGuiDialogSurface surface, DialogWindow? previous = null)
    {
        for (var i = 0; i < 30; i++)
        {
            var current = surface.Current;
            if (current is { IsVisible: true } && !ReferenceEquals(current, previous))
            {
                return current;
            }

            await Task.Yield();
        }

        throw new TimeoutException("The next dialog did not open.");
    }

    private sealed class FixedOwner : IDialogOwner
    {
        public FixedOwner(MainWindow window) => Current = window;

        public MainWindow? Current { get; }
    }
}
