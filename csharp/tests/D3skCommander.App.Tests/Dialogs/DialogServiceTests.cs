using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3skCommander.App;
using D3skCommander.App.Dialogs;
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
        Assert.IsType<WindowDialogSurface>(provider.GetRequiredService<IDialogSurface>());
        Assert.IsType<DialogsModule>(provider.GetRequiredService<DialogsModule>());
    }

    [AvaloniaFact]
    public async Task OwnedModalWindowsOpenOneAtATime()
    {
        var localizer = French();
        var owner = ShowOwner();
        try
        {
            var stop = TestContext.Current.CancellationToken;
            var dialogs = Real(owner, localizer, new FakeFolderPicker());
            var text = dialogs.AskTextAsync(localizer.Get("dialog.choose_folder"), localizer.Get("dialog.browse"), "dossier", stop);
            var first = await WaitForOwned(owner);
            first.UpdateLayout();
            Assert.Same(owner, first.Owner);
            Assert.Equal("Choisir un dossier", first.Title);
            Assert.Equal("Annuler", first.FindControl<Button>("CancelButton")!.Content);
            Assert.False(text.IsCompleted);

            var confirm = dialogs.ConfirmAsync("Confirmer", "Continuer", stop);
            Assert.Single(owner.OwnedWindows);
            Assert.False(confirm.IsCompleted);

            Accept(first);
            Assert.Equal("dossier", await text);
            var second = await WaitForOwned(owner, first);
            Assert.NotSame(first, second);
            Cancel(second);
            Assert.False(await confirm);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            CloseOwned(owner);
        }
    }

    [AvaloniaFact]
    public async Task FormDialogUsesTheSharedTemplate()
    {
        var localizer = French();
        var owner = ShowOwner();
        var picker = new FakeFolderPicker();
        picker.Enqueue("/picked");
        var folder = new FolderDialogField("Cible", "");
        var form = new DialogForm("Décompresser", FormFields(folder), FolderIsFilled);
        try
        {
            var dialogs = Real(owner, localizer, picker);
            var pending = dialogs.ShowFormAsync(form, TestContext.Current.CancellationToken);
            var window = await WaitForOwned(owner);
            window.UpdateLayout();
            Assert.Same(owner, window.Owner);
            Assert.IsType<FormDialogView>(Assert.Single(window.GetVisualDescendants().OfType<FormDialogView>()));
            Assert.Equal("Annuler", window.FindControl<Button>("CancelButton")!.Content);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "deux fichiers");
            var browse = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.Content as string == localizer.Get("dialog.browse"));
            Assert.False(window.FindControl<Button>("AcceptButton")!.Command!.CanExecute(null));
            browse.Command!.Execute(null);
            Assert.Equal("/picked", folder.Path);
            Assert.True(window.FindControl<Button>("AcceptButton")!.Command!.CanExecute(null));
            Accept(window);
            Assert.True(await pending);
        }
        finally
        {
            CloseOwned(owner);
        }
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

    private static Localizer French() => Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);

    private static Window ShowOwner()
    {
        var owner = new Window { Width = 800, Height = 600 };
        owner.Show();
        return owner;
    }

    private static DialogService Real(Window owner, ILocalizer localizer, IFolderPicker folders) =>
        new(new WindowDialogSurface(new FixedOwner(owner), localizer, folders));

    private static void Accept(Window window)
    {
        var model = Assert.IsType<DialogWindowModel>(window.DataContext);
        Assert.True(model.AcceptCommand.CanExecute(null));
        model.AcceptCommand.Execute(null);
    }

    private static void Cancel(Window window)
    {
        var model = Assert.IsType<DialogWindowModel>(window.DataContext);
        model.CancelCommand.Execute(null);
    }

    private static async Task<Window> WaitForOwned(Window owner, Window? previous = null)
    {
        for (var i = 0; i < 30; i++)
        {
            var open = new List<Window>();
            foreach (var window in owner.OwnedWindows)
            {
                if (!ReferenceEquals(window, previous))
                {
                    open.Add(window);
                }
            }

            if (open.Count == 1 && owner.OwnedWindows.Count == open.Count)
            {
                return open[0];
            }

            await Task.Yield();
        }

        throw new TimeoutException("The next dialog did not open.");
    }

    private static void CloseOwned(Window owner)
    {
        var owned = new List<Window>();
        foreach (var window in owner.OwnedWindows)
        {
            owned.Add(window);
        }

        foreach (var window in owned)
        {
            window.Close();
        }

        if (owner.IsVisible)
        {
            owner.Close();
        }
    }

    private sealed class FixedOwner : IDialogOwner
    {
        private readonly Window _window;

        public FixedOwner(Window window) => _window = window;

        public Window? Current => _window;
    }
}
