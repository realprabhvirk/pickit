using SkiaSharp;

namespace PicKit.Core.Rendering;

/// <summary>The familiar grey checks that mean "transparent", as a tiling shader.</summary>
public sealed class Checkerboard : IDisposable
{
    private readonly SKBitmap _tile;
    private readonly SKShader _shader;

    public Checkerboard(int squareSize, SKColor light, SKColor dark)
    {
        _tile = new SKBitmap(squareSize * 2, squareSize * 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(_tile))
        using (var paint = new SKPaint { Color = dark })
        {
            canvas.Clear(light);
            canvas.DrawRect(0, 0, squareSize, squareSize, paint);
            canvas.DrawRect(squareSize, squareSize, squareSize, squareSize, paint);
        }

        _shader = SKShader.CreateBitmap(_tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
    }

    public void Draw(SKCanvas canvas, SKRect area)
    {
        using var paint = new SKPaint { Shader = _shader };
        canvas.DrawRect(area, paint);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _tile.Dispose();
    }
}
