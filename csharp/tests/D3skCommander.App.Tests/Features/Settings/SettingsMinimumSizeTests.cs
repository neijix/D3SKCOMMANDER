using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using D3skCommander.App.Features.Settings;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.App.Tests.Features.Settings;

/// <summary>Every settings page rendered at the window's minimum size: fields keep their width, nothing is clipped sideways, list pages fit their height.</summary>
public class SettingsMinimumSizeTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void EveryPageFitsAtTheMinimumSize()
    {
        using var harness = new SettingsHarness();
        var model = harness.NewView();
        var toolbarRow = model.Toolbar.AddProgram("program", "args", "folder");
        model.Toolbar.SelectedRow = toolbarRow;
        var rule = model.Associations.AddRule("Text files", "*.txt");
        rule.AddExtra();
        var window = new Window
        {
            Width = ToolKind.Settings.MinWidth,
            Height = ToolKind.Settings.MinHeight,
            Content = model.CreateContent(),
        };
        window.Show();
        var folder = Directory.CreateTempSubdirectory("d3sk-settings-min-").FullName;
        output.WriteLine("Frames: " + folder);
        var problems = new List<string>();

        foreach (var category in SettingsCategories.All)
        {
            model.Select(category.Id);
            window.UpdateLayout();
            var page = window.GetVisualDescendants().OfType<Control>().First(c => c.Name == SettingsView.PageName(category.Id));
            Assert.True(page.IsEffectivelyVisible);
            Check(category.Id, page, problems);
            window.CaptureRenderedFrame()?.Save(Path.Combine(folder, category.Id + ".png"), PngBitmapEncoderOptions.Default);
        }

        window.Close();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static void Check(string id, Control page, List<string> problems)
    {
        var listPage = page is not ScrollViewer;
        foreach (var control in page.GetLogicalDescendants().OfType<Control>())
        {
            if (string.IsNullOrEmpty(control.Name) || !control.IsEffectivelyVisible || control.GetLogicalAncestors().Any(a => a is ListBox))
            {
                continue;
            }

            if (control.Bounds.Width + 0.5 < control.DesiredSize.Width)
            {
                problems.Add($"{id}: {control.Name} is {control.Bounds.Width:0} wide, wants {control.DesiredSize.Width:0}");
            }

            var clip = control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() ?? (Control)page;
            var origin = control.TranslatePoint(default, clip);
            if (origin is null)
            {
                continue;
            }

            if (origin.Value.X < -0.5 || origin.Value.X + control.Bounds.Width > clip.Bounds.Width + 0.5)
            {
                problems.Add($"{id}: {control.Name} spans x {origin.Value.X:0}..{origin.Value.X + control.Bounds.Width:0} of {clip.Bounds.Width:0}");
            }

            var scrolls = control.GetVisualAncestors().OfType<ScrollViewer>().Any(s => s != control);
            if ((listPage || clip != page) && !scrolls && (origin.Value.Y < -0.5 || origin.Value.Y + control.Bounds.Height > clip.Bounds.Height + 0.5))
            {
                problems.Add($"{id}: {control.Name} spans y {origin.Value.Y:0}..{origin.Value.Y + control.Bounds.Height:0} of {clip.Bounds.Height:0}");
            }
        }
    }
}
