using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VCut.App.Settings;

namespace VCut.App;

/// <summary>
/// 앱 기본 폰트 적용.
/// 순정 TextBlock은 ContentControlThemeFontFamily 리소스를 참조하지 않고, 그 리소스에 대한
/// {ThemeResource}/{StaticResource} 갱신도 이미 생성된 창에는 반영되지 않는 경우가 있어
/// 리소스 오버라이드만으로는 신뢰할 수 없다. 그래서 각 창/다이얼로그의 루트에 FontFamily를
/// 직접 설정(상속 전파)하는 방식을 기본으로 쓰고, ContentControlThemeFontFamily도 보조로 갱신한다.
/// </summary>
internal static class FontService
{
    public const string JetBrainsMonoSource = "ms-appx:///Assets/Fonts/JetBrainsMono-Regular.ttf#JetBrains Mono";
    public const string SeoulNamsanSource = "ms-appx:///Assets/Fonts/SeoulNamsanM.ttf#SeoulNamsan M";

    public static string ResolveSource(AppSettings s) => s.Font switch
    {
        FontChoice.SeoulNamsan => SeoulNamsanSource,
        FontChoice.System when !string.IsNullOrWhiteSpace(s.SystemFontFamily) => s.SystemFontFamily,
        _ => JetBrainsMonoSource,
    };

    public static FontFamily Resolve(AppSettings s) => new(ResolveSource(s));

    /// <summary>앱 시작 시 1회 호출. 반드시 첫 창이 생성되기 전에 호출해야 한다.</summary>
    public static void ApplyAtStartup(AppSettings s) =>
        Application.Current.Resources["ContentControlThemeFontFamily"] = Resolve(s);

    /// <summary>창의 루트 엘리먼트에 폰트를 직접 적용. Control이 아닌 Grid 등에도
    /// 첨부 속성(Control.FontFamilyProperty)으로 설정해 하위 요소에 상속시킨다.</summary>
    public static void ApplyToRoot(FrameworkElement root, AppSettings s) =>
        root.SetValue(Control.FontFamilyProperty, Resolve(s));
}
