using Geralt;

namespace D3skCommander.Core.Crypt;

/// <summary>
/// Argon2id cost stored in a <c>.d3enc</c> header. <see cref="Interactive"/> is the default
/// (OWASP's interactive preset: 2 iterations, 19 MiB). <see cref="Minimum"/> is Geralt's
/// fastest accepted cost, for tests; a file is always read with the cost written in it.
/// </summary>
public readonly record struct EncryptionCost
{
    public const int MaxIterations = 16;

    public const int MaxMemoryBytes = 256 * 1024 * 1024;

    public EncryptionCost(int iterations, int memoryBytes)
    {
        if (!IsAllowed(iterations, memoryBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), "Argon2id parameters are outside the supported range.");
        }

        Iterations = iterations;
        MemoryBytes = memoryBytes;
    }

    public int Iterations { get; }

    public int MemoryBytes { get; }

    public static EncryptionCost Interactive => new(2, 19 * 1024 * 1024);

    public static EncryptionCost Minimum => new(Argon2id.MinIterations, Argon2id.MinMemorySize);

    /// <summary>Null selects <see cref="Interactive"/>. A default struct is rejected.</summary>
    public static EncryptionCost Resolve(EncryptionCost? cost)
    {
        if (cost is null)
        {
            return Interactive;
        }

        if (!TryCreate(cost.Value.Iterations, cost.Value.MemoryBytes, out var chosen))
        {
            throw new ArgumentOutOfRangeException(nameof(cost));
        }

        return chosen;
    }

    public static bool TryCreate(int iterations, int memoryBytes, out EncryptionCost cost)
    {
        if (!IsAllowed(iterations, memoryBytes))
        {
            cost = default;
            return false;
        }

        cost = new EncryptionCost(iterations, memoryBytes);
        return true;
    }

    private static bool IsAllowed(int iterations, int memoryBytes) =>
        iterations >= Argon2id.MinIterations
        && iterations <= MaxIterations
        && memoryBytes >= Argon2id.MinMemorySize
        && memoryBytes <= MaxMemoryBytes;
}
