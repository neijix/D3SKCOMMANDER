using System.Text.Json;
using System.Text.Json.Serialization;
using D3skCommander.Core.Commands;

namespace D3skCommander.Core.Toolbar;

/// <summary>
/// The toolbar list in the preferences file. Each entry reads on its own: one with an unknown kind, an
/// unknown command, a wrong type or no target is dropped and the others stay.
/// </summary>
internal sealed class ToolbarJson : JsonConverter<List<ToolbarItem>>
{
    public override List<ToolbarItem> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return ToolbarDefaults.Items();
        }

        var items = new List<ToolbarItem>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            using var element = JsonDocument.ParseValue(ref reader);
            if (ReadItem(element.RootElement) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    public override void Write(Utf8JsonWriter writer, List<ToolbarItem> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", item.Kind.ToString());
            if (item.Command is CommandId command)
            {
                writer.WriteString("command", command.ToString());
            }

            WriteText(writer, "userCommand", item.UserCommandName);
            WriteText(writer, "program", item.Program);
            WriteText(writer, "arguments", item.Arguments);
            WriteText(writer, "workingFolder", item.WorkingFolder);
            WriteText(writer, "icon", item.Icon);
            WriteText(writer, "tooltip", item.Tooltip);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteText(Utf8JsonWriter writer, string name, string value)
    {
        if (value.Length > 0)
        {
            writer.WriteString(name, value);
        }
    }

    private static ToolbarItem? ReadItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !Enum.TryParse<ToolbarItemKind>(Text(element, "kind"), true, out var kind)
            || !Enum.IsDefined(kind))
        {
            return null;
        }

        CommandId? command = null;
        if (kind == ToolbarItemKind.Command)
        {
            command = CommandCatalog.FromName(Text(element, "command"));
        }

        var item = new ToolbarItem(
            kind,
            command,
            Text(element, "userCommand"),
            Text(element, "program"),
            Text(element, "arguments"),
            Text(element, "workingFolder"),
            Text(element, "icon"),
            Text(element, "tooltip"));
        return ToolbarItems.Clean(item);
    }

    private static string Text(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString() ?? "";
            }
        }

        return "";
    }
}
