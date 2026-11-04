using Avalonia.Input;
using D3skCommander.Core.Commands;

namespace D3skCommander.App.Shell;

/// <summary>Maps Avalonia keys onto Core gestures. The primary modifier is Command on macOS and Ctrl elsewhere.</summary>
public static class GestureMap
{
    private static readonly Dictionary<Key, GestureKey> FromKey = [];
    private static readonly Dictionary<GestureKey, Key> ToKey = [];

    static GestureMap()
    {
        AddFunctions();
        AddLetters();
        AddNavigation();
        AddSymbols();
    }

    public static Gesture? From(Key key, KeyModifiers modifiers)
    {
        var gestureKey = ToGestureKey(key, modifiers);
        if (gestureKey is null)
        {
            return null;
        }

        return new Gesture(ToGestureModifiers(modifiers), gestureKey.Value);
    }

    public static KeyGesture? ToKeyGesture(Gesture? gesture)
    {
        if (gesture is null)
        {
            return null;
        }

        return ToKeyGesture(gesture.Value);
    }

    public static KeyGesture ToKeyGesture(Gesture gesture)
    {
        if (!ToKey.TryGetValue(gesture.Key, out var key))
        {
            throw new ArgumentOutOfRangeException(nameof(gesture), gesture, "No key for this gesture.");
        }

        return new KeyGesture(key, ToKeyModifiers(gesture.Modifiers));
    }

    private static GestureKey? ToGestureKey(Key key, KeyModifiers modifiers)
    {
        if (key == Key.OemPlus)
        {
            return modifiers.HasFlag(KeyModifiers.Shift) ? GestureKey.Plus : GestureKey.Equals;
        }

        if (FromKey.TryGetValue(key, out var gestureKey))
        {
            return gestureKey;
        }

        return null;
    }

    private static GestureModifiers ToGestureModifiers(KeyModifiers modifiers)
    {
        var gesture = GestureModifiers.None;
        if (IsPrimary(modifiers))
        {
            gesture |= GestureModifiers.Ctrl;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            gesture |= GestureModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            gesture |= GestureModifiers.Alt;
        }

        return gesture;
    }

    private static KeyModifiers ToKeyModifiers(GestureModifiers modifiers)
    {
        var keys = KeyModifiers.None;
        if ((modifiers & GestureModifiers.Ctrl) != 0)
        {
            keys |= OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        }

        if ((modifiers & GestureModifiers.Shift) != 0)
        {
            keys |= KeyModifiers.Shift;
        }

        if ((modifiers & GestureModifiers.Alt) != 0)
        {
            keys |= KeyModifiers.Alt;
        }

        return keys;
    }

    private static bool IsPrimary(KeyModifiers modifiers)
    {
        if (OperatingSystem.IsMacOS())
        {
            return modifiers.HasFlag(KeyModifiers.Meta);
        }

        return modifiers.HasFlag(KeyModifiers.Control);
    }

    private static void AddFunctions()
    {
        AddPair(Key.F1, GestureKey.F1);
        AddPair(Key.F2, GestureKey.F2);
        AddPair(Key.F3, GestureKey.F3);
        AddPair(Key.F4, GestureKey.F4);
        AddPair(Key.F5, GestureKey.F5);
        AddPair(Key.F6, GestureKey.F6);
        AddPair(Key.F7, GestureKey.F7);
        AddPair(Key.F8, GestureKey.F8);
        AddPair(Key.F9, GestureKey.F9);
    }

    private static void AddLetters()
    {
        AddPair(Key.A, GestureKey.A);
        AddPair(Key.B, GestureKey.B);
        AddPair(Key.D, GestureKey.D);
        AddPair(Key.H, GestureKey.H);
        AddPair(Key.L, GestureKey.L);
        AddPair(Key.M, GestureKey.M);
        AddPair(Key.N, GestureKey.N);
        AddPair(Key.Q, GestureKey.Q);
        AddPair(Key.R, GestureKey.R);
        AddPair(Key.S, GestureKey.S);
        AddPair(Key.T, GestureKey.T);
        AddPair(Key.U, GestureKey.U);
        AddPair(Key.W, GestureKey.W);
    }

    private static void AddNavigation()
    {
        AddPair(Key.Delete, GestureKey.Delete);
        AddPair(Key.Enter, GestureKey.Enter);
        FromKey.TryAdd(Key.Return, GestureKey.Enter);
        AddPair(Key.Left, GestureKey.Left);
        AddPair(Key.Right, GestureKey.Right);
        AddPair(Key.Down, GestureKey.Down);
        AddPair(Key.Tab, GestureKey.Tab);
        AddPair(Key.PageUp, GestureKey.PageUp);
        AddPair(Key.Back, GestureKey.Backspace);
    }

    private static void AddSymbols()
    {
        ToKey.Add(GestureKey.Plus, Key.OemPlus);
        ToKey.Add(GestureKey.Equals, Key.OemPlus);
        ToKey.Add(GestureKey.Minus, Key.OemMinus);
        FromKey.TryAdd(Key.Add, GestureKey.Plus);
        FromKey.TryAdd(Key.Subtract, GestureKey.Minus);
        FromKey.TryAdd(Key.OemMinus, GestureKey.Minus);
        AddPair(Key.OemBackslash, GestureKey.Backslash);
        FromKey.TryAdd(Key.OemPipe, GestureKey.Backslash);
    }

    private static void AddPair(Key key, GestureKey gesture)
    {
        FromKey.TryAdd(key, gesture);
        ToKey.TryAdd(gesture, key);
    }
}
