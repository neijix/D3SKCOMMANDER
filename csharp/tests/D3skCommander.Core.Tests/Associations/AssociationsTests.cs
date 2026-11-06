using D3skCommander.Core.Associations;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.Core.Tests.Associations;

public class AssociationsTests
{
    private static AssociationAction Run(string program, string arguments = "") => new(program, arguments);

    [Fact]
    public void AMaskMatchesTheFileNameWithoutCaseAndSeveralMasksAreAlternatives()
    {
        var rule = new AssociationRule("Pictures", "*.png; *.JPG;photo?.bmp", Run("viewer"));
        var rules = new[] { rule };

        Assert.NotNull(FileAssociations.Find(rules, "A.PNG", AssociationKind.Open));
        Assert.NotNull(FileAssociations.Find(rules, "holiday.jpg", AssociationKind.Open));
        Assert.NotNull(FileAssociations.Find(rules, "Photo1.bmp", AssociationKind.Open));
        Assert.Null(FileAssociations.Find(rules, "notes.txt", AssociationKind.Open));
        Assert.Null(FileAssociations.Find(rules, "photo12.bmp", AssociationKind.Open));
    }

    [Fact]
    public void ARuleWithoutMasksMatchesNothing()
    {
        var rules = new[] { new AssociationRule("Empty", "  ", Run("x")) };

        Assert.Null(FileAssociations.Find(rules, "a.txt", AssociationKind.Open));
    }

    [Fact]
    public void TheFirstMatchingRuleThatDefinesTheActionWins()
    {
        var first = new AssociationRule("First", "*.txt", Run("one"));
        var second = new AssociationRule("Second", "*.txt;*.md", Run("two"));

        var match = FileAssociations.Find([first, second], "a.txt", AssociationKind.Open);

        Assert.Same(first, match!.Rule);
        Assert.Equal("one", match.Action.Program);
    }

    [Fact]
    public void ARuleWithoutTheRequestedActionFallsThroughToTheNextRule()
    {
        var onlyOpen = new AssociationRule("Open only", "*.txt", Run("opener"));
        var withView = new AssociationRule("View too", "*.txt", Run("other"), view: Run("pager"));

        var rules = new[] { onlyOpen, withView };

        Assert.Equal("pager", FileAssociations.Find(rules, "a.txt", AssociationKind.View)!.Action.Program);
        Assert.Equal("opener", FileAssociations.Find(rules, "a.txt", AssociationKind.Open)!.Action.Program);
        Assert.Null(FileAssociations.Find(rules, "a.txt", AssociationKind.Edit));
    }

    [Fact]
    public void AnActionWithoutAProgramIsNotAnAction()
    {
        var rule = new AssociationRule("Blank", "*.txt", Run("  "), edit: Run("ed"));

        Assert.Null(rule.Open);
        Assert.Equal("ed", rule.Edit!.Program);
    }

    [Fact]
    public void ExtraActionsComeFromEveryMatchingRuleInOrderAndNeedANameAndAProgram()
    {
        var a = new AssociationRule("A", "*.png", extras: [new("opt", "%p", Name: "Optimize"), new("x"), new("", Name: "Nothing")]);
        var b = new AssociationRule("B", "*.txt", extras: [new("wc", Name: "Count")]);
        var c = new AssociationRule("C", "*.*", extras: [new("sum", Name: "Hash")]);

        var extras = FileAssociations.ExtraActions([a, b, c], "pic.PNG");

        Assert.Equal(["Optimize", "Hash"], extras.Select(match => match.Action.Name));
        Assert.Same(a, extras[0].Rule);
        Assert.Empty(FileAssociations.ExtraActions([b], "pic.png"));
    }

    [Fact]
    public void ArgumentsExpandThePlaceholdersAndKeepAPathWithSpacesAsOneArgument()
    {
        var action = new AssociationAction("/opt/my tool/run", "--in \"%p\" --name %f --dir %d -v", "");
        var path = Path.Combine(Path.GetTempPath(), "my docs", "a b.txt");

        var start = FileAssociations.BuildStart(action, new AssociationContext(path));

        Assert.Equal("/opt/my tool/run", start.Program);
        Assert.Equal(["--in", path, "--name", "a b.txt", "--dir", Path.GetDirectoryName(path)!, "-v"], start.Arguments);
        Assert.Equal(Path.GetDirectoryName(path), start.WorkingFolder);
    }

    [Fact]
    public void WithNoPlaceholderTheFullPathIsAppended()
    {
        var path = Path.Combine(Path.GetTempPath(), "x y.md");

        var plain = FileAssociations.BuildStart(new AssociationAction("ed", "-n --fast"), new AssociationContext(path));
        var bare = FileAssociations.BuildStart(new AssociationAction("ed"), new AssociationContext(path));

        Assert.Equal(["-n", "--fast", path], plain.Arguments);
        Assert.Equal([path], bare.Arguments);
    }

