using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3skCommander.App.Features.Log;
using D3skCommander.App.Tests.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.OperationLog;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Log;

public class LogWindowTests
{
    [AvaloniaFact]
    public void ShowsTheOperationsLog()
    {
        var log = new OperationLog { Enabled = true };
        log.Record("COPY", "/a/one.txt", "/b/one.txt", null);
        log.Record("DELETE", "/a/two.txt", null, "denied");
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IOperationLog>(log));

        host.Window.Dispatch(CommandId.Log);

        var tool = host.Provider.GetRequiredService<IToolWindowHost>().Open(ToolKind.Log);
        var model = Assert.IsType<LogViewModel>(tool.View);
        Assert.Equal(2, model.Lines.Count);
        Assert.Contains("COPY", model.Lines[0], StringComparison.Ordinal);
        Assert.Contains("DELETE", model.Lines[1], StringComparison.Ordinal);
        var list = tool.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "LineList");
        Assert.Equal(2, list.ItemCount);
        Assert.False(model.HasNotice);

        log.Record("MOVE", "/a/three.txt", "/c/three.txt", null);
        Click(tool, "RefreshButton");
        Assert.Equal(3, model.Lines.Count);
        Assert.Contains("MOVE", model.Lines[^1], StringComparison.Ordinal);

        Click(tool, "ClearButton");
        Assert.Empty(model.Lines);
        log.Record("COPY", "/a/four.txt", "/b/four.txt", null);
        Click(tool, "RefreshButton");
        var only = Assert.Single(model.Lines);
        Assert.Contains("four.txt", only, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ExplainsHowToTurnLoggingOn()
    {
        var log = new OperationLog { Enabled = false };
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IOperationLog>(log));

        host.Window.Dispatch(CommandId.Log);

        var tool = host.Provider.GetRequiredService<IToolWindowHost>().Open(ToolKind.Log);
        var model = Assert.IsType<LogViewModel>(tool.View);
        Assert.True(model.HasNotice);
        Assert.Contains("Settings", model.Notice, StringComparison.Ordinal);
        var notice = tool.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "NoticeText");
        Assert.True(notice.IsVisible);
    }

    private static void Click(ToolWindow tool, string name)
    {
        var button = tool.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }
}
