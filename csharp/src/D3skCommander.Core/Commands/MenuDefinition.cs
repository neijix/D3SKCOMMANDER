namespace D3skCommander.Core.Commands;

/// <summary>One menu. A null item is a separator. <see cref="TitleKey"/> is an en/fr catalog key.</summary>
public sealed class MenuDefinition
{
    public MenuDefinition(string titleKey, IReadOnlyList<CommandId?> items)
    {
        ArgumentException.ThrowIfNullOrEmpty(titleKey);
        ArgumentNullException.ThrowIfNull(items);
        TitleKey = titleKey;
        Items = items;
    }

    public string TitleKey { get; }

    public IReadOnlyList<CommandId?> Items { get; }
}
