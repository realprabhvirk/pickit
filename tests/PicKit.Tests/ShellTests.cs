using Microsoft.Win32;
using PicKit.Core.Shell;

namespace PicKit.Tests;

/// <summary>
/// These write only under a throwaway key, so they never touch the real Explorer integration
/// or anything in the user's actual file associations.
/// </summary>
public sealed class ShellRegistrationTests : IDisposable
{
    private const string Executable = @"C:\Apps\PicKit\PicKit.exe";

    private readonly string _sandboxPath = $@"Software\PicKitTests\{Guid.NewGuid():N}";
    private readonly RegistryKey _classes;
    private readonly RegistryKey _software;
    private readonly ShellRegistration _registration;

    public ShellRegistrationTests()
    {
        _classes = Registry.CurrentUser.CreateSubKey($@"{_sandboxPath}\Classes");
        _software = Registry.CurrentUser.CreateSubKey($@"{_sandboxPath}\Software");
        _registration = new ShellRegistration(_classes, _software, Executable);
    }

    public void Dispose()
    {
        _classes.Dispose();
        _software.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_sandboxPath, throwOnMissingSubKey: false);
    }

    private object? Value(RegistryKey root, string path, string? name = null)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name);
    }

    [Fact]
    public void Menu_appears_on_common_image_types_as_one_submenu()
    {
        _registration.RegisterMenu();

        foreach (var extension in new[] { ".jpg", ".png", ".webp" })
        {
            var path = $@"SystemFileAssociations\{extension}\shell\PicKit";
            Assert.Equal("PicKit", Value(_classes, path, "MUIVerb"));
            Assert.Equal(ShellRegistration.MenuKey, Value(_classes, path, "ExtendedSubCommandsKey"));
        }
    }

    [Theory]
    [InlineData("01open", "--open")]
    [InlineData("02compress", "--compress")]
    [InlineData("03resize", "--resize")]
    [InlineData("04convert", "--convert")]
    [InlineData("05copy", "--copy")]
    [InlineData("06background", "--remove-background")]
    [InlineData("07rename", "--rename")]
    public void Each_menu_item_runs_its_action_with_the_path_safely_quoted(string verb, string expectedSwitch)
    {
        _registration.RegisterMenu();

        var command = (string?)Value(_classes, $@"{ShellRegistration.MenuKey}\shell\{verb}\command");

        Assert.Equal($"\"{Executable}\" {expectedSwitch} -- \"%1\"", command);
    }

    [Fact]
    public void Menu_items_accept_multiple_selected_files()
    {
        _registration.RegisterMenu();

        Assert.Equal("Player", Value(_classes, $@"{ShellRegistration.MenuKey}\shell\02compress", "MultiSelectModel"));
    }

    [Fact]
    public void Menu_registration_does_not_add_open_with()
    {
        _registration.RegisterMenu();

        Assert.True(_registration.IsMenuRegistered);
        Assert.False(_registration.IsOpenWithRegistered);
    }

    [Fact]
    public void Open_with_offers_pickit_without_taking_over_the_default()
    {
        using (var jpg = _classes.CreateSubKey(".jpg"))
        {
            jpg.SetValue(null, "SomeOtherApp.Image");
        }

        _registration.RegisterOpenWith();

        // The extension's own default is left exactly as the user had it.
        Assert.Equal("SomeOtherApp.Image", Value(_classes, ".jpg"));
        using var openWith = _classes.OpenSubKey(@".jpg\OpenWithProgids");
        Assert.Contains(ShellRegistration.ProgId, openWith!.GetValueNames());
        Assert.Equal(@"Software\PicKit\Capabilities", Value(_software, "RegisteredApplications", "PicKit"));
        Assert.Equal($"\"{Executable}\" \"%1\"", Value(_classes, $@"{ShellRegistration.ProgId}\shell\open\command"));
    }

    [Fact]
    public void Open_with_never_writes_user_choice()
    {
        _registration.RegisterOpenWith();

        Assert.Null(Value(_classes, @".jpg\UserChoice", "ProgId"));
        using var jpg = _classes.OpenSubKey(".jpg");
        Assert.DoesNotContain("UserChoice", jpg!.GetSubKeyNames());
    }

    [Fact]
    public void Uninstalling_removes_everything_pickit_added()
    {
        _registration.RegisterMenu();
        _registration.RegisterOpenWith();

        _registration.UnregisterAll();

        Assert.Empty(_classes.GetSubKeyNames());
        Assert.DoesNotContain("PicKit", _software.GetSubKeyNames());
        using var registered = _software.OpenSubKey("RegisteredApplications");
        Assert.Empty(registered?.GetValueNames() ?? []);
    }

    [Fact]
    public void Uninstalling_leaves_other_apps_entries_alone()
    {
        using (var other = _classes.CreateSubKey(@"SystemFileAssociations\.jpg\shell\OtherApp"))
        {
            other.SetValue(null, "Their verb");
        }

        using (var extension = _classes.CreateSubKey(".jpg"))
        {
            extension.SetValue(null, "SomeOtherApp.Image");
            using var openWith = extension.CreateSubKey("OpenWithProgids");
            openWith.SetValue("SomeOtherApp.Image", Array.Empty<byte>(), RegistryValueKind.None);
        }

        _registration.RegisterMenu();
        _registration.RegisterOpenWith();
        _registration.UnregisterAll();

        Assert.Equal("Their verb", Value(_classes, @"SystemFileAssociations\.jpg\shell\OtherApp"));
        Assert.Equal("SomeOtherApp.Image", Value(_classes, ".jpg"));
        using var remaining = _classes.OpenSubKey(@".jpg\OpenWithProgids");
        Assert.Equal(["SomeOtherApp.Image"], remaining!.GetValueNames());
    }

    [Fact]
    public void Uninstalling_twice_is_harmless()
    {
        _registration.UnregisterAll();
        _registration.UnregisterAll();
    }

    [Fact]
    public void Registering_again_moves_to_a_new_install_location()
    {
        _registration.RegisterMenu();

        new ShellRegistration(_classes, _software, @"D:\Moved\PicKit.exe").RegisterMenu();

        var command = (string?)Value(_classes, $@"{ShellRegistration.MenuKey}\shell\01open\command");
        Assert.StartsWith("\"D:\\Moved\\PicKit.exe\"", command);
    }
}

