namespace D3skCommander.Core.Commands;

/// <summary>
/// v0.1 <c>bindings()</c>, most modifiers first. OrderBy is stable, so equal counts keep declaration order.
/// </summary>
internal static class CommandBindings
{
    public static IReadOnlyList<CommandBinding> All { get; } = Sort(Build());

    private static List<CommandBinding> Build()
    {
        var list = new List<CommandBinding>();
        AddModified(list);
        AddPlain(list);
        return list;
    }

    private static void AddModified(List<CommandBinding> list)
    {
        var ctrlShift = GestureModifiers.Ctrl | GestureModifiers.Shift;
        Add(list, ctrlShift, GestureKey.F1, CommandId.ViewThumbs);
        Add(list, GestureModifiers.Shift, GestureKey.F4, CommandId.EditNew);
        Add(list, GestureModifiers.Shift, GestureKey.F6, CommandId.RenameInPlace);
        Add(list, GestureModifiers.Shift, GestureKey.F8, CommandId.DeletePermanent);
        Add(list, GestureModifiers.Shift, GestureKey.Delete, CommandId.DeletePermanent);
        Add(list, GestureModifiers.Shift, GestureKey.F2, CommandId.ComparePanels);
        Add(list, GestureModifiers.Alt, GestureKey.F4, CommandId.Quit);
        Add(list, GestureModifiers.Alt, GestureKey.F5, CommandId.Pack);
        Add(list, GestureModifiers.Alt, GestureKey.F9, CommandId.Unpack);
        Add(list, GestureModifiers.Alt, GestureKey.F7, CommandId.Search);
        Add(list, GestureModifiers.Alt, GestureKey.F1, CommandId.DrivesLeft);
        Add(list, GestureModifiers.Alt, GestureKey.F2, CommandId.DrivesRight);
        Add(list, GestureModifiers.Alt, GestureKey.Enter, CommandId.Properties);
        Add(list, GestureModifiers.Alt, GestureKey.Left, CommandId.Back);
        Add(list, GestureModifiers.Alt, GestureKey.Right, CommandId.Forward);
        Add(list, GestureModifiers.Alt, GestureKey.Down, CommandId.History);
        Add(list, GestureModifiers.Ctrl, GestureKey.F1, CommandId.ViewBrief);
        Add(list, GestureModifiers.Ctrl, GestureKey.F2, CommandId.ViewFull);
        Add(list, GestureModifiers.Ctrl, GestureKey.F3, CommandId.SortName);
        Add(list, GestureModifiers.Ctrl, GestureKey.F4, CommandId.SortExt);
        Add(list, GestureModifiers.Ctrl, GestureKey.F5, CommandId.SortDate);
        Add(list, GestureModifiers.Ctrl, GestureKey.F6, CommandId.SortSize);
        Add(list, GestureModifiers.Ctrl, GestureKey.A, CommandId.SelectAll);
        Add(list, GestureModifiers.Ctrl, GestureKey.B, CommandId.Branch);
        Add(list, GestureModifiers.Ctrl, GestureKey.C, CommandId.ClipCopy);
        Add(list, GestureModifiers.Ctrl, GestureKey.D, CommandId.Hotlist);
        Add(list, GestureModifiers.Ctrl, GestureKey.H, CommandId.ToggleHidden);
        Add(list, GestureModifiers.Ctrl, GestureKey.L, CommandId.DirSizes);
        Add(list, GestureModifiers.Ctrl, GestureKey.M, CommandId.MultiRename);
        Add(list, GestureModifiers.Ctrl, GestureKey.N, CommandId.Ftp);
        Add(list, GestureModifiers.Ctrl, GestureKey.Q, CommandId.QuickView);
        Add(list, GestureModifiers.Ctrl, GestureKey.R, CommandId.Refresh);
        Add(list, GestureModifiers.Ctrl, GestureKey.S, CommandId.QuickFilter);
        Add(list, GestureModifiers.Ctrl, GestureKey.T, CommandId.NewTab);
        Add(list, GestureModifiers.Ctrl, GestureKey.U, CommandId.Swap);
        Add(list, GestureModifiers.Ctrl, GestureKey.V, CommandId.ClipPaste);
        Add(list, GestureModifiers.Ctrl, GestureKey.W, CommandId.CloseTab);
        Add(list, GestureModifiers.Ctrl, GestureKey.X, CommandId.ClipCut);
        Add(list, GestureModifiers.Ctrl, GestureKey.Tab, CommandId.NextTab);
        Add(list, GestureModifiers.Ctrl, GestureKey.Backslash, CommandId.Root);
        Add(list, GestureModifiers.Ctrl, GestureKey.PageUp, CommandId.Parent);
        Add(list, GestureModifiers.Ctrl, GestureKey.Plus, CommandId.SelectSameExt);
        Add(list, GestureModifiers.Ctrl, GestureKey.Equals, CommandId.SelectSameExt);
        Add(list, GestureModifiers.Ctrl, GestureKey.Minus, CommandId.UnselectAll);
    }

    private static void AddPlain(List<CommandBinding> list)
    {
        Add(list, GestureModifiers.None, GestureKey.F1, CommandId.Keys);
        Add(list, GestureModifiers.None, GestureKey.F2, CommandId.RenameInPlace);
        Add(list, GestureModifiers.None, GestureKey.F3, CommandId.View);
        Add(list, GestureModifiers.None, GestureKey.F4, CommandId.Edit);
        Add(list, GestureModifiers.None, GestureKey.F5, CommandId.Copy);
        Add(list, GestureModifiers.None, GestureKey.F6, CommandId.Move);
        Add(list, GestureModifiers.None, GestureKey.F7, CommandId.MkDir);
        Add(list, GestureModifiers.None, GestureKey.F8, CommandId.Delete);
        Add(list, GestureModifiers.None, GestureKey.Delete, CommandId.Delete);
        Add(list, GestureModifiers.None, GestureKey.F9, CommandId.Settings);
        Add(list, GestureModifiers.None, GestureKey.Plus, CommandId.SelectGroup);
        Add(list, GestureModifiers.None, GestureKey.Minus, CommandId.UnselectGroup);
        Add(list, GestureModifiers.None, GestureKey.Tab, CommandId.SwitchPanel);
        Add(list, GestureModifiers.None, GestureKey.Backspace, CommandId.Parent);
    }

    private static void Add(List<CommandBinding> list, GestureModifiers modifiers, GestureKey key, CommandId command) =>
        list.Add(new CommandBinding(new Gesture(modifiers, key), command));

    private static IReadOnlyList<CommandBinding> Sort(List<CommandBinding> list) =>
        list.OrderByDescending(binding => binding.Gesture.ModifierCount()).ToList();
}
