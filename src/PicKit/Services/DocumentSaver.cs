using System.IO;
using PicKit.Core.Diagnostics;
using PicKit.Core.Documents;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Core.Metadata;
using PicKit.Core.Settings;
using PicKit.ViewModels;
using SkiaSharp;

namespace PicKit.Services;

/// <summary>Everything between the user pressing Save and bytes landing safely on disk.</summary>
public sealed class DocumentSaver
{
    private readonly SettingsStore _settings;
    private readonly DialogService _dialogs;

    public DocumentSaver(SettingsStore settings, DialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
    }

    /// <summary>Saves over the current file, asking first unless the user turned that off.</summary>
    public Task<bool> SaveAsync(EditorViewModel editor)
    {
        if (editor.Document is not { } document)
        {
            return Task.FromResult(false);
        }

        var path = document.Source.Path;
        var format = document.Source.Format;
        if (path is null || !ImageFormats.CanEncode(format))
        {
            return SaveAsAsync(editor);
        }

        // A JPEG can't hold the transparency just added, so don't flatten it over the original.
        if (!ImageFormats.SupportsTransparency(format) && HasTransparency(editor, document))
        {
            return SaveAsAsync(editor, ExportService.OutputPathFor(path, "transparent", ImageFormat.Png), ImageFormat.Png);
        }

        if (_settings.Current.ConfirmOverwrite && !ConfirmReplace(document, path))
        {
            return Task.FromResult(false);
        }

        return WriteAsync(editor, path, format, becomesSource: true);
    }

    /// <summary>Creates "photo-edited.jpg" beside the original.</summary>
    public Task<bool> SaveCopyAsync(EditorViewModel editor)
    {
        if (editor.Document is not { Source.Path: { } path } document || !ImageFormats.CanEncode(document.Source.Format))
        {
            return SaveAsAsync(editor);
        }

        var target = ExportService.OutputPathFor(path, "edited", document.Source.Format);
        return WriteAsync(editor, target, document.Source.Format, becomesSource: false);
    }

    public Task<bool> SaveAsAsync(EditorViewModel editor, string? suggestedPath = null, ImageFormat? suggestedFormat = null)
    {
        if (editor.Document is not { } document)
        {
            return Task.FromResult(false);
        }

        var suggested = suggestedPath ?? document.Source.Path ?? "Pasted image";
        var preferred = suggestedFormat
            ?? (ImageFormats.CanEncode(document.Source.Format) ? document.Source.Format : DefaultFormat());

        var picked = _dialogs.PickSavePath(suggested, preferred);
        if (picked is null)
        {
            return Task.FromResult(false);
        }

        var format = ImageFormats.FromExtension(picked);
        if (!ImageFormats.CanEncode(format))
        {
            format = preferred;
            picked = Path.ChangeExtension(picked, ImageFormats.Extension(format));
        }

        return WriteAsync(editor, picked, format, becomesSource: true);
    }

    /// <summary>Colour behind transparent pixels, or null if the user backed out.</summary>
    public SKColor? AskForBackground()
    {
        var fallback = SKColor.TryParse(_settings.Current.TransparencyBackground, out var parsed) ? parsed : SKColors.White;
        return _dialogs.ChooseBackground(fallback);
    }

    /// <summary>Quality for a plain save, from the user's settings.</summary>
    public int QualityFor(ImageFormat format) =>
        format == ImageFormat.WebP ? _settings.Current.WebPQuality : _settings.Current.JpegQuality;

    /// <summary>The source's EXIF, if the user wants it kept and the pixels are still the original's.</summary>
    public byte[]? ExifToKeep(EditorDocument document, ImageFormat format)
    {
        if (_settings.Current.MetadataHandling != MetadataHandling.Keep
            || format != ImageFormat.Jpeg
            || document.Source.Format != ImageFormat.Jpeg
            || document.HasPixelEdits
            || document.Markups.Length > 0
            || document.Source.Path is not { } path)
        {
            return null;
        }

        try
        {
            return ExifCarryOver.ExtractUprightExif(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not read the original's metadata", ex);
            return null;
        }
    }

    private static bool HasTransparency(EditorViewModel editor, EditorDocument document)
    {
        editor.FlushPendingEdits();
        return document.Pixels.HasTransparency();
    }

    private ImageFormat DefaultFormat() =>
        Enum.TryParse<ImageFormat>(_settings.Current.DefaultExportFormat, out var format) && ImageFormats.CanEncode(format)
            ? format
            : ImageFormat.Png;

    private bool ConfirmReplace(EditorDocument document, string path)
    {
        var note = document.Source.FrameCount > 1
            ? "\n\nThis is an animated image. Saving keeps only the first frame."
            : string.Empty;

        return _dialogs.Confirm(
            "Save changes",
            $"Save changes to:\n\n{Path.GetFileName(path)}\n\nThis will replace the existing file.{note}",
            "Replace");
    }

    private async Task<bool> WriteAsync(EditorViewModel editor, string path, ImageFormat format, bool becomesSource)
    {
        if (editor.Document is not { } document)
        {
            return false;
        }

        editor.FlushPendingEdits();

        using var flattened = document.RenderFlattened();
        SKColor? background = null;
        if (format == ImageFormat.Jpeg && flattened.HasTransparency())
        {
            background = AskForBackground();
            if (background is null)
            {
                return false;
            }
        }

        var options = new EncodeOptions(format, QualityFor(format), background);
        var exif = ExifToKeep(document, format);

        editor.BeginBusy("Saving...");
        try
        {
            var dpi = document.PrintDpi;
            var result = await Task.Run(() => ExportService.Save(flattened, options, path, exif, dpi));

            if (becomesSource)
            {
                document.MarkSaved(new DocumentSource(path, format, result.Size));
                editor.NotifySaved();
            }

            return true;
        }
        catch (ImageException ex)
        {
            Log.Warn($"Save to {path} failed", ex);
            _dialogs.ShowError("Couldn't save", ex.Message, ex.Details);
            return false;
        }
        finally
        {
            editor.EndBusy();
        }
    }
}
