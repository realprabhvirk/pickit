namespace PicKit.Core.Imaging;

public enum ImageErrorKind
{
    Unreadable,
    Unsupported,
    TooLarge,
    EncodeFailed,
}

/// <summary>A failure with a message that is safe to show directly to the user.</summary>
public sealed class ImageException : Exception
{
    public ImageException(ImageErrorKind kind, string message, string? details = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        Details = details;
    }

    public ImageErrorKind Kind { get; }

    /// <summary>Technical detail, shown behind a "Details" expander.</summary>
    public string? Details { get; }
}
