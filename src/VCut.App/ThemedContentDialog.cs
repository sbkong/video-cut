using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VCut.App.Settings;

namespace VCut.App;

/// <summary>
/// 앱의 모든 ContentDialog 공통 기반. 현재 앱 테마·폰트·버튼 스타일을 한 곳에서 강제한다.
/// (ContentDialog 등 팝업은 창의 ElementTheme를 상속하지 못하고 App.RequestedTheme로
///  떨어지므로, 여기서 명시적으로 지정하지 않으면 라이트 앱에서 다이얼로그만 다크로 뜬다.)
/// 서브클래스는 이 타입을 상속하기만 하면 자동으로 통일된 테마를 얻는다.
/// </summary>
public class ThemedContentDialog : ContentDialog
{
    public ThemedContentDialog()
    {
        RequestedTheme = ThemeService.CurrentElementTheme;
        FontFamily = FontService.Resolve(SettingsStore.Current);

        // 서브클래스에는 암시적 ContentDialog 스타일이 적용되지 않으므로 버튼 스타일을 직접 지정.
        var res = Application.Current.Resources;
        PrimaryButtonStyle   = (Style)res["DialogPrimaryButtonStyle"];
        SecondaryButtonStyle = (Style)res["DialogSecondaryButtonStyle"];
        CloseButtonStyle     = (Style)res["DialogSecondaryButtonStyle"];
    }
}
