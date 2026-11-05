using System.Net;
using FluentFTP;

namespace D3skCommander.Core.Ftp;

/// <summary>The library client with the raw data-connection call opened up, so the listing runs through <see cref="FtpListing"/>.</summary>
internal sealed class ListingClient : AsyncFtpClient
{
    public ListingClient(string host, NetworkCredential credential, int port)
        : base(host, credential, port)
    {
    }

    public Task<FtpDataStream> OpenListing(string command, CancellationToken cancellationToken) =>
        OpenDataStreamAsync(command, 0, cancellationToken);
}
