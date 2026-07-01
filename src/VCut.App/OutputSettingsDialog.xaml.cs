using Microsoft.UI.Xaml.Controls;
using VCut.App.ViewModels;

namespace VCut.App;

/// <summary>[시작] 시 표시되는 출력 설정 다이얼로그. docx '출력 설정'(고속/변환 + 옵션).</summary>
public sealed partial class OutputSettingsDialog : ContentDialog
{
    public MainViewModel VM { get; }

    public OutputSettingsDialog(MainViewModel vm)
    {
        VM = vm;
        InitializeComponent();
        FontFamily = FontService.Resolve(Settings.SettingsStore.Current);
    }
}
