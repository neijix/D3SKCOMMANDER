using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace D3skCommander.Core.Catalog;

/// <summary>One JSON object per line. The header is <c>schema</c>, <c>root</c>, <c>created</c>; an entry is <c>path</c>, <c>size</c>, <c>mtime</c>, <c>isDir</c>.</summary>
internal static class CatalogJson
{
    private static readonly JsonWriterOptions LineOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public static void WriteHeader(StreamWriter writer, string root, DateTimeOffset created)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, LineOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", DiskCatalog.SchemaVersion);
            json.WriteString("root", root);
            json.WriteString("created", Stamp(created));
            json.WriteEndObject();
            json.Flush();
        }

        WriteLine(writer, buffer);
    }

    public static void WriteEntry(StreamWriter writer, CatalogEntry entry)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, LineOptions))
        {
            json.WriteStartObject();
            json.WriteString("path", entry.Path);
            json.WriteNumber("size", entry.Size);
            WriteMtime(json, entry.Mtime);
            json.WriteBoolean("isDir", entry.IsDir);
            json.WriteEndObject();
            json.Flush();
        }

        WriteLine(writer, buffer);
    }

    public static bool IsObject(string line)
    {
        if (!TryDocument(line, out var document))
        {
            return false;
        }

        document.Dispose();
        return true;
    }

    public static bool TryHeader(string line, out CatalogHeader? header)
    {
        header = null;
        if (!TryDocument(line, out var document))
        {
            return false;
        }

        using (document)
        {
            return ReadHeader(document.RootElement, out header);
        }
    }

    public static bool TryEntry(string line, out CatalogEntry? entry)
    {
        entry = null;
        if (!TryDocument(line, out var document))
        {
            return false;
        }

        using (document)
        {
            return ReadEntry(document.RootElement, out entry);
        }
    }

    private static bool ReadHeader(JsonElement element, out CatalogHeader? header)
    {
        header = null;
        if (!TryInt(element, "schema", out var schema) || schema != DiskCatalog.SchemaVersion)
        {
            return false;
        }

        if (!TryString(element, "root", out var root))
        {
            return false;
        }

        if (!TryStamp(element, "created", out var created))
        {
            return false;
        }

        header = new CatalogHeader(schema, root, created);
        return true;
    }

    private static bool ReadEntry(JsonElement element, out CatalogEntry? entry)
    {
        entry = null;
        if (!TryString(element, "path", out var path))
        {
            return false;
        }

        if (!TryLong(element, "size", out var size) || size < 0)
        {
            return false;
        }

        if (!TryBool(element, "isDir", out var isDir))
        {
            return false;
        }

        if (!TryOptionalStamp(element, out var mtime))
        {
            return false;
        }

        entry = new CatalogEntry(path, size, mtime, isDir);
        return true;
    }

    private static void WriteMtime(Utf8JsonWriter json, DateTimeOffset? mtime)
    {
        if (mtime is null)
        {
            json.WriteNull("mtime");
            return;
        }

        json.WriteString("mtime", Stamp(mtime.Value));
    }

    private static string Stamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static bool TryOptionalStamp(JsonElement element, out DateTimeOffset? mtime)
    {
        mtime = null;
        if (!element.TryGetProperty("mtime", out var stamp) || stamp.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (!TryStampValue(stamp, out var parsed))
        {
            return false;
        }

        mtime = parsed;
        return true;
    }

    private static bool TryStamp(JsonElement element, string name, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(name, out var stamp) && TryStampValue(stamp, out value);
    }

    private static bool TryStampValue(JsonElement stamp, out DateTimeOffset value)
    {
        value = default;
        if (stamp.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = stamp.GetString();
        return !string.IsNullOrEmpty(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryLong(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }

    private static bool TryBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        return property.ValueKind == JsonValueKind.False;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryDocument(string line, out JsonDocument document)
    {
        try
        {
            var parsed = JsonDocument.Parse(line);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                document = null!;
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private static void WriteLine(StreamWriter writer, MemoryStream buffer)
    {
        writer.Write(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        writer.Write('\n');
    }
}
