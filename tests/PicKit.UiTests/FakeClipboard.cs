using PicKit.Core.Imaging;
using PicKit.Services;

namespace PicKit.UiTests;

/// <summary>An in-memory clipboard, so tests can't overwrite what the user has copied.</summary>
internal sealed class FakeClipboard : ClipboardService
{
    public IReadOnlyList<ClipboardItem>? Contents { get; set; }

    public IReadOnlyList<string> Files { get; set; } = [];

    public bool IsLocked { get; set; }

    public override bool Set(IReadOnlyList<ClipboardItem> items)
    {
        if (IsLocked)
        {
            return false;
        }

        Contents = items;
        return true;
    }

    public override bool ContainsImage() => Contents is { Count: > 0 } || Files.Count > 0;

    public override ImageBuffer? ReadImage() => Contents is { } items
        ? ClipboardCodec.Decode(format => items.FirstOrDefault(item => item.Format == format)?.Data)
        : null;

    public override IReadOnlyList<string> ReadFiles() => Files;
}
