using DareuLocal.Core;
using Xunit;

namespace DareuLocal.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Normalize_ClampsThreshold_AndCleansInvalidEntries()
    {
        var settings = new AppSettings
        {
            LowBatteryThreshold = 2,
            AppRules = new List<AppProfileRule>
            {
                new("  C:\\Games\\CS2.EXE ", "AE6V2", 2),
                new("cs2", "AE6V2", 3), // duplicate after normalization
                new("", "AE6V2", 1),
                new("notepad", "AE6V2", 9) // invalid profile
            },
            DefaultProfiles = new Dictionary<string, int>
            {
                ["AE6V2"] = 1,
                ["BAD"] = 7
            }
        };

        settings.Normalize();

        Assert.Equal(5, settings.LowBatteryThreshold);
        Assert.Single(settings.AppRules);
        Assert.Equal("cs2", settings.AppRules[0].ProcessName);
        Assert.Equal(2, settings.AppRules[0].Profile);
        Assert.Single(settings.DefaultProfiles);
        Assert.Equal(1, settings.DefaultProfiles["AE6V2"]);
    }

    [Fact]
    public void Normalize_ClampsHighThreshold()
    {
        var settings = new AppSettings { LowBatteryThreshold = 99 };
        settings.Normalize();
        Assert.Equal(50, settings.LowBatteryThreshold);
    }

    [Fact]
    public void MatchRule_IsCaseInsensitive_AndStripsExeSuffix()
    {
        var settings = new AppSettings
        {
            AppRules = new List<AppProfileRule>
            {
                new("Valorant-Win64-Shipping.exe", "AE6V2", 3),
                new("game.v2.exe", "EK75", 2)
            }
        };

        var matched = settings.MatchRule("VALORANT-WIN64-SHIPPING", "AE6V2");
        Assert.NotNull(matched);
        Assert.Equal(3, matched!.Profile);

        Assert.Null(settings.MatchRule("valorant-win64-shipping.exe", "EK75"));

        var dotted = settings.MatchRule(@"D:\Apps\Game.V2.exe", "EK75");
        Assert.NotNull(dotted);
        Assert.Equal(2, dotted!.Profile);
    }

    [Fact]
    public void ProfileAlias_FormatsAndClearsCleanly()
    {
        var settings = new AppSettings();
        Assert.Equal("Profile 2", settings.GetProfileDisplayName("AE6V2", 2));

        settings.SetProfileAlias("AE6V2", 2, "  FPS 竞技  ");
        Assert.Equal("Profile 2 · FPS 竞技", settings.GetProfileDisplayName("AE6V2", 2));

        settings.SetProfileAlias("AE6V2", 2, "   ");
        Assert.Equal("Profile 2", settings.GetProfileDisplayName("AE6V2", 2));
        Assert.Empty(settings.ProfileAliases);
    }
}
