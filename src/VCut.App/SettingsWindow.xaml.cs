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
        NavGeneral.IsChecked = true;

        // 현재 적용 중인 테마를 이 창에도 적용
        if (Content is FrameworkElement root)
            ThemeService.SetElementTheme(root, SettingsStore.Current.Theme);

        if (AppWindow is { } aw)
            aw.Resize(new Windows.Graphics.SizeInt32(560, 640));
    }

    /// <summary>x:Bind로 처리되지 않는 항목(폴더/콤보/테마)을 사본 값으로 초기화.</summary>
    private void LoadNonBound()
    {
        SameFolderRadio.IsChecked = S.SaveFolderMode == SaveFolderMode.SameAsSource;
        CustomFolderRadio.IsChecked = S.SaveFolderMode == SaveFolderMode.Custom;
        SaveFolderBox.Text = S.SaveFolder;
        CaptureSameFolderRadio.IsChecked = S.CaptureFolderMode == SaveFolderMode.SameAsSource;
        CaptureCustomFolderRadio.IsChecked = S.CaptureFolderMode == SaveFolderMode.Custom;
        CaptureFolderBox.Text = S.CaptureFolder;
        CaptureOpenAskRadio.IsChecked    = S.CaptureOpenFolderMode == CaptureOpenFolderMode.AlwaysAsk;
        CaptureOpenAlwaysRadio.IsChecked = S.CaptureOpenFolderMode == CaptureOpenFolderMode.AlwaysOpen;
        CaptureOpenNeverRadio.IsChecked  = S.CaptureOpenFolderMode == CaptureOpenFolderMode.NeverOpen;
        TempFolderBox.Text = S.TempFolder;
        LanguageCombo.SelectedIndex = S.Language switch { "en" => 1, "ja" => 2, _ => 0 };
        HwCombo.SelectedIndex = (int)S.DefaultHardwareAccel;
        ThemeDarkRadio.IsChecked  = S.Theme == AppTheme.Dark;
        ThemeLightRadio.IsChecked = S.Theme == AppTheme.Light;
    }

    private void CollectNonBound()
    {
        S.SaveFolderMode = CustomFolderRadio.IsChecked == true
            ? SaveFolderMode.Custom : SaveFolderMode.SameAsSource;
        S.SaveFolder = SaveFolderBox.Text.Trim();
        S.CaptureFolderMode = CaptureCustomFolderRadio.IsChecked == true
            ? SaveFolderMode.Custom : SaveFolderMode.SameAsSource;
        S.CaptureFolder = CaptureFolderBox.Text.Trim();
        S.CaptureOpenFolderMode = CaptureOpenAlwaysRadio.IsChecked == true
            ? CaptureOpenFolderMode.AlwaysOpen
            : CaptureOpenNeverRadio.IsChecked == true
                ? CaptureOpenFolderMode.NeverOpen
                : CaptureOpenFolderMode.AlwaysAsk;
        S.TempFolder = TempFolderBox.Text.Trim();
        S.Language = LanguageCombo.SelectedIndex switch { 1 => "en", 2 => "ja", _ => "ko" };
        S.DefaultHardwareAccel = (HardwareAccel)Math.Max(0, HwCombo.SelectedIndex);
        S.Theme = ThemeLightRadio.IsChecked == true ? AppTheme.Light : AppTheme.Dark;
    }

    // ────────────────────────────────────────────────────────────────
    // 왼쪽 네비게이션
    // ────────────────────────────────────────────────────────────────
    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || !int.TryParse(rb.Tag?.ToString(), out int idx)) return;
        PanelGeneral.Visibility  = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelPlayback.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelFiles.Visibility    = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelLanguage.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelFastMode.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelTheme.Visibility    = idx == 5 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ────────────────────────────────────────────────────────────────
    // 테마 라이브 미리보기
    // ────────────────────────────────────────────────────────────────
    private void OnThemeRadioChecked(object sender, RoutedEventArgs e)
    {
        var theme = ThemeLightRadio.IsChecked == true ? AppTheme.Light : AppTheme.Dark;
        ThemeService.Apply(theme,
            Content as FrameworkElement,
            App.MainWindow?.Content as FrameworkElement);
    }

    // ────────────────────────────────────────────────────────────────
    // 파일 찾아보기
    // ────────────────────────────────────────────────────────────────
    private async void OnBrowseSaveFolder(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            SaveFolderBox.Text = path;
            CustomFolderRadio.IsChecked = true;
        }
    }

    private async void OnBrowseCaptureFolder(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            CaptureFolderBox.Text = path;
            CaptureCustomFolderRadio.IsChecked = true;
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

    // ────────────────────────────────────────────────────────────────
    // 저장 / 취소 / 초기화
    // ────────────────────────────────────────────────────────────────
    private void OnSave(object sender, RoutedEventArgs e)
    {
        CollectNonBound();
        SettingsStore.Save(S);
        Saved = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        // 라이브 미리보기로 바뀐 테마를 저장된 상태로 되돌림
        ThemeService.Apply(SettingsStore.Current.Theme,
            Content as FrameworkElement,
            App.MainWindow?.Content as FrameworkElement);
        Close();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        S = new AppSettings();
        LoadNonBound(); // ThemeDarkRadio.IsChecked = true → OnThemeRadioChecked 발동
        Bindings.Update();
    }
}
