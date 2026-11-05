using FluentFTP;

namespace D3skCommander.Core.Ftp;

/// <summary>
/// Ends a library transfer stream. Only <c>Close</c> reads the server's reply to the transfer;
/// without it the reply (226) is taken for the answer to the next command.
/// </summary>
internal static class DataStreams
{
    public static async ValueTask CloseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream is FtpDataStream data)
        {
            await data.CloseAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await stream.DisposeAsync().ConfigureAwait(false);
    }

    public static void Close(Stream stream)
    {
        if (stream is FtpDataStream data)
        {
            data.Close();
            return;
        }

        stream.Dispose();
    }
}
