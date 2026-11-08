namespace D3skCommander.Core.Install;

/// <summary>The <c>.d3skcommander-install</c> record <c>install.sh</c> writes: UTF-8 <c>key=value</c> lines.</summary>
public static class InstallRecord
{
    public const string FileName = ".d3skcommander-install";

    /// <summary>Rewrites the <c>version=</c> line of the record in <paramref name="folder"/>, keeping the other lines.</summary>
    public static void WriteVersion(string folder, string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(version);
        var path = Path.Combine(folder, FileName);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var index = lines.FindIndex(line => line.StartsWith("version=", StringComparison.Ordinal));
        if (index >= 0)
        {
            lines[index] = "version=" + version;
        }
        else
        {
            lines.Add("version=" + version);
        }

        File.WriteAllText(path, string.Join('\n', lines) + "\n", new System.Text.UTF8Encoding(false));
    }
}
