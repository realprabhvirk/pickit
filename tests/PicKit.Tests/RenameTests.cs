using PicKit.Core.Files;

namespace PicKit.Tests;

public class RenameTests
{
    private static readonly Func<string, bool> NothingExists = _ => false;

    private static string[] Files(params string[] names) => names.Select(name => $@"C:\photos\{name}").ToArray();

    private static string CreateFiles(out List<string> paths, params string[] names)
    {
        var folder = TestImages.TempFolder();
        paths = names.Select(name => Path.Combine(folder, name)).ToList();
        foreach (var path in paths)
        {
            File.WriteAllText(path, Path.GetFileName(path));
        }

        return folder;
    }

    [Fact]
    public void Numbers_count_up_with_padding_and_extensions_stay()
    {
        var preview = RenameEngine.Preview(Files("a.jpg", "b.png", "c.jpg"), new RenameRule("Holiday_{number}"), NothingExists);

        Assert.Equal(["Holiday_001.jpg", "Holiday_002.png", "Holiday_003.jpg"], preview.Select(item => item.NewName));
        Assert.All(preview, item => Assert.Null(item.Problem));
    }

    [Theory]
    [InlineData(1, 3, "001")]
    [InlineData(1, 1, "1")]
    [InlineData(7, 2, "07")]
    [InlineData(100, 2, "100")]
    public void Start_number_and_padding_are_respected(int start, int padding, string expected)
    {
        var preview = RenameEngine.Preview(Files("a.jpg"), new RenameRule("x{number}", start, padding), NothingExists);

        Assert.Equal($"x{expected}.jpg", preview[0].NewName);
    }

    [Fact]
    public void Name_token_keeps_the_original_name()
    {
        var preview = RenameEngine.Preview(Files("sunset.jpg"), new RenameRule("trip-{name}-{number}"), NothingExists);

        Assert.Equal("trip-sunset-001.jpg", preview[0].NewName);
    }

    [Fact]
    public void Date_token_uses_the_files_date()
    {
        CreateFiles(out var paths, "a.jpg");
        File.SetLastWriteTime(paths[0], new DateTime(2024, 3, 9));

        var preview = RenameEngine.Preview(paths, new RenameRule("{date}_{number}"));

        Assert.Equal("2024-03-09_001.jpg", preview[0].NewName);
    }

    [Fact]
    public void Tokens_ignore_case()
    {
        var preview = RenameEngine.Preview(Files("a.jpg"), new RenameRule("{NAME}_{Number}"), NothingExists);

        Assert.Equal("a_001.jpg", preview[0].NewName);
    }

