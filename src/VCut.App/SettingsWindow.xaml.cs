using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using VCut.App.Keymap;
using VCut.App.Locale;
using VCut.App.Settings;
using VCut.Core.Models;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace VCut.App;

public sealed partial class SettingsWindow : WindowBase
{
    /// <summary>편집 중인 설정 사본(저장 시 영구화).</summary>
    public AppSettings S { get; private set; }

    /// <summary>저장되었는지 여부. 메인 윈도우가 닫힌 뒤 재적용 판단에 사용.</summary>
    public bool Saved { get; private set; }

    /// <summary>'단축키' 탭 목록(액션 이름 + 현재 단축키).</summary>
    public ObservableCollection<KeymapRowVm> KeymapRows { get; } = [];

    public SettingsWindow()
    {
        InitializeComponent();
        Title = Loc.Get("settings.win_title");
        S = SettingsStore.Current.Clone();
        LoadNonBound();
        ApplyLocale();
        NavGeneral.IsChecked = true;

        // 현재 적용 중인 테마/폰트를 이 창에도 적용
        if (Content is FrameworkElement root)
        {
            ThemeService.SetElementTheme(root, SettingsStore.Current.Theme);
            FontService.ApplyToRoot(root, SettingsStore.Current);
        }

        if (AppWindow is { } aw)
            aw.Resize(new Windows.Graphics.SizeInt32(560, 640));
    }

