using System.Globalization;

namespace D3skCommander.Core.Update;

/// <summary>
/// HTTPS with a ten-minute limit and the <c>d3skcommander/&lt;version&gt;</c> user agent.
/// The release document also sends <c>Accept: application/vnd.github+json</c> and
/// <c>X-GitHub-Api-Version: 2022-11-28</c>.
/// </summary>
public sealed class HttpAssetSource : IAssetSource, IDisposable
{
    private readonly HttpClient _http;

    public HttpAssetSource(string version)
        : this(version, new HttpClientHandler())
    {
    }

    public HttpAssetSource(string version, HttpMessageHandler handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        ArgumentNullException.ThrowIfNull(handler);
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(600) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("d3skcommander/" + version);
    }

    public Task<Stream> OpenAsync(string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        return _http.GetStreamAsync(url, cancellationToken);
    }

    /// <summary>The latest release document at <paramref name="apiUrl"/>, parsed. A 404 names the address.</summary>
    public async Task<UpdateStep<GitHubRelease>> ReadLatestAsync(string apiUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if ((int)response.StatusCode == 404)
        {
            return UpdateStep<GitHubRelease>.Fail(
                "no published release at " + apiUrl + " (none yet, or the repository is private)");
        }

        if (!response.IsSuccessStatusCode)
        {
            return UpdateStep<GitHubRelease>.Fail(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ReleaseParser.Parse(body);
    }

    public void Dispose() => _http.Dispose();
}
