using System.Collections.Concurrent;
using SkiaSharp;

namespace PicKit.Core.Markup;

/// <summary>The fonts offered for text. Each one ships with Windows, so nothing needs installing.</summary>
public static class TextFonts
{
    public const string Default = "Segoe UI";

    private static readonly string[] Presets =
    [
        Default,
        "Arial",
        "Calibri",
        "Georgia",
        "Times New Roman",
        "Verdana",
        "Trebuchet MS",
        "Consolas",
        "Courier New",
        "Impact",
        "Comic Sans MS",
        "Segoe Script",
    ];

    private static readonly ConcurrentDictionary<string, SKTypeface> Typefaces = new();

    private static readonly Lazy<IReadOnlyList<string>> Installed = new(() =>
        Presets.Where(IsInstalled).ToArray());

    /// <summary>The presets on this PC. A cut-down Windows can be missing some of them.</summary>
    public static IReadOnlyList<string> Available => Installed.Value;

    public static SKTypeface Typeface(string family) =>
        Typefaces.GetOrAdd(family, name => SKTypeface.FromFamilyName(name) ?? SKTypeface.Default);

    private static bool IsInstalled(string family) =>
        string.Equals(Typeface(family).FamilyName, family, StringComparison.OrdinalIgnoreCase);
}
