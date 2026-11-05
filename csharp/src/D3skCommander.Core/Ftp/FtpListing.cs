using System.Globalization;

namespace D3skCommander.Core.Ftp;

/// <summary>MLSD facts (RFC 3659) and the unix and DOS/IIS LIST dialects.</summary>
public static class FtpListing
{
    private static readonly string[] Months =
    [
        "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec",
    ];

    /// <summary>
    /// One LIST line. Null for <c>total</c>, a blank line, <c>.</c> and <c>..</c>.
    /// </summary>
    /// <param name="today">
    /// UTC date for a unix line that has a time and no year (the latest such date that is not
    /// in the future). Defaults to today, UTC.
    /// </param>
    public static RemoteEntry? ParseListLine(string line, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        var trimmed = TrimLine(line);
        var date = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = StartsWithDigit(trimmed) ? ParseDos(trimmed) : ParseUnix(trimmed, date);
        if (entry is null || entry.Name == "." || entry.Name == "..")
        {
            return null;
        }

        return entry;
    }

    /// <summary>One MLSD line (<c>fact=value; name</c>). Null for <c>cdir</c>, <c>pdir</c> and a malformed line.</summary>
    public static RemoteEntry? ParseMlsdLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var trimmed = TrimLine(line);
        var space = trimmed.IndexOf(' ');
        if (space < 0 || space >= trimmed.Length - 1)
        {
            return null;
        }

        var name = trimmed[(space + 1)..];
        if (!TryApplyFacts(trimmed[..space], out var isDirectory, out var size, out var modified, out var permissions))
        {
            return null;
        }

