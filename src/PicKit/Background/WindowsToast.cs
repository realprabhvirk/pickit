using System.Runtime.InteropServices;
using PicKit.Core.Background;
using PicKit.Core.Diagnostics;

namespace PicKit.Background;

/// <summary>
/// Shows a real Windows notification, which stays in the notification centre afterwards. This
/// talks to the Windows Runtime directly, because the usual .NET projection would add over
/// 20 MB to the install and memory to a helper that has to stay small.
/// </summary>
internal static class WindowsToast
{
    private const string ManagerClass = "Windows.UI.Notifications.ToastNotificationManager";
    private const string NotificationClass = "Windows.UI.Notifications.ToastNotification";
    private const string XmlDocumentClass = "Windows.Data.Xml.Dom.XmlDocument";

    public static void Show(string xml)
    {
        try
        {
            var notification = Create(xml);
            var manager = (IToastNotificationManagerStatics)Factory(ManagerClass, typeof(IToastNotificationManagerStatics).GUID);
            using var appId = new RuntimeString(ToastContent.AppId);
            manager.CreateToastNotifierWithId(appId.Handle).Show(notification);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.Warn("Could not show a notification", ex);
        }
    }

    /// <summary>Builds a notification without showing it. Windows rejects anything malformed here.</summary>
    internal static IToastNotification Create(string xml)
    {
        using var documentClass = new RuntimeString(XmlDocumentClass);
        Check(RoActivateInstance(documentClass.Handle, out var instance));

        using var content = new RuntimeString(xml);
        ((IXmlDocumentIO)instance).LoadXml(content.Handle);

        var factory = (IToastNotificationFactory)Factory(NotificationClass, typeof(IToastNotificationFactory).GUID);
        return factory.CreateToastNotification((IXmlDocument)instance);
    }

    private static object Factory(string className, Guid interfaceId)
    {
        using var name = new RuntimeString(className);
        Check(RoGetActivationFactory(name.Handle, interfaceId, out var factory));
        return factory;
    }

    private static void Check(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(
        IntPtr classId,
        [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [DllImport("combase.dll")]
    private static extern int RoActivateInstance(IntPtr classId, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string text, int length, out IntPtr result);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr text);

    private sealed class RuntimeString : IDisposable
    {
        public RuntimeString(string text)
        {
            Check(WindowsCreateString(text, text.Length, out var handle));
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void Dispose() => WindowsDeleteString(Handle);
    }
}

// These mirror the Windows Runtime interfaces. Their method order is their layout, and the first
// three methods of each are the ones every Windows Runtime interface starts with.
#pragma warning disable SA1600

[ComImport]
[Guid("50AC103F-D235-4598-BBEF-98FE4D1A3AD4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotificationManagerStatics
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    void CreateToastNotifier();

    [return: MarshalAs(UnmanagedType.Interface)]
    IToastNotifier CreateToastNotifierWithId(IntPtr applicationId);
}

[ComImport]
[Guid("75927B93-03F3-41EC-91D3-6E5BAC1B38E7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotifier
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    void Show([MarshalAs(UnmanagedType.Interface)] IToastNotification notification);
}

[ComImport]
[Guid("04124B20-82C6-4229-B109-FD9ED4662B53")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotificationFactory
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    [return: MarshalAs(UnmanagedType.Interface)]
    IToastNotification CreateToastNotification([MarshalAs(UnmanagedType.Interface)] IXmlDocument content);
}

[ComImport]
[Guid("997E2675-059E-4E60-8B06-1760917C8B80")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToastNotification;

[ComImport]
[Guid("F7F3A506-1E87-42D6-BCFB-B8C809FA5494")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IXmlDocument;

[ComImport]
[Guid("6CD0E74E-EE65-4489-9EBF-CA43E87BA637")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IXmlDocumentIO
{
    void GetIids();

    void GetRuntimeClassName();

    void GetTrustLevel();

    void LoadXml(IntPtr xml);
}