    [Fact]
    public void Preview_never_touches_the_files()
    {
        CreateFiles(out var paths, "a.jpg", "b.jpg");

        RenameEngine.Preview(paths, new RenameRule("new_{number}"));

        Assert.All(paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void Collision_with_a_file_outside_the_selection_is_reported()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\photos\Holiday_001.jpg" };

        var preview = RenameEngine.Preview(Files("a.jpg"), new RenameRule("Holiday_{number}"), existing.Contains);

        Assert.Contains("already exists", preview[0].Problem);
    }

    [Fact]
    public void Two_files_given_the_same_name_are_reported()
    {
        var preview = RenameEngine.Preview(Files("a.jpg", "b.jpg"), new RenameRule("same"), NothingExists);

        Assert.All(preview, item => Assert.Contains("More than one", item.Problem));
    }

    [Fact]
    public void Same_name_with_different_extensions_is_allowed()
    {
        var preview = RenameEngine.Preview(Files("a.jpg", "b.png"), new RenameRule("same"), NothingExists);

        Assert.All(preview, item => Assert.Null(item.Problem));
    }

    [Theory]
    [InlineData("bad/name")]
    [InlineData("bad:name")]
    [InlineData("bad?name")]
    [InlineData("bad*name")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CON")]
    [InlineData("LPT1")]
    [InlineData("trailing.")]
    public void Invalid_names_are_reported(string pattern)
    {
        var preview = RenameEngine.Preview(Files("a.jpg"), new RenameRule(pattern), NothingExists);

        Assert.NotNull(preview[0].Problem);
    }

    [Fact]
    public void Overlong_names_are_reported()
    {
        var preview = RenameEngine.Preview(Files("a.jpg"), new RenameRule(new string('x', 300)), NothingExists);

        Assert.Contains("too long", preview[0].Problem);
    }

    [Fact]
    public void Apply_renames_and_keeps_contents()
    {
        CreateFiles(out var paths, "a.jpg", "b.jpg");
        var folder = Path.GetDirectoryName(paths[0])!;

        RenameEngine.Apply(RenameEngine.Preview(paths, new RenameRule("new_{number}")));

        Assert.Equal("a.jpg", File.ReadAllText(Path.Combine(folder, "new_001.jpg")));
        Assert.Equal("b.jpg", File.ReadAllText(Path.Combine(folder, "new_002.jpg")));
        Assert.False(File.Exists(paths[0]));
    }

    [Fact]
    public void Apply_refuses_when_any_name_has_a_problem_and_changes_nothing()
    {
        CreateFiles(out var paths, "a.jpg", "b.jpg", "new_002.jpg");

        var selected = paths.Take(2).ToList();
        var preview = RenameEngine.Preview(selected, new RenameRule("new_{number}"));

        Assert.Throws<RenameException>(() => RenameEngine.Apply(preview));
        Assert.All(paths, path => Assert.True(File.Exists(path)));
        Assert.Equal("new_002.jpg", File.ReadAllText(paths[2]));
    }

    [Fact]
    public void Apply_catches_a_collision_that_appeared_after_the_preview()
    {
        CreateFiles(out var paths, "a.jpg");
        var folder = Path.GetDirectoryName(paths[0])!;
        var preview = RenameEngine.Preview(paths, new RenameRule("new_{number}"));

        File.WriteAllText(Path.Combine(folder, "new_001.jpg"), "someone else's file");

        Assert.Throws<RenameException>(() => RenameEngine.Apply(preview));
        Assert.Equal("someone else's file", File.ReadAllText(Path.Combine(folder, "new_001.jpg")));
        Assert.True(File.Exists(paths[0]));
    }

    [Fact]
    public void Files_can_swap_names()
    {
        CreateFiles(out var paths, "1.jpg", "2.jpg");
        var folder = Path.GetDirectoryName(paths[0])!;

        // Reversing the order gives each file the other's name.
        var preview = RenameEngine.Preview([paths[1], paths[0]], new RenameRule("{number}", 1, 1));
        Assert.All(preview, item => Assert.Null(item.Problem));

        RenameEngine.Apply(preview);

        Assert.Equal("2.jpg", File.ReadAllText(Path.Combine(folder, "1.jpg")));
        Assert.Equal("1.jpg", File.ReadAllText(Path.Combine(folder, "2.jpg")));
    }

    [Fact]
    public void Changing_only_the_case_works()
    {
        CreateFiles(out var paths, "photo.jpg");
        var folder = Path.GetDirectoryName(paths[0])!;

        var preview = RenameEngine.Preview(paths, new RenameRule("PHOTO"));
        Assert.Null(preview[0].Problem);
        RenameEngine.Apply(preview);

        Assert.Equal("PHOTO.jpg", Path.GetFileName(Directory.GetFiles(folder).Single()));
    }

    [Fact]
    public void Failure_midway_puts_every_name_back()
    {
        CreateFiles(out var paths, "a.jpg", "b.jpg", "c.jpg");

        // A file that's open can't be renamed, which fails partway through.
        using var locked = new FileStream(paths[2], FileMode.Open, FileAccess.Read, FileShare.None);
        var preview = RenameEngine.Preview(paths, new RenameRule("new_{number}"));

        Assert.Throws<RenameException>(() => RenameEngine.Apply(preview));

        Assert.All(paths, path => Assert.True(File.Exists(path), path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(paths[0])!, ".pickit-rename-*"));
    }

    [Fact]
    public void Undo_restores_the_original_names()
    {
        CreateFiles(out var paths, "a.jpg", "b.jpg");
        var operation = RenameEngine.Apply(RenameEngine.Preview(paths, new RenameRule("new_{number}")));

        RenameEngine.Undo(operation);

        Assert.Equal("a.jpg", File.ReadAllText(paths[0]));
        Assert.Equal("b.jpg", File.ReadAllText(paths[1]));
    }

    [Fact]
    public void Undo_refuses_if_a_file_has_since_gone()
    {
        CreateFiles(out var paths, "a.jpg", "b.jpg");
        var operation = RenameEngine.Apply(RenameEngine.Preview(paths, new RenameRule("new_{number}")));
        File.Delete(operation.Files[1].To);

        Assert.Throws<RenameException>(() => RenameEngine.Undo(operation));

        Assert.True(File.Exists(operation.Files[0].To));
    }

    [Fact]
    public void Undo_refuses_if_the_old_name_has_been_taken()
    {
        CreateFiles(out var paths, "a.jpg");
        var operation = RenameEngine.Apply(RenameEngine.Preview(paths, new RenameRule("new")));
        File.WriteAllText(paths[0], "a new file took this name");

        Assert.Throws<RenameException>(() => RenameEngine.Undo(operation));

        Assert.Equal("a new file took this name", File.ReadAllText(paths[0]));
    }

    [Fact]
    public void The_last_rename_survives_a_restart()
    {
        var historyFile = Path.Combine(TestImages.TempFolder(), "history.json");
        CreateFiles(out var paths, "a.jpg");
        var operation = RenameEngine.Apply(RenameEngine.Preview(paths, new RenameRule("new")));

        RenameEngine.SaveLast(operation, historyFile);
        var loaded = RenameEngine.LoadLast(historyFile)!;
        RenameEngine.Undo(loaded);

        Assert.True(File.Exists(paths[0]));
    }

    [Fact]
    public void Damaged_history_is_ignored()
    {
        var historyFile = Path.Combine(TestImages.TempFolder(), "history.json");
        File.WriteAllText(historyFile, "not json");

        Assert.Null(RenameEngine.LoadLast(historyFile));
    }

    [Fact]
    public void Order_matches_explorer()
    {
        var sorted = RenameEngine.SortLikeExplorer(Files("10.jpg", "2.jpg", "1.jpg"));

        Assert.Equal(["1.jpg", "2.jpg", "10.jpg"], sorted.Select(Path.GetFileName));
    }

    [Fact]
    public void Unchanged_names_are_left_alone()
    {
        CreateFiles(out var paths, "keep.jpg");

        var operation = RenameEngine.Apply(RenameEngine.Preview(paths, new RenameRule("{name}")));

        Assert.Empty(operation.Files);
        Assert.True(File.Exists(paths[0]));
    }
}
