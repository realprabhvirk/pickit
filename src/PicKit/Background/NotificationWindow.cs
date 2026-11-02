using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.Background;

public enum SwipeOutcome
{
    Click,
    Dismiss,
    SnapBack,
}

/// <summary>Tells a click from a swipe, and a real swipe from a nudge.</summary>
internal sealed class SwipeTracker
{
    private const int ClickSlop = 6;
    private const double DismissShare = 0.3;

    private readonly int _width;
    private int _start;
    private int _offset;
    private bool _hasMoved;

    public SwipeTracker(int width)
    {
        _width = width;
    }

    public void Press(int x)
    {
        _start = x;
        _offset = 0;
        _hasMoved = false;
    }

    /// <summary>Returns how far the window should follow the pointer.</summary>
    public int Move(int x)
    {
        _offset = x - _start;
        _hasMoved |= Math.Abs(_offset) > ClickSlop;
        return _offset;
    }

    public SwipeOutcome Release()
    {
        if (!_hasMoved)
        {
            return SwipeOutcome.Click;
        }

        return Math.Abs(_offset) >= _width * DismissShare ? SwipeOutcome.Dismiss : SwipeOutcome.SnapBack;
    }
}

/// <summary>
/// A notification beside the taskbar. It never takes focus, so the app in use carries on as if
/// nothing happened. Click it to act, or swipe it away. It's a window of PicKit's own, not a
/// Windows notification, because those are skipped in Do Not Disturb, stay in the notification
/// list afterwards, and can be read by other apps.
/// </summary>
internal sealed class NotificationWindow : AgentWindow
{
    public const int WidthDip = 340;
    public const int HeightDip = 88;

    private const int MarginDip = 16;
    private const int ThumbnailWidthDip = 96;
    private const uint VisibleMilliseconds = 8000;
    private const uint FlingMilliseconds = 15;
    private const int FlingFrames = 6;

    private static readonly IntPtr HideTimer = 1;
    private static readonly IntPtr FlingTimer = 2;

    private readonly Action _closed;
    private LayeredSurface? _surface;
    private SwipeTracker? _swipe;
    private Action? _onClick;
    private int _x;
    private int _y;
    private int _offset;
    private int _flingStep;
    private bool _isPressed;

    public NotificationWindow(Action closed)
        : base(Native.WS_POPUP, Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST)
    {
        _closed = closed;

        // Otherwise it would turn up in a second screenshot taken while it's still showing.
        Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }

    /// <summary>Thumbnails are made once, at this height in pixels.</summary>
    public static int ThumbnailHeight => (int)(ScaleFor(MonitorUnderCursor()) * (HeightDip - 24));

    public void Show(string title, string detail, ImageBuffer? thumbnail, Action? onClick)
    {
        var monitor = MonitorUnderCursor();
        var scale = ScaleFor(monitor);
        var work = InfoFor(monitor).rcWork;
        var width = (int)(WidthDip * scale);
        var height = (int)(HeightDip * scale);
        var margin = (int)(MarginDip * scale);

        _surface?.Dispose();
        _surface = new LayeredSurface(width, height);
        _surface.Paint(canvas => Paint(canvas, width, height, scale, title, detail, thumbnail));

        _swipe = new SwipeTracker(width);
        _onClick = onClick;
        _x = work.Right - width - margin;
        _y = work.Bottom - height - margin;
        _offset = 0;
        _flingStep = 0;

        _surface.Present(Handle, _x, _y);
        Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
        Native.KillTimer(Handle, FlingTimer);
        Native.SetTimer(Handle, HideTimer, VisibleMilliseconds, 0);
    }

    public override void Dispose()
    {
        _surface?.Dispose();
        base.Dispose();
    }

