using D3skCommander.Core.Index;

namespace D3skCommander.Core.Tests.Index;

/// <summary>A small hand-made index: /data/{Report.pdf, report.txt, reports/{q1.pdf, big.iso}, notes/}, /home/ann/readme.</summary>
internal static class IndexTree
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    public static NameIndex Small()
    {
        var b = new NameIndexBuilder();
        var data = b.Add("/data", -1, 0, Stamp, true);
        b.Add("Report.pdf", data, 2_000, Stamp, false);
        b.Add("report.txt", data, 300, Stamp, false);
        var reports = b.Add("reports", data, 0, Stamp, true);
        b.Add("q1.pdf", reports, 5_000_000, Stamp, false);
        b.Add("big.iso", reports, 3_000_000_000, Stamp, false);
        b.Add("notes", data, 0, Stamp, true);
        b.Add("my report final.docx", data, 90_000, Stamp, false);
        var home = b.Add("/home", -1, 0, Stamp, true);
        var ann = b.Add("ann", home, 0, Stamp, true);
        b.Add("readme", ann, 10, Stamp, false);
        return b.Build(Stamp);
    }

    public static string[] Names(IndexSearchResult result) => [.. result.Hits.Select(hit => hit.Name)];

    public static IndexSearchResult Run(NameIndex index, string query, int limit = NameIndex.DefaultLimit) =>
        index.Search(NameQueryParser.Parse(query), limit);
}
