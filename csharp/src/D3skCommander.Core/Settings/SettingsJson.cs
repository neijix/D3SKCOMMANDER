using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using D3skCommander.Core.Associations;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Toolbar;

namespace D3skCommander.Core.Settings;

/// <summary>JSON shape shared by every settings file: camelCase names, unknown fields ignored.</summary>
internal static class SettingsJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.Converters.Add(new ColumnsConverter());
        options.Converters.Add(new UserCommandConverter());
        options.Converters.Add(new ToolbarJson());
        options.Converters.Add(new AssociationsJson());
        return options;
    }

    private sealed class ColumnsConverter : JsonConverter<Columns>
    {
        public override Columns Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Columns must be an object.");
            }

            var extension = true;
            var size = true;
            var date = true;
            var attributes = true;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = PropertyName(ref reader);
                if (!TryBool(ref reader, out var value))
                {
                    continue;
                }

                Assign(name, value, ref extension, ref size, ref date, ref attributes);
            }

            return new Columns(extension, size, date, attributes);
        }

        public override void Write(Utf8JsonWriter writer, Columns value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("extension", value.Extension);
            writer.WriteBoolean("size", value.Size);
            writer.WriteBoolean("date", value.Date);
            writer.WriteBoolean("attributes", value.Attributes);
            writer.WriteEndObject();
        }

        private static void Assign(
            string? name,
            bool value,
            ref bool extension,
            ref bool size,
            ref bool date,
            ref bool attributes)
        {
            if (Same(name, "extension"))
            {
                extension = value;
                return;
            }

            if (Same(name, "size"))
            {
                size = value;
                return;
            }

            if (Same(name, "date"))
            {
                date = value;
                return;
            }

            if (Same(name, "attributes"))
            {
                attributes = value;
            }
        }
    }

    private sealed class UserCommandConverter : JsonConverter<UserCommand>
    {
        public override UserCommand Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("A user command must be an object.");
            }

            string? name = null;
            string? command = null;
            var oncePerFile = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var property = PropertyName(ref reader);
                if (Same(property, "name"))
                {
                    name = reader.GetString();
                }
                else if (Same(property, "command"))
                {
                    command = reader.GetString();
                }
                else if (Same(property, "oncePerFile"))
                {
                    oncePerFile = reader.GetBoolean();
                }
                else
                {
                    reader.Skip();
                }
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new JsonException("A user command needs a name.");
            }

            return new UserCommand(name, command ?? "", oncePerFile);
        }

        public override void Write(Utf8JsonWriter writer, UserCommand value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("name", value.Name);
            writer.WriteString("command", value.Command);
            writer.WriteBoolean("oncePerFile", value.OncePerFile);
            writer.WriteEndObject();
        }
    }

    private static string? PropertyName(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.PropertyName)
        {
            throw new JsonException("Expected a property name.");
        }

        var name = reader.GetString();
        if (!reader.Read())
        {
            throw new JsonException("Expected a property value.");
        }

        return name;
    }

    private static bool TryBool(ref Utf8JsonReader reader, out bool value)
    {
        if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
        {
            value = reader.GetBoolean();
            return true;
        }

        reader.Skip();
        value = false;
        return false;
    }

    private static bool Same(string? name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
}
