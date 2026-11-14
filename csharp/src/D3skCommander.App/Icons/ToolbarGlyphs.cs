using Avalonia.Media;
using D3skCommander.Core.Toolbar;

namespace D3skCommander.App.Icons;

/// <summary>
/// The drawings of <see cref="ToolbarIcons.Names"/>: original one-colour glyphs on a 16-unit grid.
/// The button's foreground fills them, so they follow the light and dark themes.
/// </summary>
public static class ToolbarGlyphs
{
    private static readonly Dictionary<string, string> Data = new(StringComparer.Ordinal)
    {
        ["refresh"] = "M13.64,5.95 A6,6 0 1 1 9.04,2.09 L9.2,0.7 L13,3.2 L8.5,5.3 L8.69,4.06 A4,4 0 1 0 11.76,6.63 Z",
        ["copy"] = "F0 M2,1 h7 v1.6 h-5.4 v8.4 h-1.6z M5,4.5 h8.5 v10.5 h-8.5z M6.5,6 h5.5 v7.5 h-5.5z",
        ["move"] = "M1,6.5 h7 v-3.5 l6.5,5 l-6.5,5 v-3.5 h-7 z",
        ["new_folder"] = "F0 M1,3 h5 l1.5,1.7 h7.5 v8.8 h-14z M7.2,6.8 h1.6 v1.6 h1.6 v1.6 h-1.6 v1.6 h-1.6 v-1.6 h-1.6 v-1.6 h1.6z",
        ["delete"] = "F0 M5.5,1.5 h5 v1.5 h4 v1.6 h-13 v-1.6 h4z M3.2,5.6 h9.6 l-0.8,9 h-8z M5.9,7 h1 v5.5 h-1z M7.5,7 h1 v5.5 h-1z M9.1,7 h1 v5.5 h-1z",
        ["search"] = "F0 M6.5,1 A5.5,5.5 0 1 1 6.49,1 Z M6.5,2.8 A3.7,3.7 0 1 0 6.51,2.8 Z M9.6,11.4 l1.4,-1.4 l4,4 l-1.4,1.4 z",
        ["quick_find"] = "F0 M6.5,1 A5.5,5.5 0 1 1 6.49,1 Z M6.5,2.8 A3.7,3.7 0 1 0 6.51,2.8 Z M9.6,11.4 l1.4,-1.4 l4,4 l-1.4,1.4 z M7.6,3.4 L4.8,7.1 L6.5,7.1 L5.7,9.8 L8.6,5.8 L6.9,5.8 z",
        ["diff"] = "F0 M2,1 h8 l4,4 v10 h-12z M3.5,2.5 h5.8 v3.2 h3.2 v7.8 h-9z M5.3,7 h1.9 v-1.9 h1.4 v1.9 h1.9 v1.4 h-1.9 v1.9 h-1.4 v-1.9 h-1.9z M5.3,11.3 h5.2 v1.4 h-5.2z",
        ["disk_usage"] = "M7.5,2.5 A6,6 0 1 0 13.5,8.5 L7.5,8.5 Z M8.5,1.5 A6,6 0 0 1 14.5,7.5 L8.5,7.5 Z",
        ["compare"] = "F0 M1,2 h5.5 v12 h-5.5z M2.4,3.4 h2.7 v9.2 h-2.7z M9.5,2 h5.5 v12 h-5.5z M10.9,3.4 h2.7 v9.2 h-2.7z M7,6 h2 v1 h-2z M7,9 h2 v1 h-2z",
        ["sync"] = "M4.5,1 L8,5 L5.6,5 L5.6,14 L3.4,14 L3.4,5 L1,5 Z M11.5,15 L15,11 L12.6,11 L12.6,2 L10.4,2 L10.4,11 L8,11 Z",
        ["settings"] = "F0 M6.45,2.93 L6.57,0.64 L9.43,0.64 L9.55,2.93 L10.49,3.32 L12.19,1.78 L14.22,3.81 L12.68,5.51 L13.07,6.45 L15.36,6.57 L15.36,9.43 L13.07,9.55 L12.68,10.49 L14.22,12.19 L12.19,14.22 L10.49,12.68 L9.55,13.07 L9.43,15.36 L6.57,15.36 L6.45,13.07 L5.51,12.68 L3.81,14.22 L1.78,12.19 L3.32,10.49 L2.93,9.55 L0.64,9.43 L0.64,6.57 L2.93,6.45 L3.32,5.51 L1.78,3.81 L3.81,1.78 L5.51,3.32 Z M8,5.3 A2.7,2.7 0 1 1 7.99,5.3 Z",
        ["view"] = "F0 M8,3 C4.5,3 2,6 1,8 C2,10 4.5,13 8,13 C11.5,13 14,10 15,8 C14,6 11.5,3 8,3 Z M8,5.2 A2.8,2.8 0 1 0 8.01,5.2 Z",
        ["edit"] = "M10.2,2.5 L13.5,5.8 L12.4,6.9 L9.1,3.6 Z M8.3,4.4 L11.6,7.7 L5.2,14.1 L1.5,14.5 L1.9,10.8 Z",
        ["parent"] = "M8,1.5 L14,7.5 L10.2,7.5 L10.2,14.5 L5.8,14.5 L5.8,7.5 L2,7.5 Z",
        ["home"] = "M8,1.5 L15,8 h-2 v6 h-3.5 v-4 h-3 v4 h-3.5 v-6 h-2 z",
        ["star"] = "M8,1.1 L9.76,5.97 L14.94,6.14 L10.85,9.33 L12.29,14.31 L8,11.4 L3.71,14.31 L5.15,9.33 L1.06,6.14 L6.24,5.97 Z",
        ["terminal"] = "F0 M1,2.5 h14 v11 h-14z M2.5,4 h11 v8 h-11z M3.9,5.4 l3.4,2.6 l-3.4,2.6 l-0.9,-1.1 l2.1,-1.5 l-2.1,-1.5z M8.4,10 h3.4 v1.1 h-3.4z",
        ["pack"] = "M2,10 h12 v4.5 h-12z M6.9,1.2 h2.2 v3.6 h2.4 L8,8.4 L4.5,4.8 h2.4 z",
        ["unpack"] = "M2,10 h12 v4.5 h-12z M8,1.4 L11.5,5 h-2.4 v3.8 h-2.2 v-3.8 h-2.4 z",
        ["properties"] = "F0 M8,1 A7,7 0 1 1 7.99,1 Z M7.1,6.6 h1.8 v5.6 h-1.8z M7.1,3.6 h1.8 v1.7 h-1.8z",
        ["drive"] = "F0 M1,3.5 h14 v9 h-14z M2.5,5 h11 v3.2 h-11z M10.6,9.6 h2.6 v1.5 h-2.6z",
        ["bolt"] = "M10,1 L3.5,9 h4 l-1.5,6 l7,-9 h-4.2 z",
        ["program"] = "F0 M1,2 h14 v12 h-14z M2.5,5.4 h11 v7.1 h-11z",
        ["command"] = "F0 M1.5,1.5 h13 v13 h-13z M3,3 h10 v10 h-10z M6,5.4 l4,2.6 l-4,2.6z",
    };

    /// <summary>The names that have a drawing: all of <see cref="ToolbarIcons.Names"/>.</summary>
    public static IReadOnlyCollection<string> Names => Data.Keys;

    /// <summary>The drawing of an icon name; the generic one for a name it does not know.</summary>
    public static Geometry For(string? name) =>
        Geometry.Parse(name is not null && Data.TryGetValue(name, out var data) ? data : Data[ToolbarIcons.Generic]);
}
