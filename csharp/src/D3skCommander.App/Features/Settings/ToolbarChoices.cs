using Avalonia.Media;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Features.Settings;

/// <summary>A catalog command in the toolbar editor's list: its id and localized label.</summary>
public sealed record CommandChoice(CommandId Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>An icon in the picker: its name (empty is "default"), caption and drawing.</summary>
public sealed record IconChoice(string Name, string Label, Geometry Glyph);
