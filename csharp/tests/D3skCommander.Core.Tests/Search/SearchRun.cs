using D3skCommander.Core.Search;
using Xunit;

namespace D3skCommander.Core.Tests.Search;

internal sealed class SearchRun
{
    public List<SearchHit> Hits { get; } = [];

    public List<string> Errors { get; } = [];

    public SearchEvent? Done { get; private set; }

    public int EventsAfterDone { get; private set; }

    public static async Task<SearchRun> Execute(SearchQuery query)
    {
        var run = new SearchRun();
        await foreach (var step in new SearchEngine().RunAsync(query, TestContext.Current.CancellationToken))
        {
            run.Take(step);
        }

        return run;
    }

    public List<string> Names() => Hits.Select(h => h.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private void Take(SearchEvent step)
    {
        if (Done is not null)
        {
            EventsAfterDone++;
        }

        if (step.Kind == SearchEventKind.Hit)
        {
            Hits.Add(step.Hit!);
        }
        else if (step.Kind == SearchEventKind.Error)
        {
            Errors.Add(step.Error!.Message);
        }
        else if (step.Kind == SearchEventKind.Done)
        {
            Done = step;
        }
    }
}