    private void ApplyLocale()
    {
        SettingsHeader.Text = Loc.Get("settings.title");

        // Nav items
        NavGeneral.Content  = Loc.Get("nav.general");
        NavPlayback.Content = Loc.Get("nav.playback");
        NavFiles.Content    = Loc.Get("nav.files");
        NavLanguage.Content = Loc.Get("nav.language");
        NavFastMode.Content = Loc.Get("nav.fast_mode");
        NavTheme.Content    = Loc.Get("nav.theme");
        NavFont.Content     = Loc.Get("nav.font");
        NavKeymap.Content   = Loc.Get("nav.keymap");

        // Panel: general
        CbWarnUnseekable.Content = Loc.Get("gen.warn_unseekable");
        CbWarnFileExists.Content = Loc.Get("gen.warn_file_exists");
        CbShowSaveMsg.Content    = Loc.Get("gen.show_save_msg");
        CbCreateLog.Content      = Loc.Get("gen.create_log");
        CbMoovFront.Content      = Loc.Get("gen.moov_front");
        CbKeepTime.Content       = Loc.Get("gen.keep_time");
        CbShowTips.Content       = Loc.Get("gen.show_tips");
        CbAutoCursor.Content     = Loc.Get("gen.auto_cursor");

        // Panel: playback
        CbWarnUnplayable.Content = Loc.Get("play.warn_unplayable");
        CbDeinterlace.Content    = Loc.Get("play.deinterlace");
        CbHwRenderer.Content     = Loc.Get("play.hw_renderer");
        CbHwDecoder.Content      = Loc.Get("play.hw_decoder");
        NumSeekSeconds.Header    = Loc.Get("play.seek_seconds");
        TxtPlayNote.Text         = Loc.Get("play.note");

        // Panel: files
        TxtSaveFolderHeader.Text        = Loc.Get("files.save_folder");
        SameFolderRadio.Content         = Loc.Get("files.same_location");
        CustomFolderRadio.Content       = Loc.Get("files.custom_folder");
        BtnBrowseSave.Content           = Loc.Get("files.browse");
        SaveFolderBox.PlaceholderText   = Loc.Get("files.save_folder_ph");
        TxtOpenAfterHeader.Text         = Loc.Get("files.open_after");
        OutputOpenAskRadio.Content      = Loc.Get("files.always_ask");
        OutputOpenAlwaysRadio.Content   = Loc.Get("files.always_open");
        OutputOpenNeverRadio.Content    = Loc.Get("files.never_open");
        TxtCaptureFolderHeader.Text     = Loc.Get("files.capture_folder");
        CaptureSameFolderRadio.Content  = Loc.Get("files.same_location");
        CaptureCustomFolderRadio.Content = Loc.Get("files.custom_folder");
        BtnBrowseCapture.Content        = Loc.Get("files.browse");
        CaptureFolderBox.PlaceholderText = Loc.Get("files.capture_ph");
        TxtCaptureOpenAfterHeader.Text  = Loc.Get("files.capture_after");
        CaptureOpenAskRadio.Content     = Loc.Get("files.always_ask");
        CaptureOpenAlwaysRadio.Content  = Loc.Get("files.always_open");
        CaptureOpenNeverRadio.Content   = Loc.Get("files.never_open");
        TxtTempFolderHeader.Text        = Loc.Get("files.temp_folder");
        BtnBrowseTemp.Content           = Loc.Get("files.browse");
        TempFolderBox.PlaceholderText   = Loc.Get("files.temp_ph");

        // Panel: language
        LanguageCombo.Header = Loc.Get("lang.title");
        TxtLangNote.Text     = Loc.Get("lang.note");

        // Panel: fast mode
        CbWarnFastUnavail.Content = Loc.Get("fast.warn_unavail");
        CbAlwaysKF.Content        = Loc.Get("fast.always_kf");
        CbWarnShift.Content       = Loc.Get("fast.warn_shift");
        CbRelaxMerge.Content      = Loc.Get("fast.relax_merge");
        TxtHwAccelHeader.Text     = Loc.Get("fast.hw_accel");
        if (HwCombo.Items.Count > 0 && HwCombo.Items[0] is ComboBoxItem hwNone)
            hwNone.Content = Loc.Get("fast.hw_none");

        // Panel: theme
        TxtThemeHeader.Text = Loc.Get("theme.title");
        if (ThemeDarkLabel.Inlines[0] is Run runDark)
            runDark.Text = Loc.Get("theme.dark") + "  ";
        if (ThemeLightLabel.Inlines[0] is Run runLight)
            runLight.Text = Loc.Get("theme.light") + "  ";
        TxtThemeNote.Text = Loc.Get("theme.note");

        // Panel: font
        TxtFontHeader.Text         = Loc.Get("font.title");
        FontSystemRadio.Content    = Loc.Get("font.system");
        SystemFontCombo.Header     = Loc.Get("font.system_header");
        TxtFontNote.Text           = Loc.Get("font.note");

        // Panel: keymap
        KeymapSearchBox.PlaceholderText = Loc.Get("keymap.search_ph");
        TxtKeymapNote.Text              = Loc.Get("keymap.note");

        // Bottom bar
        BtnRestart.Content        = Loc.Get("settings.btn.restart");
        BtnResetAll.Content       = Loc.Get("settings.btn.reset_all");
        BtnReset.Content          = Loc.Get("settings.btn.reset");
        BtnCancelSettings.Content = Loc.Get("settings.btn.cancel");
        BtnSaveSettings.Content   = Loc.Get("settings.btn.save");
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
        CaptureOpenAskRadio.IsChecked    = S.CaptureOpenFolderMode == OpenFolderMode.AlwaysAsk;
        CaptureOpenAlwaysRadio.IsChecked = S.CaptureOpenFolderMode == OpenFolderMode.AlwaysOpen;
        CaptureOpenNeverRadio.IsChecked  = S.CaptureOpenFolderMode == OpenFolderMode.NeverOpen;
        OutputOpenAskRadio.IsChecked    = S.OutputOpenFolderMode == OpenFolderMode.AlwaysAsk;
        OutputOpenAlwaysRadio.IsChecked = S.OutputOpenFolderMode == OpenFolderMode.AlwaysOpen;
        OutputOpenNeverRadio.IsChecked  = S.OutputOpenFolderMode == OpenFolderMode.NeverOpen;
        TempFolderBox.Text = S.TempFolder;
        LanguageCombo.SelectedIndex = S.Language switch { "en" => 1, "ja" => 2, "zh" => 3, _ => 0 };
        HwCombo.SelectedIndex = (int)S.DefaultHardwareAccel;
        ThemeDarkRadio.IsChecked  = S.Theme == AppTheme.Dark;
        ThemeLightRadio.IsChecked = S.Theme == AppTheme.Light;

        var fonts = SystemFonts.GetInstalledFamilyNames();
        SystemFontCombo.ItemsSource = fonts;
        SystemFontCombo.SelectedItem = fonts.FirstOrDefault(f =>
            string.Equals(f, S.SystemFontFamily, StringComparison.OrdinalIgnoreCase));
        FontJetBrainsRadio.IsChecked   = S.Font == FontChoice.JetBrainsMono;
        FontSeoulNamsanRadio.IsChecked = S.Font == FontChoice.SeoulNamsan;
        FontSystemRadio.IsChecked      = S.Font == FontChoice.System;
        SystemFontCombo.IsEnabled      = S.Font == FontChoice.System;

        BuildKeymapRows();
        UpdateRestartButton();
    }