    [Fact]
    public void SelectionStandsForTheSelectedFilesAndTheWorkingFolderCanUseAPlaceholder()
    {
        var one = Path.Combine(Path.GetTempPath(), "a.txt");
        var two = Path.Combine(Path.GetTempPath(), "b c.txt");
        var action = new AssociationAction("tool", "--files=%s", "%d");

        var start = FileAssociations.BuildStart(action, new AssociationContext(one, [one, two]));

        Assert.Equal(["--files=" + one, "--files=" + two], start.Arguments);
        Assert.Equal(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), start.WorkingFolder);
    }

    [Fact]
    public void ThePlaceholdersAreExpandedInOnePassSoANameIsNeverExpandedAgain()
    {
        var path = Path.Combine(Path.GetTempPath(), "%f%d.txt");

        var start = FileAssociations.BuildStart(new AssociationAction("t", "%f"), new AssociationContext(path));

        Assert.Equal(["%f%d.txt"], start.Arguments);
    }

    [Fact]
    public void ThePreferencesRoundTripTheRulesThroughTheFile()
    {
        var folder = Directory.CreateTempSubdirectory("d3sk-assoc-").FullName;
        try
        {
            var made = new Preferences
            {
                Associations =
                [
                    new AssociationRule("Pictures", "*.png;*.jpg", Run("/opt/my viewer/v", "\"%p\""), new AssociationAction("pager", "-r %s", "/tmp"), null, [new("opt", "-o %f", "/work", "Optimize")]),
                    new AssociationRule("Text", "*.txt", edit: Run("ed")),
                ],
            };
            using (var store = new PreferencesStore(folder))
            {
                store.Save(made);
            }

            using var reread = new PreferencesStore(folder);
            var rules = reread.Current.Associations;
            Assert.Equal(["Pictures", "Text"], rules.Select(rule => rule.Name));
            Assert.Equal("*.png;*.jpg", rules[0].Masks);
            Assert.Equal(new AssociationAction("/opt/my viewer/v", "\"%p\""), rules[0].Open);
            Assert.Equal(new AssociationAction("pager", "-r %s", "/tmp"), rules[0].View);
            Assert.Null(rules[0].Edit);
            Assert.Equal([new AssociationAction("opt", "-o %f", "/work", "Optimize")], rules[0].Extras);
            Assert.Null(rules[1].Open);
            Assert.Equal(new AssociationAction("ed"), rules[1].Edit);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ANewFileHasNoRule()
    {
        var folder = Directory.CreateTempSubdirectory("d3sk-assoc-").FullName;
        try
        {
            using var store = new PreferencesStore(folder);
            Assert.Empty(store.Current.Associations);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void MalformedEntriesAreDroppedAndTheOthersStay()
    {
        var folder = Directory.CreateTempSubdirectory("d3sk-assoc-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, SettingsFileNames.Preferences), """
                {
                  "schema": 1,
                  "associations": [
                    { "name": "Good", "masks": "*.png", "open": { "program": "viewer" }, "view": { "program": "" }, "edit": "text",
                      "extras": [ { "name": "Ok", "program": "p" }, { "name": "No program" }, { "program": "no name" }, 5, null ] },
                    { "name": "No masks", "open": { "program": "x" } },
                    { "name": "Masks not text", "masks": 7 },
                    "text",
                    null,
                    42,
                    { "masks": "*.md", "open": 3, "edit": { "program": "ed", "arguments": 5 } }
                  ]
                }
                """);
            using var store = new PreferencesStore(folder);

            var rules = store.Current.Associations;
            Assert.Equal(2, rules.Count);
            Assert.Equal("Good", rules[0].Name);
            Assert.Equal("viewer", rules[0].Open!.Program);
            Assert.Null(rules[0].View);
            Assert.Null(rules[0].Edit);
            Assert.Equal(["Ok"], rules[0].Extras.Select(extra => extra.Name));
            Assert.Equal("*.md", rules[1].Name);
            Assert.Null(rules[1].Open);
            Assert.Equal(new AssociationAction("ed"), rules[1].Edit);
            Assert.Null(store.TakeLoadError());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void AnAssociationsValueThatIsNotAnArrayGivesNoRule()
    {
        var folder = Directory.CreateTempSubdirectory("d3sk-assoc-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, SettingsFileNames.Preferences), """{ "schema": 1, "associations": { "masks": "*.png" } }""");
            using var store = new PreferencesStore(folder);

            Assert.Empty(store.Current.Associations);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
