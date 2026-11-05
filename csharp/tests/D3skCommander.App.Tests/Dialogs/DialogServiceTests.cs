using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
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
    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void FormAcceptFollowsTheFields()
    {
        var folder = new FolderDialogField("Target", "");
        var form = new DialogForm("Unpack", [folder], HasFolder);
        Assert.False(form.CanAccept);
        folder.Path = "/tmp";
        Assert.True(form.CanAccept);
    }

    [AvaloniaFact]
    public void DialogStringsAreBilingual()
    {
        var localizer = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        Assert.Equal("Choose a folder", localizer.Get("dialog.choose_folder", Language.En));
        Assert.Equal("Choisir un dossier", localizer.Get("dialog.choose_folder", Language.Fr));
        Assert.Equal("Choose a folder…", localizer.Get("dialog.browse", Language.En));
        Assert.Equal("Choisir un dossier…", localizer.Get("dialog.browse", Language.Fr));
        Assert.NotEqual(localizer.Get("dialog.browse", Language.En), localizer.Get("dialog.browse", Language.Fr));
    }

    [AvaloniaFact]
    public void ModuleRegistersDialogServiceAndFolderPicker()
    {
        using var provider = Program.BuildProvider(Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En));
        Assert.IsType<DialogService>(provider.GetRequiredService<IDialogService>());
        Assert.IsType<OsFolderPicker>(provider.GetRequiredService<IFolderPicker>());
        Assert.IsType<WindowDialogSurface>(provider.GetRequiredService<IDialogSurface>());
        Assert.IsType<DialogsModule>(provider.GetRequiredService<DialogsModule>());
    }

    [AvaloniaFact]
    public async Task OwnedModalWindowsOpenOneAtATime()
    {
        using var host = ShellHost.Open(Language.Fr);
        var surface = new WindowDialogSurface(new FixedOwner(host.Window), host.Localizer, new FakeFolderPicker());
        var dialogs = new DialogService(surface);
        var stop = TestContext.Current.CancellationToken;
        var text = dialogs.AskTextAsync(host.Localizer.Get("dialog.choose_folder"), host.Localizer.Get("dialog.browse"), "dossier", stop);
        var first = await WaitForDialog(host.Window);
        Assert.Equal("Choisir un dossier", first.Title);
        Assert.Equal("Annuler", ButtonNamed(first, "CancelButton").Content);
        Assert.False(text.IsCompleted);

        var confirm = dialogs.ConfirmAsync("Confirmer", "Continuer", stop);
        Assert.Single(Dialogs(host.Window));
        Assert.False(confirm.IsCompleted);

        Accept(first);
        Assert.Equal("dossier", await text);
        var second = await WaitForDialog(host.Window, first);
        Assert.NotSame(first, second);
        ButtonNamed(second, "CancelButton").Command!.Execute(null);
        Assert.False(await confirm);
        Assert.Empty(Dialogs(host.Window));
    }

    [AvaloniaFact]
    public async Task EnterAcceptsWhenAllowedAndEscapeCancels()
    {
        using var host = ShellHost.Open(Language.En);
        var dialogs = new DialogService(new WindowDialogSurface(new FixedOwner(host.Window), host.Localizer, new FakeFolderPicker()));
        var stop = TestContext.Current.CancellationToken;
        var text = dialogs.AskTextAsync("Rename", "Name", "old", stop);
        var window = await WaitForDialog(host.Window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal("old", await text);

        var confirm = dialogs.ConfirmAsync("Delete", "Sure?", stop);
        window = await WaitForDialog(host.Window, window);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(await confirm);
    }

    [AvaloniaFact]
    public async Task ChoiceDialogReturnsThePickedButton()
    {
        using var host = ShellHost.Open(Language.En);
        var dialogs = new DialogService(new WindowDialogSurface(new FixedOwner(host.Window), host.Localizer, new FakeFolderPicker()));
        var pending = dialogs.ChooseAsync("Conflict", "Exists", ["Overwrite", "Skip"], TestContext.Current.CancellationToken);
        var window = await WaitForDialog(host.Window);
        var skip = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Skip"));
        skip.Command!.Execute(null);
        Assert.Equal(1, await pending);
    }

    [AvaloniaFact]
    public async Task FormDialogUsesTheSharedTemplate()
    {
        using var host = ShellHost.Open(Language.Fr);
        var picker = new FakeFolderPicker();
        picker.Enqueue("/picked");
        var folder = new FolderDialogField("Cible", "");
        var form = new DialogForm("Décompresser", FormFields(folder), FolderIsFilled);
        var surface = new WindowDialogSurface(new FixedOwner(host.Window), host.Localizer, picker);
        var dialogs = new DialogService(surface);
        var pending = dialogs.ShowFormAsync(form, TestContext.Current.CancellationToken);
        var window = await WaitForDialog(host.Window);
        Assert.IsType<FormDialogView>(window.GetVisualDescendants().OfType<FormDialogView>().Single());
        Assert.Equal("Annuler", ButtonNamed(window, "CancelButton").Content);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "deux fichiers");
        var browse = window.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, host.Localizer.Get("dialog.browse")));
        var accept = ButtonNamed(window, "AcceptButton");
        Assert.False(accept.Command!.CanExecute(null));
        browse.Command!.Execute(null);
        Assert.Equal("/picked", folder.Path);
        Assert.True(accept.Command.CanExecute(null));
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
        var command = ButtonNamed(window, "AcceptButton").Command!;
        Assert.True(command.CanExecute(null));
        command.Execute(null);
    }

    private static Button ButtonNamed(DialogWindow window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static List<DialogWindow> Dialogs(MainWindow main) =>
        ((Window)main).OwnedWindows.OfType<DialogWindow>().ToList();

    private static async Task<DialogWindow> WaitForDialog(MainWindow main, DialogWindow? previous = null)
    {
        for (var i = 0; i < 50; i++)
        {
            var current = Dialogs(main).FirstOrDefault(window => !ReferenceEquals(window, previous) && window.IsVisible);
            if (current is not null)
            {
                return current;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The next dialog did not open.");
    }

    private sealed class FixedOwner : IDialogOwner
    {
        public FixedOwner(MainWindow window) => Current = window;

        public MainWindow? Current { get; }
    }
}
