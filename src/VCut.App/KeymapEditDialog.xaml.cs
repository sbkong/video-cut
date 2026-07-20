using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VCut.App.Keymap;
using VCut.App.Locale;
using VCut.App.Settings;

namespace VCut.App;

/// <summary>단축키 설정 탭에서 '변경' 클릭 시 표시되는 새 단축키 입력 다이얼로그.</summary>
public sealed partial class KeymapEditDialog : ThemedContentDialog
{
    /// <summary>확인 시 선택된 단축키 문자열("Ctrl+O" 등). 단축키 없음이면 "".</summary>
    public string ResultCombo { get; private set; }

    public KeymapEditDialog(string actionName, string currentCombo)
    {
        InitializeComponent();
        ResultCombo = currentCombo;

        Title = Loc.Get("keymap.dialog_title");
        PrimaryButtonText = Loc.Get("dlg.ok");
        CloseButtonText = Loc.Get("dlg.cancel");
        BtnClear.Content = Loc.Get("keymap.clear");
        TxtActionName.Text = actionName;
        CaptureBox.Text = currentCombo;
        CaptureBox.PlaceholderText = Loc.Get("keymap.dialog_prompt");

        Opened += (_, _) => CaptureBox.Focus(FocusState.Programmatic);
    }

    private void OnCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (KeyComboText.IsModifierKey(e.Key)) return;

        ResultCombo = KeyComboText.Format(e.Key, KeyComboText.CurrentModifiers());
        CaptureBox.Text = ResultCombo;
        IsPrimaryButtonEnabled = true;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ResultCombo = "";
        CaptureBox.Text = "";
        IsPrimaryButtonEnabled = true;
    }
}
