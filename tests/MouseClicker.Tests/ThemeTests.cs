using System.Xml.Linq;
using Xunit;

namespace MouseClicker.Tests;

public class ThemeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement LoadAppXaml()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "src", "MouseClicker", "App.axaml"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!, "src", "MouseClicker", "App.axaml")).Root!;
    }

    /// <summary>Brush colours of one theme variant ("Light" or "Dark") in App.axaml, by resource key.</summary>
    private static Dictionary<string, string> Palette(string variant)
    {
        XElement dictionary = LoadAppXaml().Descendants()
            .Where(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(Xaml + "Key") == variant)
            .Single();
        return dictionary.Elements()
            .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!, e => (string?)e.Attribute("Color") ?? e.Value.Trim());
    }

    private static int Lightness(string hex)
    {
        string h = hex.TrimStart('#');
        return Convert.ToInt32(h[..2], 16) + Convert.ToInt32(h[2..4], 16) + Convert.ToInt32(h[4..6], 16);
    }

    [Fact]
    public void Light_and_dark_palettes_define_the_same_keys()
    {
        var light = Palette("Light");
        var dark = Palette("Dark");
        Assert.Equal(light.Keys.OrderBy(k => k), dark.Keys.OrderBy(k => k));
        Assert.NotEqual(light["Page"], dark["Page"]);
    }

    [Fact]
    public void Dark_palette_layers_page_then_card_then_input()
    {
        var dark = Palette("Dark");
        Assert.True(Lightness(dark["Page"]) < Lightness(dark["Surface"]));
        Assert.True(Lightness(dark["Surface"]) < Lightness(dark["TextControlBackground"]));
        Assert.True(Lightness(dark["Surface"]) < Lightness(dark["ComboBoxBackground"]));
        // Text stays off-white, not pure white.
        Assert.NotEqual("#FFFFFF", dark["SystemBaseHighColor"].ToUpperInvariant());
    }

    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x70, "F1")]
    [InlineData(0x75, "F6")]
    [InlineData(0x1B, "Esc")]
    [InlineData(0x20, "Space")]
    [InlineData(0x31, "1")]
    public void Key_names_are_readable(int vk, string expected)
    {
        Assert.Equal(expected, KeyNames.Name(vk));
    }
}