    // ────────────────────────────────────────────────────────────────
    // 단축키
    // ────────────────────────────────────────────────────────────────
    private void BuildKeymapRows()
    {
        KeymapRows.Clear();
        foreach (var def in KeymapActions.All)
            KeymapRows.Add(new KeymapRowVm(def.Id, Loc.Get(def.LocKey), Loc.Get(def.CategoryLocKey)));
        RefreshKeymapShortcuts();
        ApplyKeymapFilter();
    }

    private void RefreshKeymapShortcuts()
    {
        foreach (var row in KeymapRows)
        {
            var combo = KeymapActions.ResolveCombo(S, row.Id);
            row.Shortcut = string.IsNullOrEmpty(combo) ? Loc.Get("keymap.unassigned") : combo;
        }
    }

    /// <summary>검색어로 필터링 후 카테고리별로 그룹화하여 목록에 반영.</summary>
    private void ApplyKeymapFilter()
    {
        var filter = KeymapSearchBox.Text?.Trim() ?? "";
        IEnumerable<KeymapRowVm> rows = KeymapRows;
        if (!string.IsNullOrEmpty(filter))
            rows = rows.Where(r => r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

        var grouped = rows.GroupBy(r => r.Category).ToList();
        KeymapList.ItemsSource = new CollectionViewSource { IsSourceGrouped = true, Source = grouped }.View;
    }

    private void OnKeymapSearchChanged(object sender, TextChangedEventArgs e) => ApplyKeymapFilter();

    private void OnKeymapListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        if (KeymapList.SelectedItem is not KeymapRowVm row) return;
        e.Handled = true;
        _ = OpenKeymapEditorAsync(row.Id);
    }