public class LaunchRequestTests
{
    [Fact]
    public void Plain_paths_mean_open()
    {
        var request = LaunchRequest.Parse([@"C:\a.jpg", @"C:\b.png"]);

        Assert.Equal(ShellAction.Open, request.Action);
        Assert.Equal([@"C:\a.jpg", @"C:\b.png"], request.Paths);
    }

    [Theory]
    [InlineData("--compress", ShellAction.Compress)]
    [InlineData("--RESIZE", ShellAction.Resize)]
    [InlineData("--remove-background", ShellAction.RemoveBackground)]
    public void Switches_select_the_action(string flag, ShellAction expected)
    {
        var request = LaunchRequest.Parse([flag, "--", @"C:\a.jpg"]);

        Assert.Equal(expected, request.Action);
        Assert.Equal([@"C:\a.jpg"], request.Paths);
    }

    [Fact]
    public void A_file_named_like_a_switch_is_still_a_file()
    {
        var request = LaunchRequest.Parse(["--compress", "--", "--unregister"]);

        Assert.Equal(ShellAction.Compress, request.Action);
        Assert.Equal(["--unregister"], request.Paths);
    }

    [Fact]
    public void Unknown_switches_are_treated_as_paths_not_obeyed()
    {
        var request = LaunchRequest.Parse(["--do-something-odd"]);

        Assert.Equal(ShellAction.Open, request.Action);
        Assert.Equal(["--do-something-odd"], request.Paths);
    }

    [Fact]
    public void No_arguments_just_opens_the_app()
    {
        var request = LaunchRequest.Parse([]);

        Assert.Equal(ShellAction.Open, request.Action);
        Assert.Empty(request.Paths);
    }
}

public class ImagePathsTests
{
    [Fact]
    public void Folders_are_searched_and_other_files_ignored()
    {
        var folder = TestImages.TempFolder();
        var nested = Directory.CreateDirectory(Path.Combine(folder, "more")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "a.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "notes.txt"), [1]);
        File.WriteAllBytes(Path.Combine(nested, "b.PNG"), [1]);

        var found = ImagePaths.Expand([folder]);

        Assert.Equal(2, found.Count);
        Assert.DoesNotContain(found, path => path.EndsWith(".txt"));
    }

    [Fact]
    public void Duplicates_and_missing_paths_are_dropped()
    {
        var folder = TestImages.TempFolder();
        var file = Path.Combine(folder, "a.jpg");
        File.WriteAllBytes(file, [1]);

        var found = ImagePaths.Expand([file, file.ToUpperInvariant(), Path.Combine(folder, "gone.jpg")]);

        Assert.Equal([file], found);
    }

    [Fact]
    public void Hidden_files_are_left_out_of_folder_scans()
    {
        var folder = TestImages.TempFolder();
        var hidden = Path.Combine(folder, "hidden.jpg");
        File.WriteAllBytes(hidden, [1]);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.Empty(ImagePaths.Expand([folder]));
    }
}

public class InstanceChannelTests
{
    private static string UniqueName() => "PicKitTest." + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task First_instance_is_primary_and_second_is_not()
    {
        var name = UniqueName();
        using var first = new InstanceChannel(name);
        Assert.True(first.IsPrimary);

        var secondIsPrimary = await Task.Run(() =>
        {
            using var channel = new InstanceChannel(name);
            return channel.IsPrimary;
        });

        Assert.False(secondIsPrimary);
    }

    [Fact]
    public async Task Requests_reach_the_primary_instance_with_all_their_paths()
    {
        var name = UniqueName();
        var received = new TaskCompletionSource<LaunchRequest>();
        using var primary = new InstanceChannel(name);
        primary.Listen(request => received.TrySetResult(request));

        var sent = await Task.Run(() =>
        {
            using var secondary = new InstanceChannel(name);
            return secondary.Send(new LaunchRequest(ShellAction.Compress, [@"C:\photos\holiday.jpg", @"C:\photos\é.png"]));
        });

        Assert.True(sent);
        var request = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ShellAction.Compress, request.Action);
        Assert.Equal([@"C:\photos\holiday.jpg", @"C:\photos\é.png"], request.Paths);
    }

    [Fact]
    public void A_burst_of_fifty_launches_all_arrive()
    {
        var name = UniqueName();
        var received = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var primary = new InstanceChannel(name);
        primary.Listen(request => received.Add(request.Paths[0]));

        // Explorer starts one process per selected file, all at about the same moment.
        Parallel.For(0, 50, i =>
        {
            using var secondary = new InstanceChannel(name);
            Assert.True(secondary.Send(new LaunchRequest(ShellAction.Compress, [$"file{i}.jpg"])));
        });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (received.Count < 50 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        Assert.Equal(50, received.Distinct().Count());
    }

    [Fact]
    public void Sending_with_nobody_listening_fails_cleanly()
    {
        using var lonely = new InstanceChannel(UniqueName());

        Assert.False(lonely.Send(new LaunchRequest(ShellAction.Open, [])));
    }
}
