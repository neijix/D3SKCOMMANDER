namespace D3skCommander.App.Features.Help;

/// <summary>One line of the shortcut list: a command's label and its gestures.</summary>
public sealed record KeyRow(string Command, string Label, string Shortcuts);
