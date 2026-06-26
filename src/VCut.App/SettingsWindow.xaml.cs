using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VCut.App.Settings;
using VCut.Core.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VCut.App;

public sealed partial class SettingsWindow : Window
{
    /// <summary>편집 중인 설정 사본(저장 시 영구화).</summary>
    public AppSettings S { get; private set; }

    /// <summary>저장되었는지 여부. 메인 윈도우가 닫힌 뒤 재적용 판단에 사용.</summary>
    public bool Saved { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        Title = "v-cut — 환경 설정";
        S = SettingsStore.Current.Clone();
        LoadNonBound();

        if (AppWindow is { } aw)
            aw.Resize(new Windows.Graphics.SizeInt32(560, 620));
    }

    /// <summary>x:Bind로 처리되지 않는 항목(폴더/콤보)을 사본 값으로 초기화.</summary>
    private void LoadNonBound()
    {
        SameFolderRadio.IsChecked = S.SaveFolderMode == SaveFolderMode.SameAsSource;
        CustomFolderRadio.IsChecked = S.SaveFolderMode == SaveFolderMode.Custom;
        SaveFolderBox.Text = S.SaveFolder;
        TempFolderBox.Text = S.TempFolder;
        LanguageCombo.SelectedIndex = S.Language switch { "en" => 1, "ja" => 2, _ => 0 };
        HwCombo.SelectedIndex = (int)S.DefaultHardwareAccel;
    }

    private void CollectNonBound()
    {
        S.SaveFolderMode = CustomFolderRadio.IsChecked == true
            ? SaveFolderMode.Custom : SaveFolderMode.SameAsSource;
        S.SaveFolder = SaveFolderBox.Text.Trim();
        S.TempFolder = TempFolderBox.Text.Trim();
        S.Language = LanguageCombo.SelectedIndex switch { 1 => "en", 2 => "ja", _ => "ko" };
        S.DefaultHardwareAccel = (HardwareAccel)Math.Max(0, HwCombo.SelectedIndex);
    }

    private async void OnBrowseSaveFolder(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            SaveFolderBox.Text = path;
            CustomFolderRadio.IsChecked = true;
        }
    }

    private async void OnBrowseTempFolder(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null) TempFolderBox.Text = path;
    }

    private async System.Threading.Tasks.Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        CollectNonBound();
        SettingsStore.Save(S);
        Saved = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnReset(object sender, RoutedEventArgs e)
    {
        S = new AppSettings();
        LoadNonBound();
        // x:Bind OneTime이 아니므로 재바인딩을 위해 Pivot을 다시 그릴 필요 없이
        // 사본 교체 후 컨트롤을 재초기화. 체크박스는 다음 줄에서 직접 반영.
        Bindings.Update();
    }
}
