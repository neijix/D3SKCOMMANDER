using System.Text.Json;

namespace D3skCommander.Core.FileSystem;

/// <summary>One disk or partition from <c>lsblk</c>; <see cref="ParentPath"/> is the disk holding a partition.</summary>
public sealed record BlockDevice(
    string Name,
    string Path,
    ulong Size,
    string FsType,
    string Label,
    IReadOnlyList<string> MountPoints,
    bool Removable,
    bool Hotplug,
    string Type,
    string ParentPath);

/// <summary>Reads <c>lsblk -J -b -o NAME,PATH,SIZE,FSTYPE,LABEL,MOUNTPOINTS,RM,HOTPLUG,TYPE</c> (Linux).</summary>
public static class BlockDevices
{
    /// <summary>The arguments of the <c>lsblk</c> call <see cref="ParseLsblk"/> reads.</summary>
    public static readonly string[] LsblkArguments = ["-J", "-b", "-o", "NAME,PATH,SIZE,FSTYPE,LABEL,MOUNTPOINTS,RM,HOTPLUG,TYPE"];

    private static readonly string[] SkippedTypes = ["loop", "rom"];

    private static readonly string[] SkippedFormats = ["swap", "squashfs", "LVM2_member", "crypto_LUKS", "linux_raid_member", "iso9660"];

    /// <summary>Every device, children flattened after their parent. Malformed text gives an empty list.</summary>
    public static IReadOnlyList<BlockDevice> ParseLsblk(string json)
    {
        var list = new List<BlockDevice>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return list;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("blockdevices", out var roots)
                && roots.ValueKind == JsonValueKind.Array)
            {
                Collect(roots, "", list);
            }
        }
        catch (JsonException)
        {
            list.Clear();
        }

        return list;
    }

    /// <summary>Devices holding a usable file system that nobody mounted: the ones to offer "Mount".</summary>
    public static IReadOnlyList<BlockDevice> Unmounted(IReadOnlyList<BlockDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices
            .Where(device => device.FsType.Length > 0
                && device.MountPoints.Count == 0
                && device.Size > 0
                && !Array.Exists(SkippedTypes, type => type == device.Type)
                && !Array.Exists(SkippedFormats, format => string.Equals(format, device.FsType, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>The partition mounted at <paramref name="mountPath"/> and the disk to power off, or null when it is not removable.</summary>
    public static (string Partition, string Disk)? EjectTargets(IReadOnlyList<BlockDevice> devices, string mountPath)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(mountPath);
        var wanted = mountPath.Length > 1 ? mountPath.TrimEnd('/') : mountPath;
        var device = devices.FirstOrDefault(d => d.MountPoints.Contains(wanted, StringComparer.Ordinal));
        if (device is null)
        {
            return null;
        }

        var parent = devices.FirstOrDefault(d => d.Path == device.ParentPath);
        var removable = device.Removable || device.Hotplug || (parent is not null && (parent.Removable || parent.Hotplug));
        return removable ? (device.Path, device.ParentPath.Length > 0 ? device.ParentPath : device.Path) : null;
    }

    /// <summary>True when <see cref="EjectTargets"/> finds a target.</summary>
    public static bool IsEjectable(IReadOnlyList<BlockDevice> devices, string mountPath) =>
        EjectTargets(devices, mountPath) is not null;

    private static void Collect(JsonElement array, string parentPath, List<BlockDevice> list)
    {
        foreach (var node in array.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = Text(node, "name");
            var path = Text(node, "path");
            if (path.Length == 0)
            {
                path = name.Length > 0 ? "/dev/" + name : "";
            }

            list.Add(new BlockDevice(
                name,
                path,
                Number(node, "size"),
                Text(node, "fstype"),
                Text(node, "label"),
                MountPoints(node),
                Flag(node, "rm"),
                Flag(node, "hotplug"),
                Text(node, "type"),
                parentPath));
            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                Collect(children, path, list);
            }
        }
    }

    private static string Text(JsonElement node, string key) =>
        node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static ulong Number(JsonElement node, string key)
    {
        if (!node.TryGetProperty(key, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), out var parsed) ? parsed : 0;
    }

    // Older lsblk prints "1"/"0" strings for the flags.
    private static bool Flag(JsonElement node, string key)
    {
        if (!node.TryGetProperty(key, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True
            || (value.ValueKind == JsonValueKind.String && value.GetString() == "1");
    }

    private static List<string> MountPoints(JsonElement node)
    {
        var points = new List<string>();
        if (node.TryGetProperty("mountpoints", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                {
                    points.Add(item.GetString()!);
                }
            }
        }

        return points;
    }
}
