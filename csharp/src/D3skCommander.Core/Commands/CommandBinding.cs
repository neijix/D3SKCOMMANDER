namespace D3skCommander.Core.Commands;

/// <summary>One default gesture for a command. A command may have more than one.</summary>
public readonly record struct CommandBinding(Gesture Gesture, CommandId Command);