        return new RemoteEntry(name, isDirectory, size, modified, permissions);
    }

    /// <summary>
    /// MLSD lines when the server answered, otherwise a plain LIST.
    /// A refused MLSD (vsftpd has none) is a null <paramref name="mlsdLines"/>.
    /// </summary>
    public static IReadOnlyList<RemoteEntry> EntriesFrom(
        IReadOnlyList<string>? mlsdLines,
        IReadOnlyList<string>? listLines,
        DateOnly? today = null)
    {
        if (mlsdLines is not null)
        {
            return Collect(mlsdLines, true, default);
        }

        if (listLines is null)
        {
            throw new ArgumentNullException(nameof(listLines));
        }

        return Collect(listLines, false, today ?? DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private static List<RemoteEntry> Collect(IReadOnlyList<string> lines, bool mlsd, DateOnly today)
    {
        var entries = new List<RemoteEntry>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            ArgumentNullException.ThrowIfNull(line);
            var entry = mlsd ? ParseMlsdLine(line) : ParseListLine(line, today);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static string TrimLine(string line)
    {
        var end = line.Length;
        while (end > 0 && (line[end - 1] == '\r' || line[end - 1] == '\n'))
        {
            end--;
        }

        return end == line.Length ? line : line[..end];
    }

    private static bool StartsWithDigit(string line)
    {
        var i = 0;
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            i++;
        }

        return i < line.Length && char.IsAsciiDigit(line[i]);
    }

    private static RemoteEntry? ParseUnix(string line, DateOnly today)
    {
        var perms = Next(line);
        if (perms is null || perms.Value.Word.Length < 10 || !IsListKind(perms.Value.Word[0]))
        {
            return null;
        }

        var links = Next(perms.Value.Remainder);
        var owner = links is null ? null : Next(links.Value.Remainder);
        if (owner is null
            || !TrySizeAndMonth(owner.Value.Remainder, out var sizeToken, out var monthToken, out var afterMonth))
        {
            return null;
        }

        var day = Next(afterMonth);
        var when = day is null ? null : Next(day.Value.Remainder);
        if (when is null
            || !ulong.TryParse(sizeToken, NumberStyles.None, CultureInfo.InvariantCulture, out var size))
        {
            return null;
        }

        var kind = perms.Value.Word[0];
        var name = SymlinkName(kind, when.Value.Remainder);
        if (name.Length == 0)
        {
            return null;
        }

        var modified = UnixDate(monthToken, day!.Value.Word, when.Value.Word, today);
        return new RemoteEntry(name, kind == 'd', size, modified, perms.Value.Word);
    }

    private static bool IsListKind(char kind) =>
        kind == 'd' || kind == '-' || kind == 'l' || kind == 'b' || kind == 'c' || kind == 'p' || kind == 's';

    // Some servers omit the group column: the size is then the only number before the month.
    private static bool TrySizeAndMonth(string rest, out string sizeToken, out string monthToken, out string afterMonth)
    {
        sizeToken = "";
        monthToken = "";
        afterMonth = "";
        var first = Next(rest);
        if (first is null)
        {
            return false;
        }

        var second = Next(first.Value.Remainder);
        if (second is null)
        {
            return false;
        }

        if (MonthNumber(second.Value.Word) is null)
        {
            sizeToken = second.Value.Word;
            var month = Next(second.Value.Remainder);
            if (month is null)
            {
                return false;
            }

            monthToken = month.Value.Word;
            afterMonth = month.Value.Remainder;
            return true;
        }

        sizeToken = first.Value.Word;
        monthToken = second.Value.Word;
        afterMonth = second.Value.Remainder;
        return true;
    }

    private static string SymlinkName(char kind, string name)
    {
        if (kind != 'l')
        {
            return name;
        }

        var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
        return arrow < 0 ? name : name[..arrow];
    }

    private static DateTimeOffset? UnixDate(string monthName, string dayText, string when, DateOnly today)
    {
        var month = MonthNumber(monthName);
        if (month is null || !int.TryParse(dayText, NumberStyles.None, CultureInfo.InvariantCulture, out var day))
        {
            return null;
        }

        var colon = when.IndexOf(':');
        int year;
        var time = TimeOnly.MinValue;
        if (colon >= 0)
        {
            if (!TryHourMinute(when[..colon], when[(colon + 1)..], out var hour, out var minute))
            {
                return null;
            }

            time = new TimeOnly(hour, minute, 0);
            year = YearOfRecentDate(today, month.Value, day);
        }
        else if (!int.TryParse(when, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out year))
        {
            return null;
        }

        if (!TryDate(year, month.Value, day, out var date))
        {
            return null;
        }

        return new DateTimeOffset(date.ToDateTime(time), TimeSpan.Zero);
    }

    // `ls` prints a time instead of the year for a date within about the last six months.
    // A date still in the future (past tomorrow) is last year.
    private static int YearOfRecentDate(DateOnly today, int month, int day)
    {
        if (TryDate(today.Year, month, day, out var thisYear) && thisYear <= today.AddDays(1))
        {
            return today.Year;
        }

        return today.Year - 1;
    }

    private static RemoteEntry? ParseDos(string line)
    {
        var date = Next(line);
        var time = date is null ? null : Next(date.Value.Remainder);
        var sizeToken = time is null ? null : Next(time.Value.Remainder);
        if (sizeToken is null || sizeToken.Value.Remainder.Length == 0 || time is null || date is null)
        {
            return null;
        }

        var isDirectory = sizeToken.Value.Word.Equals("<DIR>", StringComparison.OrdinalIgnoreCase);
        if (!isDirectory
            && !ulong.TryParse(
                sizeToken.Value.Word.Replace(",", "", StringComparison.Ordinal),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _))
        {
            return null;
        }

        var size = isDirectory
            ? 0UL
            : ulong.Parse(
                sizeToken.Value.Word.Replace(",", "", StringComparison.Ordinal),
                NumberStyles.None,
                CultureInfo.InvariantCulture);
        return new RemoteEntry(sizeToken.Value.Remainder, isDirectory, size, DosDate(date.Value.Word, time.Value.Word), "");
    }

    private static DateTimeOffset? DosDate(string date, string time)
    {
        var parts = date.Split('-', '/');
        if (parts.Length < 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }

        if (year < 100)
        {
            year += year < 70 ? 2000 : 1900;
        }

        if (!TryClock(time, out var hour, out var minute) || !TryDate(year, month, day, out var when))
        {
            return null;
        }

        return new DateTimeOffset(when.ToDateTime(new TimeOnly(hour, minute, 0)), TimeSpan.Zero);
    }

    private static bool TryClock(string time, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        var upper = time.ToUpperInvariant();
        string clock;
        bool? afternoon;
        if (upper.EndsWith("PM", StringComparison.Ordinal))
        {
            clock = upper[..^2];
            afternoon = true;
        }
        else if (upper.EndsWith("AM", StringComparison.Ordinal))
        {
            clock = upper[..^2];
            afternoon = false;
        }
        else
        {
            clock = upper;
            afternoon = null;
        }

        var colon = clock.IndexOf(':');
        if (colon < 0 || !TryHourMinute(clock[..colon], clock[(colon + 1)..], out hour, out minute))
        {
            return false;
        }

        if (afternoon == true && hour < 12)
        {
            hour += 12;
        }
        else if (afternoon == false && hour == 12)
        {
            hour = 0;
        }

        return hour <= 23;
    }

    private static bool TryHourMinute(string hourText, string minuteText, out int hour, out int minute)
    {
        minute = 0;
        return int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out hour)
            && int.TryParse(minuteText, NumberStyles.None, CultureInfo.InvariantCulture, out minute)
            && hour <= 23
            && minute <= 59;
    }

    private static bool TryApplyFacts(
        string facts,
        out bool isDirectory,
        out ulong size,
        out DateTimeOffset? modified,
        out string permissions)
    {
        isDirectory = false;
        size = 0;
        modified = null;
        permissions = "";
        foreach (var fact in facts.Split(';'))
        {
            if (fact.Length == 0)
            {
                continue;
            }

            var eq = fact.IndexOf('=');
            if (eq < 0)
            {
                return false;
            }

            if (!ApplyFact(fact[..eq], fact[(eq + 1)..], ref isDirectory, ref size, ref modified, ref permissions))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ApplyFact(
        string key,
        string value,
        ref bool isDirectory,
        ref ulong size,
        ref DateTimeOffset? modified,
        ref string permissions)
    {
        if (key.Equals("type", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyType(value, ref isDirectory);
        }

        if (key.Equals("size", StringComparison.OrdinalIgnoreCase) || key.Equals("sizd", StringComparison.OrdinalIgnoreCase))
        {
            size = ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            return true;
        }

        if (key.Equals("modify", StringComparison.OrdinalIgnoreCase))
        {
            modified = MlsdTime(value);
            return true;
        }

        if (key.Equals("unix.mode", StringComparison.OrdinalIgnoreCase))
        {
            permissions = value;
        }

        return true;
    }

    private static bool ApplyType(string value, ref bool isDirectory)
    {
        if (value.Equals("cdir", StringComparison.OrdinalIgnoreCase) || value.Equals("pdir", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Equals("dir", StringComparison.OrdinalIgnoreCase))
        {
            isDirectory = true;
        }

        return true;
    }

    private static DateTimeOffset? MlsdTime(string value)
    {
        var dot = value.IndexOf('.');
        var digits = dot < 0 ? value : value[..dot];
        if (!DateTime.TryParseExact(
                digits,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return null;
        }

        return new DateTimeOffset(parsed.Year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, parsed.Second, TimeSpan.Zero);
    }

    private static int? MonthNumber(string name)
    {
        if (name.Length != 3)
        {
            return null;
        }

        var lower = name.ToLowerInvariant();
        for (var i = 0; i < Months.Length; i++)
        {
            if (Months[i] == lower)
            {
                return i + 1;
            }
        }

        return null;
    }

    private static bool TryDate(int year, int month, int day, out DateOnly date)
    {
        date = default;
        if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > 31)
        {
            return false;
        }

        var text = year.ToString("0000", CultureInfo.InvariantCulture)
            + month.ToString("00", CultureInfo.InvariantCulture)
            + day.ToString("00", CultureInfo.InvariantCulture);
        if (!DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        date = DateOnly.FromDateTime(parsed);
        return true;
    }

    private static (string Word, string Remainder)? Next(string text)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i >= text.Length)
        {
            return null;
        }

        var start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        var word = text[start..i];
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return (word, text[i..]);
    }
}
