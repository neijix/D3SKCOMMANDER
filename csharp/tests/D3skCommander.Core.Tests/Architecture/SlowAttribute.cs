using Xunit.v3;

namespace D3skCommander.Testing;

/// <summary>The one slow-tier marker: <c>csharp/check.sh</c> skips it, <c>--full</c> runs it.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SlowAttribute : Attribute, ITraitAttribute
{
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new("Tier", "Slow")];
}
