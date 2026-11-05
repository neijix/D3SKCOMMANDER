using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using Geralt;

namespace D3skCommander.Core.Crypt;

/// <summary>
/// Passphrase encryption: Argon2id derives a key, libsodium secretstream carries the bytes.
/// Chunks are 64 KiB of plaintext. A false progress return cancels. The password is never
/// copied into an error.
/// </summary>
internal static class SecretStream
{
    private const int PlainChunkSize = 64 * 1024;

    public static FileStatus Encrypt(
        Stream input,
        Stream output,
        string password,
        EncryptionCost cost,
        Func<long, bool> progress)
    {
        Span<byte> salt = stackalloc byte[Argon2id.SaltSize];
        SecureRandom.Fill(salt);
        var key = DeriveKey(password, salt, cost);
        var streamHeader = new byte[IncrementalXChaCha20Poly1305.HeaderSize];
        IncrementalXChaCha20Poly1305? cipher = null;
        try
        {
            cipher = new IncrementalXChaCha20Poly1305(streamHeader, key, encryption: true);
            D3EncHeader.Write(output, cost, salt, streamHeader);
            return WritePayload(input, output, cipher, progress);
        }
        catch (CryptographicException ex)
        {
            return FileStatus.Fail(OperationError.FromException(ex));
        }
        finally
        {
            cipher?.Dispose();
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(streamHeader);
            salt.Clear();
        }
    }

    public static FileStatus Decrypt(Stream input, Stream output, string password, Func<long, bool> progress)
    {
        var header = D3EncHeader.Read(input);
        if (!header.IsSuccess)
        {
            return FileStatus.Fail(header.Error!);
        }

        var key = DeriveKey(password, header.Salt, header.Cost);
        IncrementalXChaCha20Poly1305? cipher = null;
        try
        {
            cipher = new IncrementalXChaCha20Poly1305(header.StreamHeader, key, encryption: false);
            return ReadPayload(input, output, cipher, progress);
        }
        catch (CryptographicException)
        {
            return WrongPassword();
        }
        finally
        {
            cipher?.Dispose();
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(header.Salt);
            CryptographicOperations.ZeroMemory(header.StreamHeader);
        }
    }

