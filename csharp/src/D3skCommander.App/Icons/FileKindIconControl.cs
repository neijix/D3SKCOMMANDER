using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using D3skCommander.Core.FileKinds;

namespace D3skCommander.App.Icons;

/// <summary>Draws the <see cref="FileKindIcons"/> icon of one kind, scaled to its size.</summary>
public sealed class FileKindIconControl : Control
{
    private static readonly IBrush ShadeBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)).ToImmutable();
    private static readonly IBrush GlyphBrush = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)).ToImmutable();

    private FileKindIcon? _icon;

    public FileKindIconControl()
        : this(16)
    {
    }

    public FileKindIconControl(double size)
    {
        Width = size;
        Height = size;
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>The key of the icon drawn (<c>folder</c>, <c>image</c>, ...), or empty when none.</summary>
    public string IconKey => _icon?.Key ?? "";

    /// <summary>The kind drawn; bindable (the folder tree binds it per node).</summary>
    public static readonly StyledProperty<FileKind?> KindProperty =
        AvaloniaProperty.Register<FileKindIconControl, FileKind?>(nameof(Kind));

    static FileKindIconControl()
    {
        KindProperty.Changed.AddClassHandler<FileKindIconControl>((control, e) => control.Show(e.GetNewValue<FileKind?>()));
    }

    public FileKind? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void Show(FileKind? kind)
    {
        _icon = kind.HasValue ? FileKindIcons.For(kind.Value) : null;
        AutomationProperties.SetAutomationId(this, _icon is null ? "" : "icon-" + _icon.Key);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_icon is null)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width, Bounds.Height) / FileKindIcon.Grid;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(new SolidColorBrush(_icon.Colour), null, _icon.Body);
            if (_icon.Shade is not null)
            {
                context.DrawGeometry(ShadeBrush, null, _icon.Shade);
            }

            if (_icon.Glyph is not null)
            {
                context.DrawGeometry(GlyphBrush, null, _icon.Glyph);
            }
        }
    }
}
