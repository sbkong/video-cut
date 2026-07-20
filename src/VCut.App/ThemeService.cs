using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VCut.App.Settings;
using Windows.UI;

namespace VCut.App;

/// <summary>
/// 앱 테마(다크/라이트)를 런타임에 전환.
/// Application.Current.Resources의 SolidColorBrush 인스턴스를 직접 변경해
/// {StaticResource} 참조가 모두 즉시 갱신되도록 한다.
/// </summary>
public static class ThemeService
{
    // ── 라이트 팔레트 (JetBrains Light with Light Header 모티브) ──────────
    private static readonly (string Key, string Hex)[] LightPalette =
    [
        ("BcAppBrush",           "#F2F3F5"),
        ("BcRailBrush",          "#E4E6EA"),
        ("BcPanelBrush",         "#F7F8FA"),
        ("BcPanelDarkBrush",     "#EAECEF"),
        ("BcCardBrush",          "#FFFFFF"),
        ("BcAccentBrush",        "#2F80D8"),
        ("BcAccentHoverBrush",   "#3E92EA"),
        ("BcTextBrush",          "#1C1D1F"),
        ("BcTextSecondaryBrush", "#6C6E73"),
        ("BcDividerBrush",       "#D0D3DA"),
        ("BcSliderTrackBrush",   "#C9CCD3"),
        ("BcDangerBrush",        "#D24343"),
        ("BcDangerHoverBrush",   "#E15A5A"),
        ("BcDangerPressedBrush", "#B23636"),
        ("BcDangerTextBrush",    "#C0392B"),
        ("BcWarningBrush",       "#E08A00"),
        ("BcWarningTextBrush",   "#C77700"),
        ("BcSuccessBrush",       "#3E9142"),
        ("BcSuccessTextBrush",   "#2E7D32"),
        ("AccentFillColorDefaultBrush",   "#2F80D8"),
        ("AccentFillColorSecondaryBrush", "#3E92EA"),
        ("AccentFillColorTertiaryBrush",  "#2F80D8"),
    ];

    // ── JetBrains Dark (New UI) 팔레트 ──────────────────────────────
    private static readonly (string Key, string Hex)[] DarkPalette =
    [
        ("BcAppBrush",           "#1E1F22"),
        ("BcRailBrush",          "#1A1B1E"),
        ("BcPanelBrush",         "#2B2D30"),
        ("BcPanelDarkBrush",     "#1E1F22"),
        ("BcCardBrush",          "#313335"),
        ("BcAccentBrush",        "#3574F0"),
        ("BcAccentHoverBrush",   "#4A83F0"),
        ("BcTextBrush",          "#BCBEC4"),
        ("BcTextSecondaryBrush", "#7A7E85"),
        ("BcDividerBrush",       "#393B40"),
        ("BcSliderTrackBrush",   "#3A3D42"),
        ("BcDangerBrush",        "#C24545"),
        ("BcDangerHoverBrush",   "#D35C5C"),
        ("BcDangerPressedBrush", "#A33A3A"),
        ("BcDangerTextBrush",    "#E5534B"),
        ("BcWarningBrush",       "#F5A623"),
        ("BcWarningTextBrush",   "#F5A623"),
        ("BcSuccessBrush",       "#499C54"),
        ("BcSuccessTextBrush",   "#5FAD65"),
        ("AccentFillColorDefaultBrush",   "#3574F0"),
        ("AccentFillColorSecondaryBrush", "#4A83F0"),
        ("AccentFillColorTertiaryBrush",  "#3574F0"),
    ];

    /// <summary>팔레트 브러시를 교체하고 지정된 루트 요소의 ElementTheme을 전환.</summary>
    public static void Apply(AppTheme theme, params FrameworkElement?[] roots)
    {
        var palette = theme == AppTheme.Light ? LightPalette : DarkPalette;
        var res = Application.Current.Resources;
        foreach (var (key, hex) in palette)
            if (res[key] is SolidColorBrush brush)
                brush.Color = ParseHex(hex);

        var et = theme == AppTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        foreach (var root in roots)
            if (root is not null) root.RequestedTheme = et;
    }

    /// <summary>ElementTheme만 설정 (브러시 교체 없음).</summary>
    public static void SetElementTheme(FrameworkElement root, AppTheme theme) =>
        root.RequestedTheme = ToElementTheme(theme);

    /// <summary>현재 앱 테마에 해당하는 ElementTheme.
    /// ContentDialog 등 팝업은 창의 ElementTheme를 상속 못 하고 App.RequestedTheme로 떨어지므로 명시적으로 지정해야 한다.</summary>
    public static ElementTheme CurrentElementTheme => ToElementTheme(SettingsStore.Current.Theme);

    private static ElementTheme ToElementTheme(AppTheme theme) =>
        theme == AppTheme.Light ? ElementTheme.Light : ElementTheme.Dark;

    private static Color ParseHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        return Color.FromArgb(
            Convert.ToByte(s[..2], 16),
            Convert.ToByte(s[2..4], 16),
            Convert.ToByte(s[4..6], 16),
            Convert.ToByte(s[6..8], 16));
    }
}
