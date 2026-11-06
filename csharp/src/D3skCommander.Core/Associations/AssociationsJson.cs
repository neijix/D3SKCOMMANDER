using System.Text.Json;
using System.Text.Json.Serialization;

namespace D3skCommander.Core.Associations;

/// <summary>
/// The association rules in the preferences file. Each entry reads on its own: a rule that is not an
/// object or has no masks is dropped, an action or extra without a program is dropped, and the others stay.
/// </summary>
internal sealed class AssociationsJson : JsonConverter<List<AssociationRule>>
{
    public override List<AssociationRule> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rules = new List<AssociationRule>();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return rules;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            using var element = JsonDocument.ParseValue(ref reader);
            if (ReadRule(element.RootElement) is { } rule)
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    public override void Write(Utf8JsonWriter writer, List<AssociationRule> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var rule in value)
        {
            writer.WriteStartObject();
            writer.WriteString("name", rule.Name);
            writer.WriteString("masks", rule.Masks);
            WriteAction(writer, "open", rule.Open);
            WriteAction(writer, "view", rule.View);
            WriteAction(writer, "edit", rule.Edit);
            if (rule.Extras.Count > 0)
            {
                writer.WriteStartArray("extras");
                foreach (var extra in rule.Extras)
                {
                    WriteBody(writer, extra);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteAction(Utf8JsonWriter writer, string name, AssociationAction? action)
    {
        if (action is not null)
        {
            writer.WritePropertyName(name);
            WriteBody(writer, action);
        }
    }

    private static void WriteBody(Utf8JsonWriter writer, AssociationAction action)
    {
        writer.WriteStartObject();
        WriteText(writer, "name", action.Name);
        writer.WriteString("program", action.Program);
        WriteText(writer, "arguments", action.Arguments);
        WriteText(writer, "workingFolder", action.WorkingFolder);
        writer.WriteEndObject();
    }

    private static void WriteText(Utf8JsonWriter writer, string name, string value)
    {
        if (value.Length > 0)
        {
            writer.WriteString(name, value);
        }
    }

    private static AssociationRule? ReadRule(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || Text(element, "masks").Trim().Length == 0)
        {
            return null;
        }

        var extras = new List<AssociationAction>();
        if (Property(element, "extras") is { ValueKind: JsonValueKind.Array } list)
        {
            extras.AddRange(list.EnumerateArray().Select(item => ReadAction(item)).OfType<AssociationAction>());
        }

        return new AssociationRule(
            Text(element, "name"),
            Text(element, "masks"),
            ReadAction(Property(element, "open")),
            ReadAction(Property(element, "view")),
            ReadAction(Property(element, "edit")),
            extras);
    }

    private static AssociationAction? ReadAction(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } body || Text(body, "program").Trim().Length == 0)
        {
            return null;
        }

        return new AssociationAction(Text(body, "program"), Text(body, "arguments"), Text(body, "workingFolder"), Text(body, "name"));
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";
}
