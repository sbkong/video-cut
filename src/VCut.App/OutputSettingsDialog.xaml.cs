using Microsoft.UI.Xaml.Controls;
using VCut.App.Locale;
using VCut.App.ViewModels;

namespace VCut.App;

/// <summary>[시작] 시 표시되는 출력 설정 다이얼로그. docx '출력 설정'(고속/변환 + 옵션).</summary>
public sealed partial class OutputSettingsDialog : ThemedContentDialog
{
    public MainViewModel VM { get; }

    public OutputSettingsDialog(MainViewModel vm)
    {
        VM = vm;
        InitializeComponent();
        ApplyLocale();
    }

    private void ApplyLocale()
    {
        Title            = Loc.Get("out.title");
        PrimaryButtonText = Loc.Get("btn.start");
        CloseButtonText  = Loc.Get("dlg.cancel");

        TxtModeHeader.Text        = Loc.Get("out.mode");
        TglFastMode.OnContent     = Loc.Get("out.fast_mode");
        TglFastMode.OffContent    = Loc.Get("out.convert_mode");
        CboFormat.Header          = Loc.Get("out.format");
        SldQuality.Header         = Loc.Get("out.quality");
        SldSpeed.Header           = Loc.Get("out.speed");
        TxtOptionsHeader.Text     = Loc.Get("out.options");
        CbMerge.Content           = Loc.Get("out.merge");
        CbSavePlayback.Content    = Loc.Get("out.save_playback");
        CbExtractAudio.Content    = Loc.Get("out.extract_audio");
        CbRemoveAudio.Content     = Loc.Get("out.remove_audio");
    }
}
