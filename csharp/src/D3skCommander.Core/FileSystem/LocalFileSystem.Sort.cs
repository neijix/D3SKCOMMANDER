using System.Text;

namespace D3skCommander.Core.FileSystem;

public enum SortKey
{
    Name,
    Extension,
    Size,
    Modified,
    Attributes,
}

/// <summary>Column and direction. The default is name, ascending.</summary>
public readonly record struct SortOrder(SortKey Key, bool Descending)
{
    public static SortOrder NameAscending => new(SortKey.Name, false);
}

public static partial class LocalFileSystem
{
    /// <summary>Case-insensitive order where digit runs compare as numbers ("file2" &lt; "file10").</summary>
    public static int CompareNatural(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var i = 0;
        var j = 0;
        while (true)
        {
            var compared = CompareStep(left, right, ref i, ref j);
            if (compared is not null)
            {
                return compared.Value;
            }
        }
    }

    /// <summary>".." first, then folders, then files. Folders sort by name unless the key is the date.</summary>
    public static void Sort(List<Entry> entries, SortOrder order)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sorted = entries.OrderBy(entry => entry, new EntryOrder(order)).ToList();
        entries.Clear();
        entries.AddRange(sorted);
    }

    // Null means the step only consumed equal numbers and the walk continues.
    private static int? CompareStep(string left, string right, ref int i, ref int j)
    {
        var leftDone = i >= left.Length;
        var rightDone = j >= right.Length;
        if (leftDone && rightDone)
        {
            return string.CompareOrdinal(left, right);
        }

        if (leftDone)
        {
            return -1;
        }

        if (rightDone)
        {
            return 1;
        }

        var leftRune = Rune.GetRuneAt(left, i);
        var rightRune = Rune.GetRuneAt(right, j);
        if (IsAsciiDigit(leftRune) && IsAsciiDigit(rightRune))
        {
            var leftNumber = TakeNumber(left, ref i);
            var rightNumber = TakeNumber(right, ref j);
            var compared = CompareNumbers(leftNumber, rightNumber);
            return compared == 0 ? null : compared;
        }

        var comparedChars = CompareLower(leftRune, rightRune);
        if (comparedChars != 0)
        {
            return comparedChars;
        }

        i += leftRune.Utf16SequenceLength;
        j += rightRune.Utf16SequenceLength;
        return null;
    }

    private static int CompareNumbers(string left, string right)
    {
        var leftTrimmed = TrimZeros(left);
        var rightTrimmed = TrimZeros(right);
        var byLength = leftTrimmed.Length.CompareTo(rightTrimmed.Length);
        if (byLength != 0)
        {
            return byLength;
        }

        return string.CompareOrdinal(leftTrimmed, rightTrimmed);
    }

    private static string TrimZeros(string digits)
    {
        var start = 0;
        while (start < digits.Length && digits[start] == '0')
        {
            start++;
        }

        return digits[start..];
    }

    private static string TakeNumber(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && text[index] is >= '0' and <= '9')
        {
            index++;
        }

        return text[start..index];
    }

    private static bool IsAsciiDigit(Rune rune) => rune.Value is >= '0' and <= '9';

    private static int CompareLower(Rune left, Rune right)
    {
        var a = left.ToString().ToLowerInvariant();
        var b = right.ToString().ToLowerInvariant();
        return string.CompareOrdinal(a, b);
    }

    private sealed class EntryOrder : IComparer<Entry>
    {
        private readonly SortOrder _order;

        public EntryOrder(SortOrder order) => _order = order;

        public int Compare(Entry? left, Entry? right)
        {
            if (left is null || right is null)
            {
                return NullOrder(left, right);
            }

            if (left.IsParent != right.IsParent)
            {
                return BoolFirst(left.IsParent, right.IsParent);
            }

            if (left.IsDirectory != right.IsDirectory)
            {
                return BoolFirst(left.IsDirectory, right.IsDirectory);
            }

            // Descending flips the column, not the ".." / folder grouping above.
            var key = left.IsDirectory && _order.Key != SortKey.Modified ? SortKey.Name : _order.Key;
            var compared = CompareKey(left, right, key);
            return _order.Descending ? -compared : compared;
        }

        private static int NullOrder(Entry? left, Entry? right)
        {
            if (left is null && right is null)
            {
                return 0;
            }

            return left is null ? -1 : 1;
        }

        // True sorts before false: a parent before other rows, a folder before a file.
        private static int BoolFirst(bool left, bool right) => right.CompareTo(left);

        private static int CompareKey(Entry left, Entry right, SortKey key)
        {
            if (key == SortKey.Name)
            {
                return CompareNatural(left.Name, right.Name);
            }

            if (key == SortKey.Extension)
            {
                return ThenName(CompareNatural(left.Extension, right.Extension), left, right);
            }

            if (key == SortKey.Size)
            {
                return ThenName(left.Size.CompareTo(right.Size), left, right);
            }

            if (key == SortKey.Modified)
            {
                return CompareModified(left.Modified, right.Modified);
            }

            if (key == SortKey.Attributes)
            {
                return ThenName(string.CompareOrdinal(left.Attributes, right.Attributes), left, right);
            }

            throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown sort key.");
        }

        private static int ThenName(int compared, Entry left, Entry right)
        {
            if (compared != 0)
            {
                return compared;
            }

            return CompareNatural(left.Name, right.Name);
        }

        private static int CompareModified(DateTimeOffset? left, DateTimeOffset? right)
        {
            if (left is null && right is null)
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            return left.Value.CompareTo(right.Value);
        }
    }
}
