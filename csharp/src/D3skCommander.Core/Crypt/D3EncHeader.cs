using System.Buffers.Binary;
using System.Security.Cryptography;
using D3skCommander.Core.Common;
using Geralt;

namespace D3skCommander.Core.Crypt;

/// <summary>
/// <c>.d3enc</c> preamble: magic, version, Argon2id iterations and memory, salt, then the
/// libsodium secretstream header. Ciphertext chunks follow, each a little-endian length and
/// a secretstream frame (plaintext up to 64 KiB plus the tag).
/// </summary>
internal static class D3EncHeader
{
    internal const byte Version = 1;

    internal const int MagicLength = 5;

    internal const int VersionOffset = MagicLength;

    internal const int IterationsOffset = VersionOffset + 1;

    internal const int MemoryOffset = IterationsOffset + 4;

    internal const int SaltOffset = MemoryOffset + 4;

    internal const int StreamOffset = SaltOffset + Argon2id.SaltSize;

    internal const int Length = StreamOffset + IncrementalXChaCha20Poly1305.HeaderSize;

    internal static ReadOnlySpan<byte> Magic => "D3ENC"u8;

    internal static void Write(Stream output, EncryptionCost cost, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> streamHeader)
    {
        Span<byte> buffer = stackalloc byte[Length];
        Magic.CopyTo(buffer);
        buffer[VersionOffset] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[IterationsOffset..], cost.Iterations);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[MemoryOffset..], cost.MemoryBytes);
        salt.CopyTo(buffer[SaltOffset..]);
        streamHeader.CopyTo(buffer[StreamOffset..]);
        output.Write(buffer);
    }

    internal static HeaderRead Read(Stream input)
    {
        var buffer = new byte[Length];
        if (!StreamBytes.ReadExact(input, buffer))
        {
            return HeaderRead.Fail(NotOurFormat());
        }

        if (!buffer.AsSpan(0, MagicLength).SequenceEqual(Magic) || buffer[VersionOffset] != Version)
        {
            return HeaderRead.Fail(NotOurFormat());
        }

        var iterations = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(IterationsOffset));
        var memory = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(MemoryOffset));
        if (!EncryptionCost.TryCreate(iterations, memory, out var cost))
        {
            return HeaderRead.Fail(NotOurFormat());
        }

        var salt = buffer.AsSpan(SaltOffset, Argon2id.SaltSize).ToArray();
        var streamHeader = buffer.AsSpan(StreamOffset, IncrementalXChaCha20Poly1305.HeaderSize).ToArray();
        CryptographicOperations.ZeroMemory(buffer);
        return HeaderRead.Ok(cost, salt, streamHeader);
    }

    private static OperationError NotOurFormat() => OperationError.InvalidData(CryptText.NotOurFormat);
}

internal sealed class HeaderRead
{
    private HeaderRead(EncryptionCost cost, byte[] salt, byte[] streamHeader, OperationError? error)
    {
        Cost = cost;
        Salt = salt;
        StreamHeader = streamHeader;
        Error = error;
    }

    public EncryptionCost Cost { get; }

    public byte[] Salt { get; }

    public byte[] StreamHeader { get; }

    public OperationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static HeaderRead Ok(EncryptionCost cost, byte[] salt, byte[] streamHeader) =>
        new(cost, salt, streamHeader, null);

    public static HeaderRead Fail(OperationError error) => new(default, [], [], error);
}
