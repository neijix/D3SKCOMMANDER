using D3skCommander.Core.Common;
using D3skCommander.Core.Crypt;

namespace D3skCommander.App.Features.Crypt;

/// <summary>
/// The master-password unlock flow of the password store. Features that need a stored secret
/// (the FTP window) call <see cref="UnlockAsync"/>: the master is asked once per session, then the opened store is reused.
/// </summary>
public interface IPasswordVault
{
    /// <summary>The store when unlocked, else null.</summary>
    PasswordStore? Current { get; }

    /// <summary>
    /// The opened store, asking for the master password when locked. The first use creates the vault
    /// (master asked twice). Null when the user cancels or the file cannot be opened.
    /// </summary>
    Task<PasswordStore?> UnlockAsync();

    /// <summary>Writes the current store atomically. Fails when locked.</summary>
    Task<OperationError?> SaveAsync();

    /// <summary>Forgets the opened store; the next <see cref="UnlockAsync"/> asks again.</summary>
    void Lock();
}
