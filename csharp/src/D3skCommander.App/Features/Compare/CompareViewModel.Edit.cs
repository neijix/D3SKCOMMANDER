using CommunityToolkit.Mvvm.ComponentModel;
using D3skCommander.Core.Compare;

namespace D3skCommander.App.Features.Compare;

/// <summary>The two edit panes: each shows its session buffer and writes typing back into it.</summary>
public sealed partial class CompareViewModel
{
    private CompareBuffer? _leftSource;
    private CompareBuffer? _rightSource;
    private bool _syncing;

    [ObservableProperty]
    private string _leftEdit = "";

    [ObservableProperty]
    private string _rightEdit = "";

    [ObservableProperty]
    private string _leftEditTitle = "";

    [ObservableProperty]
    private string _rightEditTitle = "";

    partial void OnLeftEditChanged(string value) => Typed(0, value);

    partial void OnRightEditChanged(string value) => Typed(1, value);

    private void Typed(int side, string value)
    {
        if (!_syncing)
        {
            _session.SetText(side, value);
        }
    }

    private void ResetEditSources()
    {
        _leftSource = null;
        _rightSource = null;
    }

    private void UpdateEditors()
    {
        var left = _session.LeftBuffer;
        var right = _session.RightBuffer;
        if (left is null || right is null)
        {
            return;
        }

        _syncing = true;
        try
        {
            if (!ReferenceEquals(_leftSource, left))
            {
                LeftEdit = left.Text;
                _leftSource = left;
            }

            if (!ReferenceEquals(_rightSource, right))
            {
                RightEdit = right.Text;
                _rightSource = right;
            }
        }
        finally
        {
            _syncing = false;
        }

        LeftEditTitle = left.Dirty ? "* " + left.Path : left.Path;
        RightEditTitle = right.Dirty ? "* " + right.Path : right.Path;
    }
}
