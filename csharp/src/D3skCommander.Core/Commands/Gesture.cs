namespace D3skCommander.Core.Commands;

/// <summary>Primary modifier is Ctrl on Windows and Linux, and Command on macOS (v0.1 <c>Modifiers::COMMAND</c>).</summary>
[Flags]
public enum GestureModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>Keys v0.1 binds. Names match the egui logical keys used in <c>bindings()</c>.</summary>
public enum GestureKey
{
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    Delete,
    Enter,
    Left,
    Right,
    Down,
    A,
    B,
    C,
    D,
    F,
    G,
    H,
    L,
    M,
    N,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Tab,
    Backslash,
    PageUp,
    Plus,
    Equals,
    Minus,
    Backspace,
    Escape,
    D1,
}

/// <summary>One chord. Extra Shift or Alt still matches a less specific pattern, as egui's logical match does.</summary>
public readonly record struct Gesture(GestureModifiers Modifiers, GestureKey Key)
{
    public bool Matches(Gesture pressed)
    {
        if (Key != pressed.Key)
        {
            return false;
        }

        if (Has(GestureModifiers.Alt) && !pressed.Has(GestureModifiers.Alt))
        {
            return false;
        }

        if (Has(GestureModifiers.Shift) && !pressed.Has(GestureModifiers.Shift))
        {
            return false;
        }

        return Has(GestureModifiers.Ctrl) == pressed.Has(GestureModifiers.Ctrl);
    }

    public string Format()
    {
        var parts = new List<string>();
        if (Has(GestureModifiers.Ctrl))
        {
            parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl");
        }

        if (Has(GestureModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Has(GestureModifiers.Alt))
        {
            parts.Add("Alt");
        }

        parts.Add(KeyText(Key));
        return string.Join("+", parts);
    }

    public int ModifierCount()
    {
        var count = 0;
        if (Has(GestureModifiers.Ctrl))
        {
            count++;
        }

        if (Has(GestureModifiers.Shift))
        {
            count++;
        }

        if (Has(GestureModifiers.Alt))
        {
            count++;
        }

        return count;
    }

    private bool Has(GestureModifiers flag) => (Modifiers & flag) == flag;

    private static string KeyText(GestureKey key)
    {
        if (key == GestureKey.Plus)
        {
            return "+";
        }

        if (key == GestureKey.Equals)
        {
            return "=";
        }

        if (key == GestureKey.Minus)
        {
            return "-";
        }

        if (key == GestureKey.Backslash)
        {
            return "\\";
        }

        if (key == GestureKey.D1)
        {
            return "1";
        }

        return key.ToString();
    }
}
