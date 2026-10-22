using System.IO;
using System.Reflection;
using Xunit;

namespace MouseClicker.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MouseClickerTests-" + Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => Path.Combine(_dir, name);

    public SettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Save_and_load_round_trip()
    {
        string path = PathFor("settings.json");
        AppSettings settings = AppSettings.Load(path);
        settings.IntervalMs = 250;
        settings.ClickButton = ClickButton.Middle;
        settings.Save();

        AppSettings loaded = AppSettings.Load(path);
        Assert.Equal(250, loaded.IntervalMs);
        Assert.Equal(ClickButton.Middle, loaded.ClickButton);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Enums_are_saved_as_names_and_old_numbers_still_load()
    {
        string path = PathFor("settings.json");
        AppSettings settings = AppSettings.Load(path);
        settings.ClickButton = ClickButton.Right;
        settings.Save();
        Assert.Contains("\"Right\"", File.ReadAllText(path));

        File.WriteAllText(path, "{\"ClickButton\": 2}");
        Assert.Equal(ClickButton.Middle, AppSettings.Load(path).ClickButton);
    }

    [Fact]
    public void Corrupt_file_is_kept_as_bak_and_defaults_load()
    {
        string path = PathFor("settings.json");
        File.WriteAllText(path, "{ this is not json");

        AppSettings loaded = AppSettings.Load(path);
        Assert.Equal(new AppSettings().IntervalMs, loaded.IntervalMs);
        Assert.Equal("{ this is not json", File.ReadAllText(path + ".bak"));

        loaded.Save();
        Assert.Equal("{ this is not json", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Apply_profile_leaves_app_level_settings_alone()
    {
        var current = new AppSettings { StartWithWindows = true, MinimizeToTray = true, IntervalMs = 100 };
        current.Profiles.Add(new Profile { Name = "keep" });

        var saved = new AppSettings { StartWithWindows = false, MinimizeToTray = false, IntervalMs = 40 };
        current.ApplyProfile(saved);

        Assert.Equal(40, current.IntervalMs);
        Assert.True(current.StartWithWindows);
        Assert.True(current.MinimizeToTray);
        Assert.Single(current.Profiles);
    }

    [Fact]
    public void Snapshot_has_only_profile_level_settings()
    {
        var settings = new AppSettings { IntervalMs = 33, StartWithWindows = true, ActiveProfile = "x" };
        settings.Profiles.Add(new Profile { Name = "x" });

        AppSettings snapshot = settings.Snapshot();
        Assert.Equal(33, snapshot.IntervalMs);
        Assert.False(snapshot.StartWithWindows);
        Assert.Empty(snapshot.Profiles);
        Assert.Null(snapshot.ActiveProfile);
    }

    [Fact]
    public void App_level_settings_are_the_expected_ones()
    {
        // A new setting travels with profiles unless it is tagged [AppLevel]. This list makes that a conscious choice.
        string[] expected =
        {
            nameof(AppSettings.SchemaVersion), nameof(AppSettings.Profiles), nameof(AppSettings.ActiveProfile),
            nameof(AppSettings.ProfileNextVk), nameof(AppSettings.StartWithWindows), nameof(AppSettings.StartMinimized),
            nameof(AppSettings.MinimizeToTray), nameof(AppSettings.DarkMode),
        };

        string[] actual = typeof(AppSettings).GetProperties()
            .Where(p => p.GetCustomAttribute<AppLevelAttribute>() is not null)
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(expected.OrderBy(n => n), actual.OrderBy(n => n));
    }
}

public class ProfileNameTests
{
    [Fact]
    public void Saving_with_a_different_case_replaces_the_profile_and_keeps_its_name()
    {
        var settings = new AppSettings { IntervalMs = 100 };
        settings.SaveProfile("Racing");

        settings.IntervalMs = 20;
        string name = settings.SaveProfile("racing");

        Assert.Equal("Racing", name);
        Assert.Single(settings.Profiles);
        Assert.Equal(20, settings.Profiles[0].Data.IntervalMs);
        Assert.Equal("Racing", settings.ActiveProfile);
    }

    [Fact]
    public void Names_are_trimmed()
    {
        var settings = new AppSettings();
        Assert.Equal("Mining", settings.SaveProfile("  Mining  "));
        Assert.NotNull(settings.FindProfile("mining "));
    }

    [Fact]
    public void Empty_name_gets_the_first_free_numbered_name()
    {
        var settings = new AppSettings();
        settings.SaveProfile("Profile 2");
        Assert.Equal("Profile 2", settings.Profiles[0].Name);

        Assert.Equal("Profile 3", settings.SaveProfile(""));
        Assert.Equal(2, settings.Profiles.Count);
    }

    [Fact]
    public void Delete_ignores_case_and_clears_the_active_profile()
    {
        var settings = new AppSettings();
        settings.SaveProfile("Racing");
        Assert.True(settings.DeleteProfile("RACING"));
        Assert.Empty(settings.Profiles);
        Assert.Null(settings.ActiveProfile);
        Assert.False(settings.DeleteProfile("Racing"));
    }

    [Fact]
    public void Delete_of_another_profile_keeps_the_active_one()
    {
        var settings = new AppSettings();
        settings.SaveProfile("A");
        settings.SaveProfile("B");
        settings.DeleteProfile("A");
        Assert.Equal("B", settings.ActiveProfile);
    }

    [Fact]
    public void Next_profile_wraps_and_matches_the_active_name_without_case()
    {
        var settings = new AppSettings();
        settings.SaveProfile("A");
        settings.SaveProfile("B");
        settings.ActiveProfile = "b";

        Assert.Equal("A", settings.NextProfile()!.Name);
        settings.ActiveProfile = null;
        Assert.Equal("A", settings.NextProfile()!.Name);
        Assert.Null(new AppSettings().NextProfile());
    }

    [Fact]
    public void Loading_repairs_duplicate_blank_and_dangling_names()
    {
        string path = Path.Combine(Path.GetTempPath(), "MouseClickerNames-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "ActiveProfile": "gone",
                  "Profiles": [
                    { "Name": "Racing", "Data": { "IntervalMs": 10 } },
                    { "Name": "racing", "Data": { "IntervalMs": 99 } },
                    { "Name": "  ", "Data": { "IntervalMs": 50 } }
                  ]
                }
                """);

            AppSettings settings = AppSettings.Load(path);
            Assert.Equal(new[] { "Racing", "Profile 2" }, settings.Profiles.Select(p => p.Name));
            Assert.Equal(10, settings.Profiles[0].Data.IntervalMs);
            Assert.Null(settings.ActiveProfile);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
