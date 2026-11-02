using PicKit.Core.Diagnostics;

namespace PicKit.Core.Documents;

/// <summary>
/// One undoable edit. A step holds whichever state is not currently on show, and undo and redo
/// are the same exchange, so a step can never get out of sync with its document.
/// </summary>
internal abstract class EditStep : IDisposable
{
    private static long _nextRevision;

    protected EditStep(string label)
    {
        Label = label;
    }

    public string Label { get; }

    public long Revision { get; } = Interlocked.Increment(ref _nextRevision);

    public virtual long BytesInMemory => 0;

    public abstract void Exchange(EditorDocument document);

    public virtual void MoveToDisk()
    {
    }

    public virtual void Dispose()
    {
    }
}

public sealed class EditHistory : IDisposable
{
    private const int MaxSteps = 64;

    private readonly List<EditStep> _undo = [];
    private readonly List<EditStep> _redo = [];
    private readonly long _memoryBudget;

    public EditHistory(long? memoryBudget = null)
    {
        _memoryBudget = memoryBudget ?? DefaultBudget();
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>Identifies the current state, for telling whether it differs from what was saved.</summary>
    public long Revision => _undo.Count > 0 ? _undo[^1].Revision : 0;

    public long BytesInMemory => _undo.Sum(step => step.BytesInMemory) + _redo.Sum(step => step.BytesInMemory);

    public void Dispose()
    {
        Clear(_undo);
        Clear(_redo);
    }

    internal void Push(EditStep step)
    {
        Clear(_redo);
        _undo.Add(step);

        while (_undo.Count > MaxSteps)
        {
            _undo[0].Dispose();
            _undo.RemoveAt(0);
        }

        KeepWithinBudget();
    }

    internal void Undo(EditorDocument document) => Move(_undo, _redo, document);

    internal void Redo(EditorDocument document) => Move(_redo, _undo, document);

    private static void Move(List<EditStep> from, List<EditStep> to, EditorDocument document)
    {
        if (from.Count == 0)
        {
            return;
        }

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(step);
        try
        {
            step.Exchange(document);
        }
        catch
        {
            // Nothing was exchanged, so the step stays exactly where it was.
            to.RemoveAt(to.Count - 1);
            from.Add(step);
            throw;
        }
    }

    private static void Clear(List<EditStep> steps)
    {
        foreach (var step in steps)
        {
            step.Dispose();
        }

        steps.Clear();
    }

    private static long DefaultBudget()
    {
        const long maximum = 2L * 1024 * 1024 * 1024;
        return Math.Min(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 4, maximum);
    }

    /// <summary>Moves the oldest steps to disk rather than discarding them, keeping undo complete.</summary>
    private void KeepWithinBudget()
    {
        var used = BytesInMemory;
        foreach (var step in _undo)
        {
            if (used <= _memoryBudget)
            {
                return;
            }

            var bytes = step.BytesInMemory;
            if (bytes == 0)
            {
                continue;
            }

            try
            {
                step.MoveToDisk();
                used -= bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A full disk costs some memory, not the user's undo history.
                Log.Warn("Could not move undo data to disk", ex);
                return;
            }
        }
    }
}
