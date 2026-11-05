using Avalonia.Headless.XUnit;
using Avalonia.Input;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using Xunit;

namespace D3skCommander.App.Tests.Shell;

public class GestureMapTests
{
    [AvaloniaFact]
    public void ThePrimaryModifierIsCommandOnMacOsAndCtrlElsewhere()
    {
        var mac = OperatingSystem.IsMacOS();
        Assert.Equal(mac, GestureMap.IsPrimary(KeyModifiers.Meta));
        Assert.Equal(!mac, GestureMap.IsPrimary(KeyModifiers.Control));
        Assert.False(GestureMap.IsPrimary(KeyModifiers.Shift));
        var primary = mac ? KeyModifiers.Meta : KeyModifiers.Control;
        Assert.Equal(new Gesture(GestureModifiers.Ctrl, GestureKey.A), GestureMap.From(Key.A, primary));
    }

    [AvaloniaFact]
    public void ShiftEqualIsPlusAndEqualAloneIsEquals()
    {
        Assert.Equal(GestureKey.Plus, GestureMap.From(Key.OemPlus, KeyModifiers.Shift)?.Key);
        Assert.Equal(GestureKey.Equals, GestureMap.From(Key.OemPlus, KeyModifiers.None)?.Key);
    }

    [AvaloniaFact]
    public void KeypadKeysMapLikeTheMainKeys()
    {
        Assert.Equal(GestureKey.Plus, GestureMap.From(Key.Add, KeyModifiers.None)?.Key);
        Assert.Equal(GestureKey.Minus, GestureMap.From(Key.Subtract, KeyModifiers.None)?.Key);
        Assert.Equal(GestureKey.Minus, GestureMap.From(Key.OemMinus, KeyModifiers.None)?.Key);
        Assert.Equal(GestureKey.Enter, GestureMap.From(Key.Enter, KeyModifiers.None)?.Key);
        Assert.Equal(GestureKey.Enter, GestureMap.From(Key.Return, KeyModifiers.None)?.Key);
    }

    [AvaloniaFact]
    public void AnUnmappedKeyGivesNoGesture()
    {
        Assert.Null(GestureMap.From(Key.F12, KeyModifiers.None));
    }
}
