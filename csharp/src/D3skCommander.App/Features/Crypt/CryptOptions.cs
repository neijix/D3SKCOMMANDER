using D3skCommander.Core.Crypt;

namespace D3skCommander.App.Features.Crypt;

/// <summary>The Argon2id cost for new files and vaults. Null is Core's interactive default; tests pass the minimum.</summary>
public sealed record CryptOptions(EncryptionCost? Cost = null);
