#!/usr/bin/env dotnet
#:package SkiaSharp@4.154.0
#:property ManagePackageVersionsCentrally=false
// Draws the PicKit application icon and writes a multi-resolution .ico.
// Usage: dotnet run tools/make-icon.cs -- src/PicKit/Assets/PicKit.ico

using SkiaSharp;

var output = args.Length > 0 ? args[0] : "PicKit.ico";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var images = sizes.Select(size => (Size: size, Png: Render(size))).ToArray();

using var file = File.Create(output);
using var writer = new BinaryWriter(file);
writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)images.Length);

var offset = 6 + (16 * images.Length);
foreach (var (size, png) in images)
{
    writer.Write((byte)(size >= 256 ? 0 : size));
    writer.Write((byte)(size >= 256 ? 0 : size));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(png.Length);
    writer.Write(offset);
    offset += png.Length;
}

foreach (var (_, png) in images)
{
    writer.Write(png);
}

static byte[] Render(int size)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / 32f);

    // A photo: rounded tile, sun and hills. Sharp enough to survive at 16 pixels.
    using var tile = new SKPaint { IsAntialias = true, Color = new SKColor(0x00, 0x67, 0xC0) };
    canvas.DrawRoundRect(new SKRect(1.5f, 1.5f, 30.5f, 30.5f), 6.5f, 6.5f, tile);

    using var sky = new SKPaint { IsAntialias = true, Color = SKColors.White };
    canvas.DrawRoundRect(new SKRect(5.5f, 5.5f, 26.5f, 26.5f), 2.5f, 2.5f, sky);

    using var sun = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0xB9, 0x00) };
    canvas.DrawCircle(20.5f, 11.5f, 3f, sun);

    using var hill = new SKPaint { IsAntialias = true, Color = new SKColor(0x00, 0x67, 0xC0) };
    using var path = new SKPathBuilder();
    path.MoveTo(5.5f, 24f);
    path.LineTo(12.5f, 13.5f);
    path.LineTo(18f, 21f);
    path.LineTo(21f, 17.5f);
    path.LineTo(26.5f, 24f);
    path.LineTo(26.5f, 26.5f);
    path.LineTo(5.5f, 26.5f);
    path.Close();
    using var shape = path.Detach();
    canvas.DrawPath(shape, hill);

    using var data = bitmap.PeekPixels().Encode(new SKPngEncoderOptions())!;
    return data.ToArray();
}
