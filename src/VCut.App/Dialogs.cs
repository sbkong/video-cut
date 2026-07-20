using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VCut.App.Locale;
using VCut.App.Settings;

namespace VCut.App;

/// <summary>알림/확인 대화상자의 심각도. 제목 앞 아이콘·색상을 결정한다.</summary>
public enum DialogSeverity { Info, Success, Warning, Error }

/// <summary>
/// 앱 전역 알림·확인 대화상자를 한 곳에서 생성해 폰트·버튼·심각도 표현을 통일한다.
/// 모든 ContentDialog는 가급적 이 헬퍼를 통해 만든다.
/// (버튼 스타일은 Theme.xaml의 ContentDialog 스타일이, 폰트는 여기서 통일.)
/// </summary>
public static class Dialogs
{
    /// <summary>메시지 안내(확인 버튼 하나).</summary>
    public static async Task ShowMessageAsync(XamlRoot root, string title, string message,
        DialogSeverity severity = DialogSeverity.Info)
    {
        var dialog = Create(root, title, severity);
        dialog.Content = new ScrollViewer
        {
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            MaxHeight = 400,
        };
        dialog.CloseButtonText = Loc.Get("dlg.ok");
        await dialog.ShowAsync();
    }

    /// <summary>예/아니오 확인. 확인 시 true.</summary>
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message,
        DialogSeverity severity = DialogSeverity.Info,
        string? primaryText = null, string? closeText = null, bool defaultToPrimary = true)
    {
        var dialog = Create(root, title, severity);
        dialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        dialog.PrimaryButtonText = primaryText ?? Loc.Get("dlg.yes");
        dialog.CloseButtonText = closeText ?? Loc.Get("dlg.no");
        dialog.DefaultButton = defaultToPrimary ? ContentDialogButton.Primary : ContentDialogButton.Close;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>"다시 묻지 않기" 체크박스가 있는 확인.</summary>
    public static async Task<(bool confirmed, bool dontAskAgain)> ConfirmWithOptOutAsync(
        XamlRoot root, string title, string message, DialogSeverity severity = DialogSeverity.Info)
    {
        var checkBox = new CheckBox { Content = Loc.Get("dlg.dont_ask"), Margin = new Thickness(0, 10, 0, 0) };
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(checkBox);

        var dialog = Create(root, title, severity);
        dialog.Content = panel;
        dialog.PrimaryButtonText = Loc.Get("dlg.yes");
        dialog.CloseButtonText = Loc.Get("dlg.no");
        dialog.DefaultButton = ContentDialogButton.Primary;
        var result = await dialog.ShowAsync();
        return (result == ContentDialogResult.Primary, checkBox.IsChecked == true);
    }

    // ThemedContentDialog가 테마·폰트·버튼 스타일을 자동 적용한다.
    private static ContentDialog Create(XamlRoot root, string title, DialogSeverity severity) =>
        new ThemedContentDialog
        {
            XamlRoot = root,
            Title = BuildTitle(title, severity),
        };

    // 심각도 아이콘을 제목 앞에 색상과 함께 표시. Info는 아이콘 없이 텍스트만(기존 동작 유지).
    private static object BuildTitle(string title, DialogSeverity severity)
    {
        if (severity == DialogSeverity.Info) return title;

        // Segoe MDL2 Assets 코드포인트: CheckMark=E73E, Warning=E7BA, ErrorBadge=EA39
        var (codePoint, brushKey) = severity switch
        {
            DialogSeverity.Success => (0xE73E, "BcSuccessTextBrush"),
            DialogSeverity.Warning => (0xE7BA, "BcWarningTextBrush"),
            DialogSeverity.Error   => (0xEA39, "BcDangerTextBrush"),
            _                      => (0xE946, "BcTextBrush"),
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new FontIcon
        {
            Glyph = char.ConvertFromUtf32(codePoint),
            Foreground = (Brush)Application.Current.Resources[brushKey],
            FontSize = (double)Application.Current.Resources["FsTitle"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
}