    private void OnKeymapRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string actionId }) _ = OpenKeymapEditorAsync(actionId);
    }

    private void OnKeymapContextChange(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string actionId }) _ = OpenKeymapEditorAsync(actionId);
    }

    private void OnKeymapContextReset(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string actionId }) return;
        S.Keymap.Remove(actionId);
        RefreshKeymapShortcuts();
    }

    private async Task OpenKeymapEditorAsync(string actionId)
    {
        var def = KeymapActions.All.FirstOrDefault(a => a.Id == actionId);
        if (def is null) return;

        var current = KeymapActions.ResolveCombo(S, actionId);
        var dialog = new KeymapEditDialog(Loc.Get(def.LocKey), current) { XamlRoot = Content.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var newCombo = dialog.ResultCombo;
        var conflictId = KeymapActions.FindConflict(S, newCombo, actionId);
        if (conflictId is not null)
        {
            var conflictDef = KeymapActions.All.First(a => a.Id == conflictId);
            var confirm = new ContentDialog
            {
                Title = Loc.Get("keymap.conflict_title"),
                Content = Loc.Format("keymap.conflict_msg", Loc.Get(conflictDef.LocKey)),
                PrimaryButtonText = Loc.Get("dlg.yes"),
                CloseButtonText = Loc.Get("dlg.no"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
                FontFamily = FontService.Resolve(SettingsStore.Current),
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            S.Keymap[conflictId] = "";
        }

        S.Keymap[actionId] = newCombo;
        RefreshKeymapShortcuts();
    }

    // ────────────────────────────────────────────────────────────────
    // 재시작이 필요한 설정(언어/폰트) 변경 감지
    // ────────────────────────────────────────────────────────────────
    private void OnRestartSensitiveChanged(object sender, SelectionChangedEventArgs e) => UpdateRestartButton();

    private void UpdateRestartButton()
    {
        var newLanguage = LanguageCombo.SelectedIndex switch { 1 => "en", 2 => "ja", 3 => "zh", _ => "ko" };
        var newFont = FontSeoulNamsanRadio.IsChecked == true
            ? FontChoice.SeoulNamsan
            : FontSystemRadio.IsChecked == true
                ? FontChoice.System
                : FontChoice.JetBrainsMono;
        var newSystemFont = SystemFontCombo.SelectedItem as string ?? S.SystemFontFamily;

        var current = SettingsStore.Current;
        var needsRestart =
            newLanguage != current.Language ||
            newFont != current.Font ||
            (newFont == FontChoice.System &&
             !string.Equals(newSystemFont, current.SystemFontFamily, StringComparison.OrdinalIgnoreCase));

        BtnRestart.Visibility = needsRestart ? Visibility.Visible : Visibility.Collapsed;
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
            ? OpenFolderMode.AlwaysOpen
            : CaptureOpenNeverRadio.IsChecked == true
                ? OpenFolderMode.NeverOpen
                : OpenFolderMode.AlwaysAsk;
        S.OutputOpenFolderMode = OutputOpenAlwaysRadio.IsChecked == true
            ? OpenFolderMode.AlwaysOpen
            : OutputOpenNeverRadio.IsChecked == true
                ? OpenFolderMode.NeverOpen
                : OpenFolderMode.AlwaysAsk;
        S.TempFolder = TempFolderBox.Text.Trim();
        S.Language = LanguageCombo.SelectedIndex switch { 1 => "en", 2 => "ja", 3 => "zh", _ => "ko" };
        S.DefaultHardwareAccel = (HardwareAccel)Math.Max(0, HwCombo.SelectedIndex);
        S.Theme = ThemeLightRadio.IsChecked == true ? AppTheme.Light : AppTheme.Dark;
        S.Font = FontSeoulNamsanRadio.IsChecked == true
            ? FontChoice.SeoulNamsan
            : FontSystemRadio.IsChecked == true
                ? FontChoice.System
                : FontChoice.JetBrainsMono;
        if (SystemFontCombo.SelectedItem is string selectedFont) S.SystemFontFamily = selectedFont;
    }

    // ────────────────────────────────────────────────────────────────
    // 왼쪽 네비게이션
    // ────────────────────────────────────────────────────────────────
    /// <summary>현재 선택된 네비게이션 탭 인덱스(0=일반 … 7=단축키). [초기화] 버튼이 참조.</summary>
    private int _navIndex;

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || !int.TryParse(rb.Tag?.ToString(), out int idx)) return;
        _navIndex = idx;
        PanelGeneral.Visibility  = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelPlayback.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelFiles.Visibility    = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelLanguage.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelFastMode.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelTheme.Visibility    = idx == 5 ? Visibility.Visible : Visibility.Collapsed;
        PanelFont.Visibility     = idx == 6 ? Visibility.Visible : Visibility.Collapsed;
        PanelKeymap.Visibility   = idx == 7 ? Visibility.Visible : Visibility.Collapsed;
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
    // 폰트
    // ────────────────────────────────────────────────────────────────
    private void OnFontRadioChecked(object sender, RoutedEventArgs e)
    {
        SystemFontCombo.IsEnabled = FontSystemRadio.IsChecked == true;
        UpdateRestartButton();
    }

    private void OnSystemFontComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SystemFontCombo.SelectedItem is string) FontSystemRadio.IsChecked = true;
        UpdateRestartButton();
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

    private void OnRestartNow(object sender, RoutedEventArgs e)
    {
        CollectNonBound();
        SettingsStore.Save(S);
        Saved = true;
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); }
        catch { /* 재시작 실패 시 무시 */ }
        Close();
        Application.Current.Exit();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        // 라이브 미리보기로 바뀐 테마를 저장된 상태로 되돌림
        ThemeService.Apply(SettingsStore.Current.Theme,
            Content as FrameworkElement,
            App.MainWindow?.Content as FrameworkElement);
        Close();
    }

    /// <summary>[초기화] — 현재 탭에 속한 값만 기본값으로 되돌림.</summary>
    private void OnReset(object sender, RoutedEventArgs e)
    {
        var def = new AppSettings();
        switch (_navIndex)
        {
            case 0: // 일반
                S.WarnUnseekable = def.WarnUnseekable;
                S.WarnFileExists = def.WarnFileExists;
                S.ShowProjectSaveMessage = def.ShowProjectSaveMessage;
                S.CreateLogFile = def.CreateLogFile;
                S.MoovAtFront = def.MoovAtFront;
                S.KeepCreationTime = def.KeepCreationTime;
                S.ShowTips = def.ShowTips;
                S.AutoAdvanceCursor = def.AutoAdvanceCursor;
                break;
            case 1: // 재생
                S.WarnUnplayable = def.WarnUnplayable;
                S.DeinterlaceOnPlay = def.DeinterlaceOnPlay;
                S.HardwareRenderer = def.HardwareRenderer;
                S.UseHardwareDecoder = def.UseHardwareDecoder;
                S.SeekSeconds = def.SeekSeconds;
                break;
            case 2: // 파일
                S.SaveFolderMode = def.SaveFolderMode;
                S.SaveFolder = def.SaveFolder;
                S.CaptureFolderMode = def.CaptureFolderMode;
                S.CaptureFolder = def.CaptureFolder;
                S.CaptureOpenFolderMode = def.CaptureOpenFolderMode;
                S.OutputOpenFolderMode = def.OutputOpenFolderMode;
                S.TempFolder = def.TempFolder;
                break;
            case 3: // 언어
                S.Language = def.Language;
                break;
            case 4: // 고속 모드
                S.WarnFastUnavailable = def.WarnFastUnavailable;
                S.AlwaysKeyframe = def.AlwaysKeyframe;
                S.WarnStartShift = def.WarnStartShift;
                S.RelaxFastMerge = def.RelaxFastMerge;
                S.DefaultHardwareAccel = def.DefaultHardwareAccel;
                break;
            case 5: // 테마
                S.Theme = def.Theme;
                break;
            case 6: // 폰트
                S.Font = def.Font;
                S.SystemFontFamily = def.SystemFontFamily;
                break;
            case 7: // 단축키
                S.Keymap.Clear();
                break;
        }
        LoadNonBound(); // ThemeDarkRadio.IsChecked = true → OnThemeRadioChecked 발동
        Bindings.Update();
    }

    /// <summary>[전체 초기화] — 모든 탭의 값을 기본값으로 되돌림.</summary>
    private void OnResetAll(object sender, RoutedEventArgs e)
    {
        S = new AppSettings();
        LoadNonBound(); // ThemeDarkRadio.IsChecked = true → OnThemeRadioChecked 발동
        Bindings.Update();
    }
}
