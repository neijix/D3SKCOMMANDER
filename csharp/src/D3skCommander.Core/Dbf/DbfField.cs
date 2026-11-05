namespace D3skCommander.Core.Dbf;

/// <summary>One column of a dBase III/IV/FoxPro table. Memo files are not read.</summary>
public sealed class DbfField
{
    public DbfField(string name, char kind, byte length, byte decimals)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Kind = kind;
        Length = length;
        Decimals = decimals;
    }

    public string Name { get; }

    /// <summary>dBase type letter: <c>C</c> <c>N</c> <c>F</c> <c>L</c> <c>D</c> <c>M</c> …</summary>
    public char Kind { get; }

    public byte Length { get; }

    public byte Decimals { get; }
}
