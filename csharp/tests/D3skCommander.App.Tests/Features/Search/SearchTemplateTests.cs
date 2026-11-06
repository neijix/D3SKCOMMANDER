using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Search;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Search;
using Xunit;

namespace D3skCommander.App.Tests.Features.Search;

public class SearchTemplateTests
{
    private static SearchViewModel Model(SearchDir dir, FakeDialogService dialogs) =>
        new(
            Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En),
            new JobService(),
            new SearchPicker(null),
            new SearchPanels(dir.Root),
            new SearchEngine(),
            dialogs,
            new SearchHistoryStore(SearchMemoryFolder.Fresh()),
            new SearchTemplateStore(SearchMemoryFolder.Fresh()));

    [Fact]
    public async Task ATemplateBringsTheSavedFieldsBack()
    {
        using var dir = new SearchDir();
        var dialogs = new FakeDialogService();
        var model = Model(dir, dialogs);
        model.Roots = "/src";
        model.NameMask = "*.cs;*.md";
        model.ExcludeFolders = "bin;obj";
        model.ExcludeFiles = "*.g.cs";
        model.FollowLinks = true;
        dialogs.Enqueue(DialogResult.Ok("code"));
        await model.SaveTemplateAsync();
        Assert.Equal(["code"], model.TemplateNames);

        model.Roots = "/elsewhere";
        model.NameMask = "*";
        model.ExcludeFolders = "";
        model.ExcludeFiles = "";
        model.FollowLinks = false;
        Assert.True(model.LoadTemplate("code"));
        Assert.Equal("/src", model.Roots);
        Assert.Equal("*.cs;*.md", model.NameMask);
        Assert.Equal("bin;obj", model.ExcludeFolders);
        Assert.Equal("*.g.cs", model.ExcludeFiles);
        Assert.True(model.FollowLinks);

        model.DeleteTemplate("code");
        Assert.Empty(model.TemplateNames);
        Assert.False(model.LoadTemplate("code"));
    }

    [Fact]
    public async Task ReplacingATemplateNeedsAConfirmation()
    {
        using var dir = new SearchDir();
        var dialogs = new FakeDialogService();
        var model = Model(dir, dialogs);
        model.NameMask = "first";
        dialogs.Enqueue(DialogResult.Ok("t"));
        await model.SaveTemplateAsync();
        model.NameMask = "second";
        dialogs.Enqueue(DialogResult.Ok("t"));
        dialogs.Enqueue(DialogResult.Cancel());
        await model.SaveTemplateAsync();
        model.NameMask = "other";
        Assert.True(model.LoadTemplate("t"));
        Assert.Equal("first", model.NameMask);
    }

    [Fact]
    public void AStartedSearchFeedsTheHistory()
    {
        using var dir = new SearchDir();
        var model = Model(dir, new FakeDialogService());
        model.Roots = dir.Root;
        model.NameMask = "*.txt";
        model.Start();
        Assert.Equal(["*.txt"], model.NameHistory);
        Assert.Equal([dir.Root], model.RootsHistory);
        Assert.Empty(model.TextHistory);
        model.Cancel();
    }
}
