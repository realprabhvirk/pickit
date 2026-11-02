using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using PicKit.Core.Diagnostics;

namespace PicKit.Core.Files;

/// <summary>How new names are built. Tokens: {name}, {number}, {date}, {original}.</summary>
public sealed record RenameRule(string Pattern, int StartNumber = 1, int Padding = 3);

/// <summary>One line of the preview. A set of names can only be applied if none has a problem.</summary>
public sealed record RenamePreview(string Source, string NewName, string? Problem)
{
    public string OldName => Path.GetFileName(Source);

    public string Target => Path.Combine(Path.GetDirectoryName(Source) ?? string.Empty, NewName);

    public bool IsUnchanged => string.Equals(OldName, NewName, StringComparison.Ordinal);
}

public sealed record RenamedFile(string From, string To);

/// <summary>Enough to reverse the last rename, even after PicKit has been closed.</summary>
public sealed record RenameOperation(DateTimeOffset When, IReadOnlyList<RenamedFile> Files);

public sealed class RenameException : IOException
{
    public RenameException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public static partial class RenameEngine
{
    private const int MaxNameLength = 240;

    /// <summary>Works out every new name and checks each one, without touching a single file.</summary>
    public static IReadOnlyList<RenamePreview> Preview(IReadOnlyList<string> files, RenameRule rule, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var sources = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var names = files.Select((file, index) => BuildName(file, rule, index)).ToList();

        var taken = names
            .Select((name, index) => Path.Combine(Path.GetDirectoryName(files[index]) ?? string.Empty, name))
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<RenamePreview>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var target = Path.Combine(Path.GetDirectoryName(files[i]) ?? string.Empty, names[i]);
            results.Add(new RenamePreview(files[i], names[i], Check(names[i], target, taken, sources, exists)));
        }

        return results;
    }

    /// <summary>
    /// Renames in two stages, through temporary names, so swaps like a to b and b to a work.
    /// If anything fails, every file goes back to its old name.
    /// </summary>
    public static RenameOperation Apply(IReadOnlyList<RenamePreview> preview)
    {
        if (preview.Any(item => item.Problem is not null))
        {
            throw new RenameException("Some names have problems, so nothing was renamed.");
        }

        var moves = preview.Where(item => !item.IsUnchanged).Select(item => new RenamedFile(item.Source, item.Target)).ToList();
        foreach (var move in moves)
        {
            var isSwapping = moves.Any(other => string.Equals(other.From, move.To, StringComparison.OrdinalIgnoreCase));
            var isCaseChange = string.Equals(move.From, move.To, StringComparison.OrdinalIgnoreCase);
            if (!isSwapping && !isCaseChange && File.Exists(move.To))
            {
                throw new RenameException($"{Path.GetFileName(move.To)} already exists, so nothing was renamed.");
            }
        }

        Move(moves);
        return new RenameOperation(DateTimeOffset.Now, moves);
    }

    /// <summary>Puts names back. Refuses if the files have since been moved or replaced.</summary>
    public static void Undo(RenameOperation operation)
    {
        var moves = operation.Files.Select(file => new RenamedFile(file.To, file.From)).ToList();
        foreach (var move in moves)
        {
            if (!File.Exists(move.From))
            {
                throw new RenameException($"{Path.GetFileName(move.From)} has been moved or deleted, so nothing was changed.");
            }

            var isSwapping = moves.Any(other => string.Equals(other.From, move.To, StringComparison.OrdinalIgnoreCase));
            var isCaseChange = string.Equals(move.From, move.To, StringComparison.OrdinalIgnoreCase);
            if (!isSwapping && !isCaseChange && File.Exists(move.To))
            {
                throw new RenameException($"{Path.GetFileName(move.To)} already exists, so nothing was changed.");
            }
        }

        Move(moves);
    }

    public static void SaveLast(RenameOperation operation, string? path = null)
    {
        try
        {
            SafeFile.WriteAllText(path ?? AppPaths.RenameHistory, JsonSerializer.Serialize(operation, RenameJsonContext.Default.RenameOperation));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not record the rename", ex);
        }
    }

    public static RenameOperation? LoadLast(string? path = null)
    {
        try
        {
            var file = path ?? AppPaths.RenameHistory;
            return File.Exists(file)
                ? JsonSerializer.Deserialize(File.ReadAllText(file), RenameJsonContext.Default.RenameOperation)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn("Could not read the rename history", ex);
            return null;
        }
    }

    public static void ClearLast(string? path = null) => SafeFile.TryDelete(path ?? AppPaths.RenameHistory);

    /// <summary>Sorts as File Explorer does, so "2.jpg" comes before "10.jpg".</summary>
    public static IReadOnlyList<string> SortLikeExplorer(IEnumerable<string> files) =>
        files.Order(Comparer<string>.Create((a, b) => StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)))).ToList();

    private static void Move(List<RenamedFile> moves)
    {
        var parked = new List<(RenamedFile Move, string Temporary)>();
        var finished = new List<RenamedFile>();
        try
        {
            foreach (var move in moves)
            {
                var folder = Path.GetDirectoryName(move.From) ?? string.Empty;
                var temporary = Path.Combine(folder, $".pickit-rename-{Guid.NewGuid():N}.tmp");
                File.Move(move.From, temporary);
                parked.Add((move, temporary));
            }

            foreach (var (move, temporary) in parked)
            {
                File.Move(temporary, move.To);
                finished.Add(move);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RollBack(parked, finished);
            throw new RenameException($"A file couldn't be renamed, so all names were put back.\n\n{ex.Message}", ex);
        }
    }

    private static void RollBack(List<(RenamedFile Move, string Temporary)> parked, List<RenamedFile> finished)
    {
        foreach (var (move, temporary) in parked)
        {
            try
            {
                File.Move(finished.Contains(move) ? move.To : temporary, move.From);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Could not restore {move.From}", ex);
            }
        }
    }

    private static string BuildName(string file, RenameRule rule, int index)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        var extension = Path.GetExtension(file);
        var number = (rule.StartNumber + index).ToString(new string('0', Math.Clamp(rule.Padding, 1, 9)), CultureInfo.InvariantCulture);

        var name = rule.Pattern
            .Replace("{name}", stem, StringComparison.OrdinalIgnoreCase)
            .Replace("{original}", stem, StringComparison.OrdinalIgnoreCase)
            .Replace("{number}", number, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", DateOf(file), StringComparison.OrdinalIgnoreCase);

        return name + extension;
    }

    private static string DateOf(string file)
    {
        var written = File.Exists(file) ? File.GetLastWriteTime(file) : DateTime.Today;
        return written.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string? Check(string name, string target, HashSet<string> taken, HashSet<string> sources, Func<string, bool> exists)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return "The name is empty.";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "The name contains a character Windows doesn't allow.";
        }

        if (FileNames.IsReservedDeviceName(name))
        {
            return "Windows reserves this name.";
        }

        if (name.EndsWith(' ') || stem.EndsWith('.') || stem.EndsWith(' '))
        {
            return "Windows doesn't allow a name to end with a space or dot.";
        }

        if (name.Length > MaxNameLength)
        {
            return "The name is too long.";
        }

        if (taken.Contains(target))
        {
            return "More than one file would get this name.";
        }

        // A name held by another selected file is fine: that file is moving out of the way.
        return !sources.Contains(target) && exists(target) ? "A file with this name already exists." : null;
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int StrCmpLogicalW(string first, string second);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RenameOperation))]
internal sealed partial class RenameJsonContext : JsonSerializerContext;
