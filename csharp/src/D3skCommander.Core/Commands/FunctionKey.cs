namespace D3skCommander.Core.Commands;

/// <summary>One key on the function-key bar. <see cref="LabelKey"/> is the short <c>fk.*</c> label, not the menu label.</summary>
public readonly record struct FunctionKey(CommandId Command, string LabelKey);
