using PicKit.Core.Settings;

namespace PicKit.Tests;

public class RecentFilesTests
{
    private static (RecentFiles Recent, string Store, string Folder) Create()
    {
        var folder = TestImages.TempFolder();
        var store = Path.Combine(folder, "recent.json");
        return (new RecentFiles(store), store, folder);
    }

    private static string Touch(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, [1]);
        return path;
    }

    [Fact]
    public void Newest_comes_first_without_duplicates()
    {
        var (recent, _, folder) = Create();
        var first = Touch(folder, "a.jpg");
        var second = Touch(folder, "b.jpg");

        recent.Add(first);
        recent.Add(second);
        recent.Add(first);

        Assert.Equal([first, second], recent.Existing);
    }

    [Fact]
    public void The_list_stays_modest()
    {
        var (recent, _, folder) = Create();

        for (var i = 0; i < 20; i++)
        {
            recent.Add(Touch(folder, $"{i}.jpg"));
        }

        Assert.Equal(8, recent.Existing.Count);
    }

    [Fact]
    public void Survives_a_restart()
    {
        var (recent, store, folder) = Create();
        var file = Touch(folder, "a.jpg");
        recent.Add(file);

        Assert.Equal([file], new RecentFiles(store).Existing);
    }

    [Fact]
    public void Deleted_files_drop_out()
    {
        var (recent, _, folder) = Create();
        var file = Touch(folder, "a.jpg");
        recent.Add(file);
        File.Delete(file);

        Assert.Empty(recent.Existing);
    }

    [Fact]
    public void Clearing_removes_the_stored_list()
    {
        var (recent, store, folder) = Create();
        recent.Add(Touch(folder, "a.jpg"));

        recent.Clear();

        Assert.Empty(recent.Existing);
        Assert.False(File.Exists(store));
    }

    [Fact]
    public void A_damaged_list_is_ignored()
    {
        var folder = TestImages.TempFolder();
        var store = Path.Combine(folder, "recent.json");
        File.WriteAllText(store, "{ not valid");

        Assert.Empty(new RecentFiles(store).Existing);
    }
}