    /// <summary>Draws the notification. Separate from the window so it can be checked as a picture.</summary>
    internal static void Paint(SKCanvas canvas, int width, int height, float scale, string title, string detail, ImageBuffer? thumbnail)
    {
        var box = new SKRoundRect(new SKRect(0, 0, width, height), 8 * scale);
        using var fill = new SKPaint { Color = new SKColor(0x2B, 0x2B, 0x2B), IsAntialias = true };
        using var edge = new SKPaint { Color = new SKColor(0x45, 0x45, 0x45), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = scale };
        canvas.DrawRoundRect(box, fill);
        canvas.DrawRoundRect(box, edge);

        var left = 16 * scale;
        if (thumbnail is not null)
        {
            left = DrawThumbnail(canvas, thumbnail, height, scale) + (12 * scale);
        }

        using var strong = new SKFont(SKTypeface.FromFamilyName(TextFonts.Default, SKFontStyle.Bold), 14 * scale);
        using var plain = new SKFont(TextFonts.Typeface(TextFonts.Default), 12 * scale);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var soft = new SKPaint { Color = new SKColor(0xD0, 0xD0, 0xD0), IsAntialias = true };

        canvas.DrawText(title, left, 34 * scale, SKTextAlign.Left, strong, white);
        var baseline = 54 * scale;
        foreach (var line in detail.Split('\n'))
        {
            canvas.DrawText(line, left, baseline, SKTextAlign.Left, plain, soft);
            baseline += 17 * scale;
        }
    }

    protected override IntPtr OnMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case Native.WM_MOUSEACTIVATE:
                return Native.MA_NOACTIVATE;
            case Native.WM_LBUTTONDOWN:
                OnPress();
                return 0;
            case Native.WM_MOUSEMOVE:
                OnMove();
                return 0;
            case Native.WM_LBUTTONUP:
                OnRelease();
                return 0;
            case Native.WM_TIMER:
                OnTimer(wParam);
                return 0;
            default:
                return base.OnMessage(message, wParam, lParam);
        }
    }

    private static float DrawThumbnail(SKCanvas canvas, ImageBuffer thumbnail, int height, float scale)
    {
        var inset = 12 * scale;
        var available = new SKRect(inset, inset, inset + (ThumbnailWidthDip * scale), height - inset);
        var fit = Math.Min(available.Width / thumbnail.Width, available.Height / thumbnail.Height);
        var size = new SKSize(thumbnail.Width * fit, thumbnail.Height * fit);
        var target = SKRect.Create(available.Left, available.MidY - (size.Height / 2), size.Width, size.Height);

        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(target, 4 * scale), antialias: true);
        using var image = thumbnail.AsImage();
        canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear));
        canvas.Restore();
        return target.Right;
    }

    private void OnPress()
    {
        Native.GetCursorPos(out var cursor);
        _swipe?.Press(cursor.X);
        _isPressed = true;
        Native.SetCapture(Handle);
    }

    private void OnMove()
    {
        if (_isPressed && _swipe is { } swipe)
        {
            Native.GetCursorPos(out var cursor);
            _offset = swipe.Move(cursor.X);
            PresentAtOffset();
        }
    }

    private void OnRelease()
    {
        if (!_isPressed || _swipe is not { } swipe)
        {
            return;
        }

        _isPressed = false;
        Native.ReleaseCapture();
        switch (swipe.Release())
        {
            case SwipeOutcome.Click:
                var action = _onClick;
                Close();
                action?.Invoke();
                break;
            case SwipeOutcome.Dismiss:
                _flingStep = 1;
                Native.SetTimer(Handle, FlingTimer, FlingMilliseconds, 0);
                break;
            default:
                _offset = 0;
                PresentAtOffset();
                break;
        }
    }

    private void OnTimer(IntPtr timer)
    {
        if (timer == HideTimer)
        {
            // Stays up while it's being held, however long that takes.
            if (!_isPressed)
            {
                Close();
            }
        }
        else if (timer == FlingTimer)
        {
            Fling();
        }
    }

    private void Fling()
    {
        if (_surface is not { } surface || _flingStep > FlingFrames)
        {
            Close();
            return;
        }

        _offset += Math.Sign(_offset) * surface.Width / FlingFrames;
        _flingStep++;
        PresentAtOffset();
    }

    private void PresentAtOffset()
    {
        if (_surface is { } surface)
        {
            var fade = 1 - Math.Clamp(Math.Abs(_offset) / (double)surface.Width, 0, 0.9);
            surface.Present(Handle, _x + _offset, _y, (byte)(255 * fade));
        }
    }

    private void Close()
    {
        Native.KillTimer(Handle, HideTimer);
        Native.KillTimer(Handle, FlingTimer);
        Native.ShowWindow(Handle, Native.SW_HIDE);
        _surface?.Dispose();
        _surface = null;
        _onClick = null;
        _closed();
    }
}
