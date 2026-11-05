using D3skCommander.Core.Common;

namespace D3skCommander.Core.Trash;

/// <summary>One trash attempt. <see cref="OfferPermanentDelete"/> is set only for <see cref="OsTrash.UnavailableMessage"/>.</summary>
public readonly struct TrashStatus
{
    private TrashStatus(OperationError? error, bool offerPermanent)
    {
        Error = error;
        OfferPermanentDelete = offerPermanent;
    }

    public OperationError? Error { get; }

    public bool IsSuccess => Error is null;

    /// <summary>Trash cannot be used. The delete dialog offers a permanent delete.</summary>
    public bool OfferPermanentDelete { get; }

    public static TrashStatus Success => new(null, false);

    public static TrashStatus Fail(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new TrashStatus(error, false);
    }

    public static TrashStatus Unavailable(string? path = null) =>
        new(OperationError.Unsupported(OsTrash.UnavailableMessage, path), true);
}
