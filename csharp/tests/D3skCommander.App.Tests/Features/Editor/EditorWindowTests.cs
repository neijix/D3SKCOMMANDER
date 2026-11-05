using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Editor;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.ToolWindows;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.App.Tests.Features.Editor;

public class EditorWindowTests
{
    [AvaloniaFact]
    public async Task F4ShowsTheFirstFileAndFoldersAloneShowNoSelection()
    {
        using var rig = await EditorRig.Open(("notes.txt", "hello\nworld\n"));
        Directory.CreateDirectory(Path.Combine(rig.Root, "sub"));
        await rig.Workspace.RefreshAsync(TestContext.Current.CancellationToken);

        rig.Select("sub", "notes.txt");
        await rig.Commands.EditAsync();
        var model = rig.Model;
        Assert.Equal("notes.txt", model.FileName);
        Assert.False(model.IsBusy);
        Assert.Equal("hello\nworld\n", model.Buffer.Text);
        Assert.False(model.Dirty);
        Assert.Contains("LF", model.InfoText, StringComparison.Ordinal);
        rig.Tools.Open(ToolKind.Editor).Close();

        rig.Select("sub");
        var pending = rig.Commands.EditAsync();
        var shown = await rig.Dialogs.WaitShownAsync();
        Assert.Equal(rig.Text.Get("dlg.no_selection"), shown.Text);
        Assert.False(rig.Tools.IsOpen(ToolKind.Editor));
        rig.Dialogs.Answer(DialogResult.Ok(""));
        await pending;
    }

    [AvaloniaFact]
    public async Task TheEditorUsesAFixedPitchFont()
    {
        using var rig = await EditorRig.Open(("a.txt", "iW\n"));
        rig.Select("a.txt");
        await rig.Commands.EditAsync();
        var tool = rig.Tools.Open(ToolKind.Editor);
        tool.UpdateLayout();

        var surface = Assert.IsType<EditorView>(tool.Content).FindControl<AvaloniaEdit.TextEditor>("Surface")!;
        Assert.True(D3skCommander.App.Tests.Features.Lister.FixedPitch.Is(surface.FontFamily), surface.FontFamily.ToString());
    }

    [AvaloniaFact]
    public async Task TypingThenCtrlSSavesKeepingCrlfAndTheBom()
    {
        using var rig = await EditorRig.Open();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("one\r\ntwo\r\n")).ToArray();
        File.WriteAllBytes(Path.Combine(rig.Root, "win.txt"), bytes);
        await rig.Workspace.RefreshAsync(TestContext.Current.CancellationToken);
        rig.Select("win.txt");
        await rig.Commands.EditAsync();
        var model = rig.Model;
        Assert.Equal("one\ntwo\n", model.Buffer.Text);
        Assert.Contains("CRLF", model.InfoText, StringComparison.Ordinal);
        Assert.Contains("BOM", model.InfoText, StringComparison.Ordinal);

