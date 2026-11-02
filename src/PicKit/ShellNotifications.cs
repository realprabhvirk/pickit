using System.Runtime.InteropServices;

namespace PicKit;

internal static class ShellNotifications
{
    private const int AssocChanged = 0x08000000;

    /// <summary>Tells Explorer to pick up menu and "Open with" changes straight away.</summary>
    public static void AssociationsChanged() => SHChangeNotify(AssocChanged, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
