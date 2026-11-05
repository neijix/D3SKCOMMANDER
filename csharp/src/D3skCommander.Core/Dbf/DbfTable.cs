namespace D3skCommander.Core.Dbf;

/// <summary>
/// Field definitions plus the records that were read. <see cref="TotalRecords"/> is the count
/// announced by the header and may be larger than <see cref="Records"/>.
/// </summary>
public sealed class DbfTable
{
    public DbfTable(
        IReadOnlyList<DbfField> fields,
        IReadOnlyList<IReadOnlyList<string>> records,
        IReadOnlyList<bool> deleted,
        uint totalRecords)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(deleted);
        Fields = fields;
        Records = records;
        Deleted = deleted;
        TotalRecords = totalRecords;
    }

    public IReadOnlyList<DbfField> Fields { get; }

    /// <summary>One row per record read, one string per field.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Records { get; }

    /// <summary>Parallel to <see cref="Records"/>: the record carries the deletion mark (<c>*</c>).</summary>
    public IReadOnlyList<bool> Deleted { get; }

    public uint TotalRecords { get; }
}
