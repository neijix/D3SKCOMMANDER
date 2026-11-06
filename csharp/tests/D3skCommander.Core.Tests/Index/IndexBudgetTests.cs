using System.Diagnostics;
using D3skCommander.Core.Index;
using D3skCommander.Testing;
using Xunit;

namespace D3skCommander.Core.Tests.Index;

public class IndexBudgetTests
{
    private const int Entries = 1_000_000;
    private const double BudgetMilliseconds = 100;

    [Fact, Slow]
    public void AQueryOverAMillionEntriesAnswersInUnder100Milliseconds()
    {
        var index = Synthetic(Entries);
        Assert.Equal(Entries, index.Count);
        string[] queries = ["report", "a", "*.pdf", "ext:pdf size:>1M", "doc ext:txt;md", "folder:src type:file x", "zzzzqqq"];

        foreach (var text in queries)
        {
            var query = NameQueryParser.Parse(text);
            index.Search(query, NameIndex.DefaultLimit, TestContext.Current.CancellationToken);
            var worst = 0d;
            for (var run = 0; run < 5; run++)
            {
                var clock = Stopwatch.StartNew();
                var result = index.Search(query, NameIndex.DefaultLimit, TestContext.Current.CancellationToken);
                clock.Stop();
                worst = Math.Max(worst, clock.Elapsed.TotalMilliseconds);
                Assert.True(result.Hits.Count <= NameIndex.DefaultLimit);
            }

            TestContext.Current.SendDiagnosticMessage($"quick find '{text}': worst of 5 warm runs {worst:F1} ms over {Entries} entries");
            Assert.True(worst < BudgetMilliseconds, $"'{text}' took {worst:F1} ms over {Entries} entries (budget {BudgetMilliseconds} ms)");
        }
    }

    private static NameIndex Synthetic(int count)
    {
        var random = new Random(42);
        string[] words = ["report", "photo", "doc", "src", "main", "index", "data", "backup", "notes", "invoice", "cat", "img", "readme", "setup", "build"];
        string[] extensions = ["txt", "md", "pdf", "jpg", "cs", "json", "zip", "log"];
        var stamp = DateTimeOffset.UnixEpoch.AddDays(19000);
        var builder = new NameIndexBuilder();
        var folders = new List<int> { builder.Add("/data", -1, 0, stamp, true) };
        while (builder.Count < count)
        {
            var parent = folders[random.Next(folders.Count)];
            var name = words[random.Next(words.Length)] + "_" + random.Next(100_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (random.Next(8) == 0)
            {
                folders.Add(builder.Add(name, parent, 0, stamp, true));
            }
            else
            {
                builder.Add(name + "." + extensions[random.Next(extensions.Length)], parent, random.NextInt64(5_000_000), stamp, false);
            }
        }

        return builder.Build(stamp);
    }
}
