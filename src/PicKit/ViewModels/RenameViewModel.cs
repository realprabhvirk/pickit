using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Diagnostics;
using PicKit.Core.Files;

namespace PicKit.ViewModels;

/// <summary>Batch rename. Nothing changes until the preview is clean and the user presses Rename.</summary>
public sealed partial class RenameViewModel : ObservableObject
{
    private readonly Action _close;
    private readonly Action<IReadOnlyList<RenamedFile>> _renamed;
    private readonly string? _historyPath;
    private IReadOnlyList<string> _paths;

    [ObservableProperty]
    private string _pattern = "{name}_{number}";

    [ObservableProperty]
    private string _startText = "1";

    [ObservableProperty]
    private string _paddingText = "3";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private IReadOnlyList<RenamePreview> _preview = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private string? _problemText;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyPropertyChangedFor(nameof(CanUndoRename))]
    private RenameOperation? _lastOperation;

    public RenameViewModel(
        IReadOnlyList<string> paths,
        Action close,
        Action<IReadOnlyList<RenamedFile>> renamed,
        string? historyPath = null)
    {
        _paths = RenameEngine.SortLikeExplorer(paths);
        _close = close;
        _renamed = renamed;
        _historyPath = historyPath;
        _lastOperation = RenameEngine.LoadLast(historyPath);
        Refresh();
    }

    public int Total => _paths.Count;

    public string Heading => Total == 1 ? "Rename 1 image" : $"Rename {Total} images";

    public string ConfirmText => Total == 1 ? "1 file will be renamed." : $"{Total} files will be renamed.";

    public bool CanUndoRename => LastOperation is { Files.Count: > 0 };

    partial void OnPatternChanged(string value) => Refresh();

    partial void OnStartTextChanged(string value) => Refresh();

    partial void OnPaddingTextChanged(string value) => Refresh();

    private void Refresh()
    {
        ResultText = null;
        if (!int.TryParse(StartText, out var start) || start < 0)
        {
            ProblemText = "Enter a starting number.";
            return;
        }

        if (!int.TryParse(PaddingText, out var padding) || padding is < 1 or > 9)
        {
            ProblemText = "Enter a number of digits from 1 to 9.";
            return;
        }

        Preview = RenameEngine.Preview(_paths, new RenameRule(Pattern, start, padding));

        var problems = Preview.Count(item => item.Problem is not null);
        ProblemText = problems switch
        {
            0 => null,
            1 => $"{Preview.First(item => item.Problem is not null).Problem} Nothing has been renamed.",
            _ => $"{problems} names have problems, shown below. Nothing has been renamed.",
        };
    }

    private bool CanRename() => ProblemText is null && Preview.Any(item => !item.IsUnchanged);

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        try
        {
            var operation = RenameEngine.Apply(Preview);
            RenameEngine.SaveLast(operation, _historyPath);
            LastOperation = operation;
            _paths = Preview.Select(item => item.Target).ToList();
            _renamed(operation.Files);

            Refresh();
            ResultText = operation.Files.Count == 1 ? "1 file renamed." : $"{operation.Files.Count} files renamed.";
        }
        catch (RenameException ex)
        {
            Log.Warn("Rename failed", ex);
            ProblemText = ex.Message;
        }
    }

    private bool CanRunUndo() => CanUndoRename;

    [RelayCommand(CanExecute = nameof(CanRunUndo))]
    private void Undo()
    {
        if (LastOperation is not { } operation)
        {
            return;
        }

        try
        {
            RenameEngine.Undo(operation);
            RenameEngine.ClearLast(_historyPath);

            var restored = operation.Files.Select(file => new RenamedFile(file.To, file.From)).ToList();
            _paths = _paths.Select(path => restored.FirstOrDefault(file => file.From == path)?.To ?? path).ToList();
            _renamed(restored);

            LastOperation = null;
            Refresh();
            ResultText = "Names put back.";
        }
        catch (RenameException ex)
        {
            Log.Warn("Undo rename failed", ex);
            ProblemText = ex.Message;
        }
    }

    [RelayCommand]
    private void InsertToken(string token) => Pattern += token;

    [RelayCommand]
    private void Close() => _close();
}
