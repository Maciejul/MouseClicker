using Avalonia;
using Avalonia.Controls;

namespace MouseClicker;

/// <summary>A section title with an optional subtitle above a group of setting cards. Templated in Styles.axaml.</summary>
sealed class SectionHeader : ContentControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<SectionHeader, string>(nameof(Title), "");
    public static readonly StyledProperty<string> SubtitleProperty = AvaloniaProperty.Register<SectionHeader, string>(nameof(Subtitle), "");

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
}

/// <summary>
/// A bordered card with a title, a muted description and a control (switch, box, buttons) on the right. The description wraps
/// to three lines at most. <see cref="Tone"/> ("good", "warn" or empty) colours the description for live status text.
/// </summary>
sealed class SettingCard : ContentControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<SettingCard, string>(nameof(Title), "");
    public static readonly StyledProperty<string> DescriptionProperty = AvaloniaProperty.Register<SettingCard, string>(nameof(Description), "");
    public static readonly StyledProperty<string> ToneProperty = AvaloniaProperty.Register<SettingCard, string>(nameof(Tone), "");

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public string Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}