    private static FileStatus WritePayload(
        Stream input,
        Stream output,
        IncrementalXChaCha20Poly1305 cipher,
        Func<long, bool> progress)
    {
        var pending = new byte[PlainChunkSize];
        var ahead = new byte[PlainChunkSize];
        var cipherBuf = new byte[PlainChunkSize + IncrementalXChaCha20Poly1305.TagSize];
        try
        {
            var count = ReadUpTo(input, pending);
            if (count == 0)
            {
                WriteChunk(output, cipher, cipherBuf, ReadOnlySpan<byte>.Empty, FinalFlag());
                return FileStatus.Success;
            }

            return WritePending(input, output, cipher, progress, pending, ahead, cipherBuf, count);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pending);
            CryptographicOperations.ZeroMemory(ahead);
            CryptographicOperations.ZeroMemory(cipherBuf);
        }
    }

    private static FileStatus WritePending(
        Stream input,
        Stream output,
        IncrementalXChaCha20Poly1305 cipher,
        Func<long, bool> progress,
        byte[] pending,
        byte[] ahead,
        byte[] cipherBuf,
        int count)
    {
        long done = 0;
        while (true)
        {
            var next = ReadUpTo(input, ahead);
            var flag = next == 0 ? FinalFlag() : MessageFlag();
            WriteChunk(output, cipher, cipherBuf, pending.AsSpan(0, count), flag);
            done += count;
            if (!progress(done))
            {
                return FileStatus.Fail(OperationError.Cancelled());
            }

            if (next == 0)
            {
                return FileStatus.Success;
            }

            ahead.AsSpan(0, next).CopyTo(pending);
            count = next;
        }
    }

    private static void WriteChunk(
        Stream output,
        IncrementalXChaCha20Poly1305 cipher,
        byte[] cipherBuf,
        ReadOnlySpan<byte> plain,
        IncrementalXChaCha20Poly1305.ChunkFlag flag)
    {
        var length = plain.Length + IncrementalXChaCha20Poly1305.TagSize;
        cipher.EncryptChunk(cipherBuf.AsSpan(0, length), plain, flag);
        Span<byte> prefix = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        output.Write(prefix);
        output.Write(cipherBuf, 0, length);
    }

    private static FileStatus ReadPayload(
        Stream input,
        Stream output,
        IncrementalXChaCha20Poly1305 cipher,
        Func<long, bool> progress)
    {
        var cipherBuf = new byte[PlainChunkSize + IncrementalXChaCha20Poly1305.TagSize];
        var plainBuf = new byte[PlainChunkSize];
        try
        {
            return ReadChunks(input, output, cipher, progress, cipherBuf, plainBuf);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cipherBuf);
            CryptographicOperations.ZeroMemory(plainBuf);
        }
    }

    private static FileStatus ReadChunks(
        Stream input,
        Stream output,
        IncrementalXChaCha20Poly1305 cipher,
        Func<long, bool> progress,
        byte[] cipherBuf,
        byte[] plainBuf)
    {
        long done = 0;
        var finished = false;
        while (true)
        {
            if (!TryReadLength(input, out var length, out var end))
            {
                return end && finished ? FileStatus.Success : NotOurFormat();
            }

            if (finished || !IsChunkLength(length))
            {
                return NotOurFormat();
            }

            var chunk = ReadChunk(input, output, cipher, progress, cipherBuf, plainBuf, length, ref done);
            if (!chunk.IsSuccess)
            {
                return chunk.Status;
            }

            finished = chunk.Final;
        }
    }

    private static ChunkRead ReadChunk(
        Stream input,
        Stream output,
        IncrementalXChaCha20Poly1305 cipher,
        Func<long, bool> progress,
        byte[] cipherBuf,
        byte[] plainBuf,
        int length,
        ref long done)
    {
        if (!StreamBytes.ReadExact(input, cipherBuf.AsSpan(0, length)))
        {
            return ChunkRead.Fail(NotOurFormat());
        }

        var plainLength = length - IncrementalXChaCha20Poly1305.TagSize;
        IncrementalXChaCha20Poly1305.ChunkFlag flag;
        try
        {
            flag = cipher.DecryptChunk(plainBuf.AsSpan(0, plainLength), cipherBuf.AsSpan(0, length), ReadOnlySpan<byte>.Empty);
        }
        catch (CryptographicException)
        {
            return ChunkRead.Fail(WrongPassword());
        }

        if (plainLength > 0)
        {
            output.Write(plainBuf, 0, plainLength);
        }

        done += plainLength;
        if (!progress(done))
        {
            return ChunkRead.Fail(FileStatus.Fail(OperationError.Cancelled()));
        }

        if (!IsMessage(flag) && !IsFinal(flag))
        {
            return ChunkRead.Fail(NotOurFormat());
        }

        return ChunkRead.Ok(IsFinal(flag));
    }

    private static bool TryReadLength(Stream input, out int length, out bool end)
    {
        Span<byte> prefix = stackalloc byte[4];
        var read = 0;
        while (read < prefix.Length)
        {
            var n = input.Read(prefix[read..]);
            if (n == 0)
            {
                length = 0;
                end = read == 0;
                return false;
            }

            read += n;
        }

        length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        end = false;
        return true;
    }

    private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt, EncryptionCost cost)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var key = new byte[Argon2id.KeySize];
        try
        {
            Argon2id.DeriveKey(key, passwordBytes, salt, cost.Iterations, cost.MemoryBytes);
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static int ReadUpTo(Stream input, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = input.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                return read;
            }

            read += n;
        }

        return read;
    }

    private static bool IsChunkLength(int length)
    {
        var tag = IncrementalXChaCha20Poly1305.TagSize;
        return length >= tag && length <= PlainChunkSize + tag;
    }

    private static bool IsFinal(IncrementalXChaCha20Poly1305.ChunkFlag flag) =>
        flag == IncrementalXChaCha20Poly1305.ChunkFlag.Final;

    private static bool IsMessage(IncrementalXChaCha20Poly1305.ChunkFlag flag) =>
        flag == IncrementalXChaCha20Poly1305.ChunkFlag.Message;

    private static IncrementalXChaCha20Poly1305.ChunkFlag FinalFlag() =>
        IncrementalXChaCha20Poly1305.ChunkFlag.Final;

    private static IncrementalXChaCha20Poly1305.ChunkFlag MessageFlag() =>
        IncrementalXChaCha20Poly1305.ChunkFlag.Message;

    private static FileStatus NotOurFormat() => FileStatus.Fail(OperationError.InvalidData(CryptText.NotOurFormat));

    private static FileStatus WrongPassword() => FileStatus.Fail(OperationError.AccessDenied(CryptText.WrongPassword));

    private readonly struct ChunkRead
    {
        private ChunkRead(FileStatus status, bool final)
        {
            Status = status;
            Final = final;
        }

        public FileStatus Status { get; }

        public bool Final { get; }

        public bool IsSuccess => Status.IsSuccess;

        public static ChunkRead Ok(bool final) => new(FileStatus.Success, final);

        public static ChunkRead Fail(FileStatus status) => new(status, false);
    }
}
