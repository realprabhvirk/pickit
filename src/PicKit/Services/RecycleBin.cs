using Microsoft.VisualBasic.FileIO;

namespace PicKit.Services;

/// <summary>Trash means the Recycle Bin, so a slip of the mouse can still be undone.</summary>
public static class RecycleBin
{
    public static void Send(string path) =>
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
}
