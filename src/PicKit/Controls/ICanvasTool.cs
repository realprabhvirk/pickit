using System.Windows.Input;
using PicKit.Core.Rendering;
using SkiaSharp;

namespace PicKit.Controls;

/// <summary>A pointer event, already translated into both viewport and image coordinates.</summary>
public readonly record struct CanvasPointer(SKPoint View, SKPoint Image, ModifierKeys Modifiers, float Zoom);

/// <summary>Whatever currently interprets pointer input over the image and draws guides on top of it.</summary>
public interface ICanvasTool
{
    void OnPointerDown(CanvasPointer pointer);

    void OnPointerMove(CanvasPointer pointer, bool isPressed);

    void OnPointerUp(CanvasPointer pointer);

    /// <summary>Cursor for the current position, or null for the default.</summary>
    Cursor? CursorAt(CanvasPointer pointer);

    /// <summary>Draws work in progress in image coordinates, clipped to the image.</summary>
    void DrawOnImage(SKCanvas canvas)
    {
    }

    /// <summary>Draws guides in viewport coordinates, above the image and any markup.</summary>
    void DrawOverlay(SKCanvas canvas, ViewTransform view)
    {
    }
}