        model.Buffer.Insert(0, "X");
        Assert.True(model.Dirty);
        var tool = rig.Tools.Open(ToolKind.Editor);
        var surface = Assert.IsType<EditorView>(tool.Content).FindControl<AvaloniaEdit.TextEditor>("Surface")!;
        surface.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.S,
            KeyModifiers = KeyModifiers.Control,
        });
        await rig.WaitForAsync(() => !model.Dirty);

        Assert.Equal("Saved", model.Notice);
        var expected = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Xone\r\ntwo\r\n"));
        Assert.Equal(expected, File.ReadAllBytes(Path.Combine(rig.Root, "win.txt")));
    }

    [AvaloniaFact]
    public async Task AFailingSaveKeepsTheBytesTheBufferAndShowsTheTypedError()
    {
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess)
        {
            return;
        }

        using var rig = await EditorRig.Open();
        var folder = Directory.CreateDirectory(Path.Combine(rig.Root, "locked")).FullName;
        var path = Path.Combine(folder, "a.txt");
        File.WriteAllText(path, "keep\n");
        var model = new EditorViewModel(rig.Text, rig.Dialogs, () => Task.CompletedTask);
        await model.OpenAsync(path);
        model.Buffer.Text = "changed";
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var saving = model.SaveAsync();
            var shown = await rig.Dialogs.WaitShownAsync();
            Assert.StartsWith(rig.Text.Get("error.access_denied") + ": ", shown.Text, StringComparison.Ordinal);
            Assert.Contains("a.txt", shown.Text, StringComparison.Ordinal);
            rig.Dialogs.Answer(DialogResult.Ok(""));
            Assert.False(await saving);
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.True(model.Dirty);
        Assert.Equal("changed", model.Buffer.Text);
        Assert.Equal("keep\n", File.ReadAllText(path));
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EscapeOnADirtyBufferAsksSaveDontSaveOrCancel(int pick)
    {
        using var rig = await EditorRig.Open(("a.txt", "one\n"));
        rig.Select("a.txt");
        await rig.Commands.EditAsync();
        var tool = rig.Tools.Open(ToolKind.Editor);
        var model = rig.Model;
        model.Buffer.Insert(0, "X");

        tool.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        var shown = await rig.Dialogs.WaitShownAsync();
        Assert.Equal(["Save", "Don't save", "Cancel"], shown.Choices);
        Assert.Contains("a.txt", shown.Text, StringComparison.Ordinal);
        Assert.True(tool.IsVisible);
        rig.Dialogs.Answer(DialogResult.Picked(pick));
        await rig.WaitForAsync(() => pick == 2 || !tool.IsVisible);

        var bytes = File.ReadAllText(Path.Combine(rig.Root, "a.txt"));
        Assert.Equal(pick != 2, !tool.IsVisible);
        Assert.Equal(pick == 0 ? "Xone\n" : "one\n", bytes);
        Assert.Equal(pick != 0, model.Dirty);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task F4OntoAnotherFileAsksFirstAndOnlyAYesOpensIt(int pick)
    {
        using var rig = await EditorRig.Open(("a.txt", "one\n"), ("b.txt", "two\n"));
        rig.Select("a.txt");
        await rig.Commands.EditAsync();
        var model = rig.Model;
        model.Buffer.Insert(0, "X");

        rig.Select("b.txt");
        var opening = rig.Commands.EditAsync();
        await rig.Dialogs.WaitShownAsync();
        Assert.Equal("a.txt", model.FileName);
        rig.Dialogs.Answer(DialogResult.Picked(pick));
        await opening;

        Assert.Equal(pick == 2 ? "a.txt" : "b.txt", model.FileName);
        Assert.Equal(pick == 2 ? "Xone\n" : "two\n", model.Buffer.Text);
        Assert.Equal(pick == 2, model.Dirty);
        Assert.Equal(pick == 0 ? "Xone\n" : "one\n", File.ReadAllText(Path.Combine(rig.Root, "a.txt")));
    }

    [AvaloniaFact]
    public async Task ACleanBufferClosesWithoutAsking()
    {
        using var rig = await EditorRig.Open(("a.txt", "one\n"));
        rig.Select("a.txt");
        await rig.Commands.EditAsync();
        var tool = rig.Tools.Open(ToolKind.Editor);

        tool.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.False(tool.IsVisible);
        Assert.Equal(0, rig.Dialogs.ShownCount);
    }

    [AvaloniaFact]
    public async Task AFailingSaveOnCloseShowsTheErrorAndKeepsTheWindowAndBuffer()
    {
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess)
        {
            return;
        }

        using var rig = await EditorRig.Open();
        var folder = Directory.CreateDirectory(Path.Combine(rig.Root, "locked")).FullName;
        var path = Path.Combine(folder, "a.txt");
        File.WriteAllText(path, "keep\n");
        var model = rig.Model;
        await model.OpenAsync(path);
        model.Buffer.Text = "changed";
        var tool = rig.Tools.Open(ToolKind.Editor);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            tool.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await rig.Dialogs.WaitShownAsync();
            rig.Dialogs.Answer(DialogResult.Picked(0));
            var error = await rig.Dialogs.WaitShownAsync();
            Assert.StartsWith(rig.Text.Get("error.access_denied") + ": ", error.Text, StringComparison.Ordinal);
            rig.Dialogs.Answer(DialogResult.Ok(""));
            await rig.WaitForAsync(() => false);
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.True(tool.IsVisible);
        Assert.True(model.Dirty);
        Assert.Equal("changed", model.Buffer.Text);
        Assert.Equal("keep\n", File.ReadAllText(path));
        model.Buffer.Text = "keep\n";
    }

    [AvaloniaFact]
    public async Task EditNewOpensAnEmptyBufferWrittenOnTheFirstSave()
    {
        using var rig = await EditorRig.Open();
        rig.Select();
        var asking = rig.Commands.EditNewAsync();
        var shown = await rig.Dialogs.WaitShownAsync();
        Assert.Equal(rig.Text.Get("editor.new_name"), shown.Text);
        rig.Dialogs.Answer(DialogResult.Ok("fresh.txt"));
        await asking;

        var model = rig.Model;
        Assert.Equal("fresh.txt", model.FileName);
        Assert.Equal("", model.Buffer.Text);
        Assert.False(File.Exists(Path.Combine(rig.Root, "fresh.txt")));
        model.Buffer.Text = "hi";
        Assert.True(await model.SaveAsync());
        Assert.Equal("hi", File.ReadAllText(Path.Combine(rig.Root, "fresh.txt")));
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("a*b")]
    public async Task EditNewWithACancelOrABadNameOpensNothing(string? answer)
    {
        if (answer == "a*b" && !OperatingSystem.IsWindows())
        {
            return; // '*' is a forbidden character on Windows only
        }

        using var rig = await EditorRig.Open();
        rig.Select();
        var asking = rig.Commands.EditNewAsync();
        await rig.Dialogs.WaitShownAsync();
        rig.Dialogs.Answer(answer is null ? DialogResult.Cancel() : DialogResult.Ok(answer));
        if (!string.IsNullOrEmpty(answer))
        {
            var shown = await rig.Dialogs.WaitShownAsync();
            Assert.StartsWith(rig.Text.Get("error.invalid_data") + ": ", shown.Text, StringComparison.Ordinal);
            rig.Dialogs.Answer(DialogResult.Ok(""));
        }

        await asking;
        Assert.False(rig.Tools.IsOpen(ToolKind.Editor));
    }

    [AvaloniaFact]
    public async Task EditNewOnAnExistingNameOpensThatFile()
    {
        using var rig = await EditorRig.Open(("here.txt", "present\n"));
        rig.Select();
        var asking = rig.Commands.EditNewAsync();
        await rig.Dialogs.WaitShownAsync();
        rig.Dialogs.Answer(DialogResult.Ok("here.txt"));
        await asking;

        Assert.Equal("present\n", rig.Model.Buffer.Text);
    }

    [AvaloniaFact]
    public async Task FindAndReplaceFollowTheMatchCaseSetting()
    {
        using var rig = await EditorRig.Open(("f.txt", "Cat cat CAT\n"));
        rig.Select("f.txt");
        await rig.Commands.EditAsync();
        var model = rig.Model;
        var picked = new List<(int Start, int Length)>();
        model.SelectionRequested += (start, length) => picked.Add((start, length));

        model.FindText = "cat";
        model.FindNext();
        Assert.Equal((0, 3), picked[^1]);
        model.SetSelection(0, 3);
        model.FindNext();
        Assert.Equal((4, 3), picked[^1]);

        model.MatchCase = true;
        model.ReplaceText = "dog";
        model.ReplaceAll();
        Assert.Equal("Cat dog CAT\n", model.Buffer.Text);
        Assert.Equal("1 replaced", model.Notice);
        Assert.True(model.Dirty);

        model.FindText = "zzz";
        model.FindNext();
        Assert.Equal("Not found", model.Notice);
    }

    [Fact]
    public void StringsExistInBothLanguages()
    {
        var en = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var fr = Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);
        string[] keys =
        [
            "editor.opening", "editor.new_name", "editor.dirty", "editor.bom", "editor.lossy", "editor.saved",
            "editor.save_failed", "editor.not_found", "editor.replaced", "editor.find", "editor.replace",
            "editor.find_next", "editor.replace_one", "editor.replace_all", "editor.match_case", "editor.save",
            "editor.unsaved", "editor.dont_save",
        ];
        foreach (var key in keys)
        {
            Assert.NotEqual(key, en.Get(key));
            Assert.NotEqual(key, fr.Get(key));
        }

        var dialogs = new FakeDialogService();
        Assert.Equal("Replace all", new EditorViewModel(en, dialogs, () => Task.CompletedTask).ReplaceAllLabel);
        Assert.Equal("Tout remplacer", new EditorViewModel(fr, dialogs, () => Task.CompletedTask).ReplaceAllLabel);
    }

    private sealed class EditorRig : IDisposable
    {
        private readonly ToolWindowApp _app;

        private EditorRig(string root, PanelWorkspace workspace, ToolWindowApp app)
        {
            Root = root;
            Workspace = workspace;
            _app = app;
            Text = app.Localizer;
            Commands = new EditorCommands(workspace, app.Tools, Dialogs, Text);
            Commands.Bind(new ShellContributions());
        }

        public string Root { get; }

        public PanelWorkspace Workspace { get; }

        public FakeDialogService Dialogs { get; } = new();

        public ILocalizer Text { get; }

        public EditorCommands Commands { get; }

        public IToolWindowHost Tools => _app.Tools;

        public EditorViewModel Model => Assert.IsType<EditorViewModel>(Tools.Open(ToolKind.Editor).View);

        public static async Task<EditorRig> Open(params (string Name, string Content)[] files)
        {
            var root = Directory.CreateTempSubdirectory("d3sk-editor-").FullName;
            foreach (var (name, content) in files)
            {
                File.WriteAllText(Path.Combine(root, name), content);
            }

            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new NoFolderPicker());
            await workspace.OpenAsync(root, root, TestContext.Current.CancellationToken);
            return new EditorRig(root, workspace, ToolWindowApp.Open());
        }

        public void Select(params string[] names)
        {
            Workspace.Activate(PanelSide.Left);
            Workspace.ApplySelection(PanelSide.Left, 0, names.Select(name => Path.Combine(Root, name)).ToList());
        }

        public async Task WaitForAsync(Func<bool> done)
        {
            for (var i = 0; i < 200 && !done(); i++)
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }

        public void Dispose()
        {
            _app.Dispose();
            Workspace.Dispose();
            Directory.Delete(Root, true);
        }
    }

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
