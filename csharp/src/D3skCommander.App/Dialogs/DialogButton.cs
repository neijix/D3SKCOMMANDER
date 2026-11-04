using System.Windows.Input;

namespace D3skCommander.App.Dialogs;

/// <summary>A labeled command the dialog view draws as a button.</summary>
public readonly record struct DialogButton(string Content, ICommand Command);
