using System.Text;
using D3skCommander.Core.Ftp;
using Xunit;

namespace D3skCommander.Core.Tests.Ftp;

public class FtpListingTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);

    private static readonly string[] Months =
    [
        "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec",
    ];

    [Fact]
    public void UnixDirectory()
    {
        var entry = Unix("drwxr-xr-x 2 user group 4096 Jan 01 12:00 dir");
        Assert.Equal("dir", entry.Name);
        Assert.True(entry.IsDirectory);
        Assert.Equal(4096UL, entry.Size);
        Assert.Equal("drwxr-xr-x", entry.Permissions);
        // 2026-01-01 12:00 UTC
        Assert.Equal(At(1_767_268_800), entry.Modified);
    }

    [Fact]
    public void UnixFileWithYearAndSpaces()
    {
        var entry = Unix("-rw-r--r--   1 ftp      ftp        123456 Dec  7  2019 my file (1).txt");
        Assert.Equal("my file (1).txt", entry.Name);
        Assert.False(entry.IsDirectory);
        Assert.Equal(123_456UL, entry.Size);
        Assert.Equal(At(1_575_676_800), entry.Modified);
    }

    [Fact]
    public void UnixTimeFormInTheFutureMeansLastYear()
    {
        // "Dec 25 10:00" listed on Oct 31 2026 is December 2025.
        var entry = Unix("-rw-r--r-- 1 u g 10 Dec 25 10:00 old.txt");
        var expected = new DateTimeOffset(2025, 12, 25, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, entry.Modified);
    }

    [Fact]
    public void UnixSymlinkKeepsItsOwnName()
    {
        var entry = Unix("lrwxrwxrwx 1 user group 1 Jan 01 12:00 a -> b");
        Assert.Equal("a", entry.Name);
        Assert.False(entry.IsDirectory);
    }

    [Fact]
    public void UnixWithoutGroupColumn()
    {
        var entry = Unix("-rw-r--r-- 1 owner 77 Mar 03 2020 f.bin");
        Assert.Equal("f.bin", entry.Name);
        Assert.Equal(77UL, entry.Size);
    }

    [Fact]
    public void DosDirectoryAndFile()
    {
        var dir = Unix("01-01-24  12:00PM       <DIR>          dir");
        Assert.Equal("dir", dir.Name);
        Assert.True(dir.IsDirectory);
        Assert.Equal(0UL, dir.Size);
        Assert.Equal(At(1_704_110_400), dir.Modified);
        var file = Unix("01-01-24  12:00PM 1234 file.txt");
        Assert.Equal("file.txt", file.Name);
        Assert.False(file.IsDirectory);
        Assert.Equal(1234UL, file.Size);
        var am = Unix("12-31-99  12:05AM 5 old file.txt");
        Assert.Equal("old file.txt", am.Name);
        Assert.Equal(At(946_598_700), am.Modified);
    }

    [Fact]
    public void NonEntriesAreSkipped()
    {
        Assert.Null(UnixLine("total 12"));
        Assert.Null(UnixLine(""));
        Assert.Null(UnixLine("drwxr-xr-x 2 user group 4096 Jan 01 12:00 ."));
        Assert.Null(UnixLine("drwxr-xr-x 2 user group 4096 Jan 01 12:00 .."));
        Assert.Null(UnixLine("garbage line here"));
    }

    [Fact]
    public void TrailingCrIsIgnored()
    {
        var entry = Unix("-rw-r--r-- 1 u g 1 Jan 01 2020 a.txt\r");
        Assert.Equal("a.txt", entry.Name);
    }

    [Fact]
    public void MlsdEntries()
    {
        var file = FtpListing.ParseMlsdLine("type=file;size=1024;modify=20240101120000;perm=r; a b.txt");
        Assert.NotNull(file);
        Assert.Equal("a b.txt", file.Name);
        Assert.False(file.IsDirectory);
        Assert.Equal(1024UL, file.Size);
        Assert.Equal(At(1_704_110_400), file.Modified);
        var dir = FtpListing.ParseMlsdLine("Type=DIR;Modify=20240101120000.123;UNIX.mode=0755; sub");
        Assert.NotNull(dir);
        Assert.True(dir.IsDirectory);
        Assert.Equal("0755", dir.Permissions);
        Assert.Equal(At(1_704_110_400), dir.Modified);
        Assert.Null(FtpListing.ParseMlsdLine("type=cdir;modify=20240101120000; ."));
        Assert.Null(FtpListing.ParseMlsdLine("type=pdir;modify=20240101120000; .."));
    }

    [Fact]
    public void MlsdAnswerWinsAndAMissingMlsdUsesList()
    {
        var mlsd = new[] { "type=file;size=1024;modify=20240101120000; a.txt" };
        var list = new[] { "-rw-r--r-- 1 u g 1 Jan 01 2020 other.txt" };
        var fromMlsd = FtpListing.EntriesFrom(mlsd, list, Today);
        Assert.Equal("a.txt", Assert.Single(fromMlsd).Name);

        var fromList = FtpListing.EntriesFrom(null, list, Today);
        var entry = Assert.Single(fromList);
        Assert.Equal("other.txt", entry.Name);
        Assert.Equal(1UL, entry.Size);
    }

    // vsftpd 3.0.5 has no MLSD, so MlsdEntries keeps v0.1's inputs. LIST and the refusal are recordings.
    [Fact]
    public void VsftpdListingsReplayAndMlsdRefusalFallsBackToList()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "ftp-vsftpd", "3.0.5");
        Assert.True(Directory.Exists(dir), dir);
        var readme = Readme(dir);
        Assert.Contains("MLSD", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIST", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("curl", readme, StringComparison.OrdinalIgnoreCase);

        var captured = Captures(dir);
        var lists = new List<Capture>();
        var refusals = new List<Capture>();
        foreach (var capture in captured)
        {
            if (capture.Parsed > 0)
            {
                lists.Add(capture);
            }
            else if (capture.Text.Trim().Length > 0)
            {
                refusals.Add(capture);
            }
        }

        Assert.True(lists.Count >= 2, "LIST of the root and of a subfolder");
        Assert.NotEmpty(refusals);
        foreach (var refusal in refusals)
        {
            Assert.Empty(FtpListing.EntriesFrom(refusal.Lines, null));
        }

        var listLines = new List<string>();
        foreach (var list in lists)
        {
            listLines.AddRange(list.Lines);
        }

        var entries = FtpListing.EntriesFrom(null, listLines, DateOnly.FromDateTime(DateTime.UtcNow));
        AssertRecordedTree(entries, listLines);
    }

    private static void AssertRecordedTree(IReadOnlyList<RemoteEntry> entries, IReadOnlyList<string> lines)
    {
        Assert.True(CountDirectories(entries) >= 2);
        Assert.Contains(entries, static entry => !entry.IsDirectory && entry.Size == 0);
        Assert.Contains(entries, static entry => !entry.IsDirectory && entry.Size >= 1024 && entry.Size < 1024 * 1024);
        Assert.Contains(entries, static entry => entry.Size > 4UL * 1024UL * 1024UL * 1024UL);
        Assert.Contains(entries, static entry => entry.Permissions.Length > 0 && entry.Permissions[0] == 'l' && !entry.IsDirectory);
        Assert.Contains(entries, static entry => entry.Name.Contains(' '));
        Assert.Contains(entries, static entry => entry.Name.Contains('é'));
        Assert.Contains(entries, static entry => entry.Name.Contains('ü'));
        Assert.Contains(entries, static entry => entry.Name.StartsWith('.'));
        AssertBothDateForms(lines);
    }

    private static int CountDirectories(IReadOnlyList<RemoteEntry> entries)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertBothDateForms(IReadOnlyList<string> lines)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sawYear = false;
        var sawTime = false;
        foreach (var line in lines)
        {
            if (FtpListing.ParseListLine(line, today) is null)
            {
                continue;
            }

            NoteDateForm(line, ref sawYear, ref sawTime);
        }

        Assert.True(sawYear, "a LIST line dated with a year");
        Assert.True(sawTime, "a LIST line dated with a time");
    }

    private static void NoteDateForm(string line, ref bool sawYear, ref bool sawTime)
    {
        var at = MonthAt(line);
        if (at < 0 || at + 3 > line.Length)
        {
            return;
        }

        var rest = line[(at + 3)..];
        var colon = rest.IndexOf(':');
        if (colon >= 0 && colon <= 8)
        {
            sawTime = true;
            return;
        }

        if (HasYearToken(rest))
        {
            sawYear = true;
        }
    }

    private static int MonthAt(string line)
    {
        var best = -1;
        foreach (var month in Months)
        {
            var at = line.IndexOf(" " + month + " ", StringComparison.OrdinalIgnoreCase);
            if (at >= 0 && (best < 0 || at < best))
            {
                best = at + 1;
            }
        }

        return best;
    }

    private static bool HasYearToken(string rest)
    {
        var window = rest.Length > 16 ? rest[..16] : rest;
        var i = 0;
        while (i < window.Length)
        {
            if (!char.IsAsciiDigit(window[i]))
            {
                i++;
                continue;
            }

            var digits = 0;
            while (i + digits < window.Length && char.IsAsciiDigit(window[i + digits]))
            {
                digits++;
            }

            if (digits == 4)
            {
                return true;
            }

            i += digits;
        }

        return false;
    }

    private static List<Capture> Captures(string dir)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var found = new List<Capture>();
        foreach (var path in Directory.GetFiles(dir).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("README", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(path, Encoding.UTF8);
            var lines = SplitLines(text);
            var parsed = 0;
            foreach (var line in lines)
            {
                if (FtpListing.ParseListLine(line, today) is not null)
                {
                    parsed++;
                }
            }

            found.Add(new Capture(text, lines, parsed));
        }

        return found;
    }

    private static string Readme(string dir)
    {
        string? path = null;
        foreach (var candidate in Directory.GetFiles(dir))
        {
            if (Path.GetFileName(candidate).StartsWith("README", StringComparison.OrdinalIgnoreCase))
            {
                path = candidate;
                break;
            }
        }

        Assert.NotNull(path);
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string[] SplitLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Split('\n');
    }

    private static RemoteEntry Unix(string line)
    {
        var entry = UnixLine(line);
        Assert.NotNull(entry);
        return entry;
    }

    private static RemoteEntry? UnixLine(string line) => FtpListing.ParseListLine(line, Today);

    private static DateTimeOffset At(long secs) => DateTimeOffset.FromUnixTimeSeconds(secs);

    private sealed record Capture(string Text, string[] Lines, int Parsed);
}
