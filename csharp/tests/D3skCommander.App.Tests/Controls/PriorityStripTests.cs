using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Controls;
using Xunit;

namespace D3skCommander.App.Tests.Controls;

public sealed class PriorityStripTests
{
    [AvaloniaFact]
    public void ChildrenThatDoNotFitBeforeTheReserveAreLeftOutFromTheEnd()
    {
        var strip = new PriorityStrip { Spacing = 2, Reserve = 100 };
        for (var i = 0; i < 6; i++)
        {
            strip.Children.Add(new Border { Width = 30, Height = 20 });
        }

        strip.Measure(new Size(200, 30));
        strip.Arrange(new Rect(strip.DesiredSize));

        Assert.Equal(3, strip.ShownCount);
        Assert.Equal(94, strip.DesiredSize.Width);
        Assert.Equal(new Rect(64, 0, 30, 20), strip.Children[2].Bounds);
        Assert.True(strip.ClipToBounds);
        Assert.True(strip.Children[3].Bounds.X >= strip.Bounds.Width);

        strip.Measure(new Size(double.PositiveInfinity, 30));
        Assert.Equal(6, strip.ShownCount);
    }
}
