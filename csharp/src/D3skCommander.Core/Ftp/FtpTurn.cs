using D3skCommander.Core.Common;

namespace D3skCommander.Core.Ftp;

public sealed partial class FtpConnection
{
    /// <summary>Waits for the connection. Disposing hands it back unless <see cref="Keep"/> moved it to a stream.</summary>
    private async Task<Turn> TakeTurnAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return new Turn(null, OperationError.Network("the connection is closed"));
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new Turn(null, OperationError.Cancelled());
        }
        catch (ObjectDisposedException)
        {
            return new Turn(null, OperationError.Network("the connection is closed"));
        }

        return new Turn(_gate, null);
    }

    private sealed class Turn : IDisposable
    {
        private SemaphoreSlim? _held;

        public Turn(SemaphoreSlim? held, OperationError? error)
        {
            _held = held;
            Error = error;
        }

        public OperationError? Error { get; }

        /// <summary>The gate, handed to whoever releases it later; this turn no longer releases it.</summary>
        public SemaphoreSlim Keep()
        {
            var gate = _held ?? throw new InvalidOperationException("The turn holds nothing.");
            _held = null;
            return gate;
        }

        public void Dispose()
        {
            var held = _held;
            _held = null;
            try
            {
                held?.Release();
            }
            catch (ObjectDisposedException)
            {
                // The connection was disposed while this turn ran: nobody waits for the gate any more.
            }
        }
    }
}
