using System.IO;
using System.Runtime.CompilerServices;
using PicKit.Core.Diagnostics;

namespace PicKit.Tests;

/// <summary>Keeps everything the tests log or store away from the user's own PicKit data.</summary>
internal static class TestData
{
#pragma warning disable CA2255 // This has to run before any test touches the log.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RedirectAppData()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PicKitTests", $"data-{Environment.ProcessId}");
        AppPaths.Data = folder;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Scratch space in the temp folder, which Windows can clear later.
            }
        };
    }
}
