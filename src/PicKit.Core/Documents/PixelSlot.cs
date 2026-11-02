using PicKit.Core.Files;
using PicKit.Core.Imaging;

namespace PicKit.Core.Documents;

/// <summary>
/// Holds pixels for an undo step, either in memory or moved out to a temporary file when
/// memory is tight. Exactly one of the two exists at any time.
/// </summary>
internal sealed class PixelSlot : IDisposable
{
    private ImageBuffer? _memory;
    private string? _file;
    private int _width;
    private int _height;

    public PixelSlot(ImageBuffer pixels)
    {
        Store(pixels);
    }

    public long BytesInMemory => _memory?.ByteCount ?? 0;

    /// <summary>Hands the pixels to the caller, who then owns them.</summary>
    public ImageBuffer Take()
    {
        if (_memory is { } memory)
        {
            _memory = null;
            return memory;
        }

        var pixels = ImageBuffer.Create(_width, _height);
        using (var stream = File.OpenRead(_file!))
        {
            stream.ReadExactly(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.Pixels));
        }

        SafeFile.TryDelete(_file!);
        _file = null;
        return pixels;
    }

    public void Store(ImageBuffer pixels)
    {
        _memory = pixels;
        _width = pixels.Width;
        _height = pixels.Height;
    }

    public void MoveToDisk()
    {
        if (_memory is null)
        {
            return;
        }

        var path = TempFiles.NewPath(".pixels");
        using (var stream = File.Create(path))
        {
            stream.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes<uint>(_memory.Pixels));
        }

        _memory.Dispose();
        _memory = null;
        _file = path;
    }

    public void Dispose()
    {
        _memory?.Dispose();
        _memory = null;
        if (_file is not null)
        {
            SafeFile.TryDelete(_file);
            _file = null;
        }
    }
}
