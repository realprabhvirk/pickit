using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PicKit.Services;

/// <summary>The standard Windows colour dialog.</summary>
public sealed partial class ColorService
{
    private const int FullOpen = 0x00000002;
    private const int RgbInit = 0x00000001;

    private readonly int[] _customColors = new int[16];

    public Color? Pick(Color initial)
    {
        var customColors = GCHandle.Alloc(_customColors, GCHandleType.Pinned);
        try
        {
            var owner = Application.Current?.MainWindow is { } window ? new WindowInteropHelper(window).Handle : IntPtr.Zero;
            var options = new ChooseColorOptions
            {
                StructSize = Marshal.SizeOf<ChooseColorOptions>(),
                Owner = owner,
                Result = initial.R | (initial.G << 8) | (initial.B << 16),
                CustomColors = customColors.AddrOfPinnedObject(),
                Flags = FullOpen | RgbInit,
            };

            return ChooseColor(ref options)
                ? Color.FromRgb((byte)options.Result, (byte)(options.Result >> 8), (byte)(options.Result >> 16))
                : null;
        }
        finally
        {
            customColors.Free();
        }
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "ChooseColorW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChooseColor(ref ChooseColorOptions options);

    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorOptions
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public int Result;
        public IntPtr CustomColors;
        public int Flags;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
    }
}
