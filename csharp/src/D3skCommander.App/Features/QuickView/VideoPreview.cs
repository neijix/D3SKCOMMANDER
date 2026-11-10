using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Preview;
using LibVLCSharp.Shared;

namespace D3skCommander.App.Features.QuickView;

/// <summary>
/// A video played by libvlc in the preview: VLC decodes into a buffer of 32-bit pixels (its video callbacks),
/// each frame is copied into a bitmap the pane shows, so no native window floats over the app. It starts at
/// once, without sound; the bar under the picture plays or pauses, seeks, turns the sound on and opens the file
/// in VLC. Disposing stops the player off the UI thread, then frees the buffer.
/// </summary>
internal sealed class VideoPreview : DockPanel, IDisposable
{
    private static readonly TimeSpan Refresh = TimeSpan.FromMilliseconds(250);
    private readonly ILocalizer _text;
    private readonly string _path;
    private readonly int _maxWidth;
    private readonly Media _media;
    private readonly MediaPlayer _player;
    private readonly Lock _frame = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Button _play = new();
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _sound = new();
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = Refresh };

    // Kept in fields: libvlc calls them from its threads for the player's whole life.
    private readonly MediaPlayer.LibVLCVideoFormatCb _format;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanup;
    private readonly MediaPlayer.LibVLCVideoLockCb _lock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _display;

    private IntPtr _buffer;
    private VideoFrameSize _size;
    private WriteableBitmap? _bitmap;
    private byte[] _row = [];
    private int _drawPending;
    private volatile bool _closed;
    private volatile bool _soundOn;
    private bool _showing;

    public VideoPreview(LibVLC vlc, ILocalizer text, string path, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(vlc);
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _path = path;
        _maxWidth = maxWidth;
        _format = OnFormat;
        _cleanup = OnCleanup;
        _lock = OnLock;
        _display = OnDisplay;
        _media = new Media(vlc, path, FromType.FromPath);
        _player = new MediaPlayer(_media);
        _player.SetVideoFormatCallbacks(_format, _cleanup);
        _player.SetVideoCallbacks(_lock, null, _display);

        // libvlc must not be called back from its own event threads: the sound is set from the pool.
        _player.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ => KeepSound());
        _player.EndReached += (_, _) => Dispatcher.UIThread.Post(Rewind);
        BuildBar();
        _timer.Tick += (_, _) => ShowState();
        _timer.Start();
        _player.Play();
    }

    /// <summary>True while the sound is on (it starts off).</summary>
    public bool SoundOn => _soundOn;

    private void BuildBar()
    {
        _play.Click += (_, _) => PlayOrPause();
        _sound.Content = _text.Get("quickview.video_sound");
        _sound.IsCheckedChanged += (_, _) =>
        {
            _soundOn = _sound.IsChecked == true;
            KeepSound();
        };
        _seek.ValueChanged += (_, e) =>
        {
            if (!_showing && _player.IsSeekable)
            {
                _player.Position = (float)e.NewValue;
            }
        };
        var open = new Button { Content = _text.Get("quickview.video_open") };
        open.Click += (_, _) => VlcRuntime.OpenInPlayer(_path);
        var bar = new DockPanel { Margin = new Thickness(4), LastChildFill = true };
        foreach (var (control, dock) in new (Control, Dock)[] { (_play, Dock.Left), (open, Dock.Right), (_sound, Dock.Right), (_clock, Dock.Right) })
        {
            SetDock(control, dock);
            control.Margin = new Thickness(4, 0);
            bar.Children.Add(control);
        }

        bar.Children.Add(_seek);
        SetDock(bar, Dock.Bottom);
        Children.Add(bar);
        Children.Add(_image);
        ShowState();
    }

    private void PlayOrPause()
    {
        if (_player.IsPlaying)
        {
            _player.SetPause(true);
        }
        else
        {
            _player.Play();
        }

        ShowState();
    }

    // At the end the player stops: the Play button starts the video again from its start.
    private void Rewind()
    {
        if (!_closed)
        {
            _ = Task.Run(_player.Stop);
        }
    }

    private void KeepSound()
    {
        if (!_closed)
        {
            _player.Mute = !_soundOn;
        }
    }

    private void ShowState()
    {
        if (_closed)
        {
            return;
        }

        _showing = true;
        try
        {
            _play.Content = _text.Get(_player.IsPlaying ? "quickview.video_pause" : "quickview.video_play");
            _seek.Value = Math.Clamp(_player.Position, 0, 1);
            _seek.IsEnabled = _player.IsSeekable;
            var time = TimeSpan.FromMilliseconds(Math.Max(0, _player.Time));
            _clock.Text = VideoFrames.Clock(time, TimeSpan.FromMilliseconds(Math.Max(0, _player.Length)));
        }
        finally
        {
            _showing = false;
        }

        // The sound may only be set once the audio output exists: keep it as asked.
        if (_player.IsPlaying && _player.Mute == _soundOn)
        {
            _player.Mute = !_soundOn;
        }
    }

    // libvlc asks which pixels it should write: 32-bit BGRX, scaled down to the pane, rows padded.
    private uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        var size = VideoFrames.Fit((int)width, (int)height, _maxWidth);
        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        WriteableBitmap shown;
        lock (_frame)
        {
            FreeFrame();
            _size = size;
            _buffer = Marshal.AllocHGlobal(size.Bytes);
            _row = new byte[size.Width * 4];
            shown = new WriteableBitmap(new PixelSize(size.Width, size.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _bitmap = shown;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_closed)
            {
                shown.Dispose();
                return;
            }

            var before = _image.Source as IDisposable;
            _image.Source = shown;
            before?.Dispose();
        });
        width = (uint)size.Width;
        height = (uint)size.Height;
        pitches = (uint)size.Pitch;
        lines = (uint)size.Lines;
        return 1;
    }

    private void OnCleanup(ref IntPtr opaque)
    {
    }

    private IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _buffer);
        return IntPtr.Zero;
    }

    // A frame is ready in the buffer: copy it into the bitmap, then ask the pane to draw it (at most one draw queued).
    private void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        lock (_frame)
        {
            if (_closed || _bitmap is not { } bitmap || _buffer == IntPtr.Zero)
            {
                return;
            }

            using var target = bitmap.Lock();
            var row = Math.Min(_row.Length, target.RowBytes);
            for (var y = 0; y < _size.Height; y++)
            {
                Marshal.Copy(_buffer + (y * _size.Pitch), _row, 0, row);
                Marshal.Copy(_row, 0, target.Address + (y * target.RowBytes), row);
            }
        }

        if (Interlocked.Exchange(ref _drawPending, 1) == 0)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    Interlocked.Exchange(ref _drawPending, 0);
                    _image.InvalidateVisual();
                },
                DispatcherPriority.Render);
        }
    }

    // Frees the frame buffer; the bitmap stays with the pane until it is replaced or the player is gone.
    private void FreeFrame()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }

        _bitmap = null;
    }

    /// <summary>Stops the video: the pane lets go of the picture now, the player stops and frees it in the background.</summary>
    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _timer.Stop();
        var shown = _image.Source as IDisposable;
        _image.Source = null;
        _ = Task.Run(() =>
        {
            _player.Stop();
            lock (_frame)
            {
                FreeFrame();
            }

            _player.Dispose();
            _media.Dispose();
            shown?.Dispose();
        });
    }
}
