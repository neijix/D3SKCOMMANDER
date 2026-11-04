using D3skCommander.Core.Commands;
using Hexa.NET.ImGui;

namespace D3skCommander.App.Shell;

/// <summary>Maps ImGui keys onto Core gestures. The primary modifier is Command on macOS and Ctrl elsewhere.</summary>
public static class GestureMap
{
    private static readonly Dictionary<ImGuiKey, GestureKey> FromKey = [];
    private static readonly Dictionary<GestureKey, ImGuiKey> ToKey = [];

    static GestureMap()
    {
        AddFunctions();
        AddLetters();
        AddNavigation();
        AddSymbols();
    }

    public static Gesture? From(ImGuiKey key, GestureModifiers modifiers)
    {
        var gestureKey = ToGestureKey(key, modifiers);
        if (gestureKey is null)
        {
            return null;
        }

        return new Gesture(modifiers, gestureKey.Value);
    }

    public static ImGuiKey ToImGui(GestureKey key)
    {
        if (!ToKey.TryGetValue(key, out var imgui))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "No ImGui key for this gesture.");
        }

        return imgui;
    }

    public static bool IsPrimary(bool ctrl, bool super)
    {
        if (OperatingSystem.IsMacOS())
        {
            return super;
        }

        return ctrl;
    }

    public static GestureModifiers Modifiers(bool primary, bool shift, bool alt)
    {
        var gesture = GestureModifiers.None;
        if (primary)
        {
            gesture |= GestureModifiers.Ctrl;
        }

        if (shift)
        {
            gesture |= GestureModifiers.Shift;
        }

        if (alt)
        {
            gesture |= GestureModifiers.Alt;
        }

        return gesture;
    }

    private static GestureKey? ToGestureKey(ImGuiKey key, GestureModifiers modifiers)
    {
        if (key == ImGuiKey.Equal)
        {
            return modifiers.HasFlag(GestureModifiers.Shift) ? GestureKey.Plus : GestureKey.Equals;
        }

        if (FromKey.TryGetValue(key, out var gestureKey))
        {
            return gestureKey;
        }

        return null;
    }

    private static void AddFunctions()
    {
        AddPair(ImGuiKey.F1, GestureKey.F1);
        AddPair(ImGuiKey.F2, GestureKey.F2);
        AddPair(ImGuiKey.F3, GestureKey.F3);
        AddPair(ImGuiKey.F4, GestureKey.F4);
        AddPair(ImGuiKey.F5, GestureKey.F5);
        AddPair(ImGuiKey.F6, GestureKey.F6);
        AddPair(ImGuiKey.F7, GestureKey.F7);
        AddPair(ImGuiKey.F8, GestureKey.F8);
        AddPair(ImGuiKey.F9, GestureKey.F9);
    }

    private static void AddLetters()
    {
        AddPair(ImGuiKey.A, GestureKey.A);
        AddPair(ImGuiKey.B, GestureKey.B);
        AddPair(ImGuiKey.D, GestureKey.D);
        AddPair(ImGuiKey.H, GestureKey.H);
        AddPair(ImGuiKey.L, GestureKey.L);
        AddPair(ImGuiKey.M, GestureKey.M);
        AddPair(ImGuiKey.N, GestureKey.N);
        AddPair(ImGuiKey.Q, GestureKey.Q);
        AddPair(ImGuiKey.R, GestureKey.R);
        AddPair(ImGuiKey.S, GestureKey.S);
        AddPair(ImGuiKey.T, GestureKey.T);
        AddPair(ImGuiKey.U, GestureKey.U);
        AddPair(ImGuiKey.W, GestureKey.W);
    }

    private static void AddNavigation()
    {
        AddPair(ImGuiKey.Delete, GestureKey.Delete);
        AddPair(ImGuiKey.Enter, GestureKey.Enter);
        FromKey.TryAdd(ImGuiKey.KeypadEnter, GestureKey.Enter);
        AddPair(ImGuiKey.LeftArrow, GestureKey.Left);
        AddPair(ImGuiKey.RightArrow, GestureKey.Right);
        AddPair(ImGuiKey.DownArrow, GestureKey.Down);
        AddPair(ImGuiKey.Tab, GestureKey.Tab);
        AddPair(ImGuiKey.PageUp, GestureKey.PageUp);
        AddPair(ImGuiKey.Backspace, GestureKey.Backspace);
    }

    private static void AddSymbols()
    {
        ToKey.Add(GestureKey.Plus, ImGuiKey.Equal);
        ToKey.Add(GestureKey.Equals, ImGuiKey.Equal);
        ToKey.Add(GestureKey.Minus, ImGuiKey.Minus);
        FromKey.TryAdd(ImGuiKey.KeypadAdd, GestureKey.Plus);
        FromKey.TryAdd(ImGuiKey.KeypadSubtract, GestureKey.Minus);
        FromKey.TryAdd(ImGuiKey.Minus, GestureKey.Minus);
        AddPair(ImGuiKey.Backslash, GestureKey.Backslash);
    }

    private static void AddPair(ImGuiKey key, GestureKey gesture)
    {
        FromKey.TryAdd(key, gesture);
        ToKey.TryAdd(gesture, key);
    }
}
