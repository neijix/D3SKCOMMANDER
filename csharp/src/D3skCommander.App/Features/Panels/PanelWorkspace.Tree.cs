using D3skCommander.App.Shell;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Tree;

namespace D3skCommander.App.Features.Panels;

public sealed partial class PanelWorkspace
{
    private readonly bool[] _showTree = new bool[2];
    private readonly double[] _treeWidth = [Preferences.DefaultTreeWidth, Preferences.DefaultTreeWidth];

    /// <summary>Whether hidden folders and files show (the tree follows the same setting).</summary>
    public bool ShowHidden => _showHidden;

    /// <summary>Whether the folder tree shows beside the file list of <paramref name="side"/> (saved in the preferences).</summary>
    public bool ShowTree(PanelSide side) => _showTree[Index(side)];

    /// <summary>Width of the folder tree of <paramref name="side"/> in device-independent pixels.</summary>
    public double TreeWidth(PanelSide side) => _treeWidth[Index(side)];

    /// <summary>Reads folders for the tree panels.</summary>
    public FolderTreeLoader TreeLoader() => new(_files);

    public void ToggleTree(PanelSide side)
    {
        var next = !_showTree[Index(side)];
        _showTree[Index(side)] = next;
        SaveTree(preferences =>
        {
            if (side == PanelSide.Left)
            {
                preferences.ShowTreeLeft = next;
            }
            else
            {
                preferences.ShowTreeRight = next;
            }
        });
    }

    /// <summary>Keeps the width the user dragged the tree splitter to.</summary>
    public void SetTreeWidth(PanelSide side, double width)
    {
        if (double.IsNaN(width) || width < Preferences.MinTreeWidth || Math.Abs(width - _treeWidth[Index(side)]) < 0.5)
        {
            return;
        }

        _treeWidth[Index(side)] = Math.Min(width, Preferences.MaxTreeWidth);
        SaveTree(preferences =>
        {
            if (side == PanelSide.Left)
            {
                preferences.TreeWidthLeft = width;
            }
            else
            {
                preferences.TreeWidthRight = width;
            }
        });
    }

    private void LoadTree(Preferences? preferences)
    {
        if (preferences is null)
        {
            return;
        }

        _showTree[0] = preferences.ShowTreeLeft;
        _showTree[1] = preferences.ShowTreeRight;
        _treeWidth[0] = preferences.TreeWidthLeft;
        _treeWidth[1] = preferences.TreeWidthRight;
    }

    private void SaveTree(Action<Preferences> change)
    {
        if (_settings is null)
        {
            return;
        }

        var preferences = _settings.Preferences.Current;
        change(preferences);
        _settings.Preferences.Save(preferences);
    }
}
