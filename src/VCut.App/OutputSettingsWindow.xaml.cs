using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.IO;
using VCut.App.Locale;
using VCut.App.Settings;
using VCut.App.ViewModels;
using VCut.Core.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;
using VirtualKey = Windows.System.VirtualKey;

namespace VCut.App;

public sealed partial class OutputSettingsWindow : WindowBase
{
    public MainViewModel VM { get; }

    private readonly TaskCompletionSource<bool> _tcs = new();

    public Task<bool> WaitAsync() => _tcs.Task;

    public OutputSettingsWindow(MainViewModel vm)
    {
        VM = vm;
        InitializeComponent();

        Title = $"v-cut — {Loc.Get("out.title")}";

        // 실제 출력 경로를 항상 표시 (원본 폴더도 플레이스홀더 대신 경로로 표시)
        var s = SettingsStore.Current;
        var resolvedPath = s.SaveFolderMode == SaveFolderMode.Custom && !string.IsNullOrWhiteSpace(s.SaveFolder)
            ? s.SaveFolder
            : vm.SourcePath is not null ? Path.GetDirectoryName(vm.SourcePath) : null;
        if (resolvedPath is not null)
            FolderBox.Text = resolvedPath;

        ApplyLocale();

        if (Content is FrameworkElement root)
        {
            ThemeService.SetElementTheme(root, s.Theme);
            FontService.ApplyToRoot(root, s);
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 640));

        // 창을 열 때마다 "포함 모드"로 초기화. 메인 목록에서 선택(IsSelected)해둔 항목이 있으면
        // 그것만 기본으로 체크, 없으면(아무것도 선택 안 했으면) 전체를 기본으로 체크.
        bool anySelected = vm.Segments.Any(s => s.IsSelected);
        foreach (var seg in vm.Segments) seg.IsPicked = anySelected ? seg.IsSelected : true;
        vm.RemoveSelectedEnabled = false;
        vm.IsFastMode = true;

        // CanKeepOriginalSize는 IsPicked 초기화 이후 값을 반영해야 하므로 바인딩을 강제 갱신.
        Bindings.Update();
        // 여러 원본 파일이 섞여 있어 "원본 유지"가 숨겨졌는데 기본 선택이 그 항목이면 "직접 지정"으로 이동.
        if (!vm.CanKeepOriginalSize && vm.SizeModeIndex == 0) vm.SizeModeIndex = 3;

        ApplyDefaultsFromSource();
        UpdateFileNamePlaceholder();
        TglFastMode.Toggled += (_, _) => UpdateFileNamePlaceholder();
        CboFormat.SelectionChanged += (_, _) => UpdateFileNamePlaceholder();
        CbMerge.Checked += (_, _) => UpdateFileNamePlaceholder();
        CbMerge.Unchecked += (_, _) => UpdateFileNamePlaceholder();
        RbPickInclude.Checked += (_, _) => UpdateFileNamePlaceholder();
        RbPickRemove.Checked += (_, _) => UpdateFileNamePlaceholder();

        Closed += (_, _) => _tcs.TrySetResult(false);

        // Root.Loaded는 콘텐츠 트리가 완전히 로드된 뒤 정확히 한 번 발생하므로,
        // 이 시점에 초기 포커스를 지정하는 게 표준적인 방법(Window.Activated보다 안정적).
        Root.Loaded += OnRootLoaded;

        // WinUI 네이티브 포커스 매니저가 포커스가 빌 때마다 FolderBox로 폴백을 "시도"하는 버그가
        // 있고, FolderBox(TextBox)는 실제로 포커스를 받으면 캐럿을 보이게 하려고
        // ScrollViewer.BringIntoViewOnFocusChange를 무시하고 스크롤을 요청한다. 포커스 전환이
        // "완료되기 전" 단계인 GettingFocus(취소/리다이렉트 가능한 프리뷰 이벤트)에서 FolderBox로의
        // 전환 자체를 막아, FolderBox가 실제로 포커스를 받는 일 자체가 없게 한다.
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnAnyPointerPressed), true);
        Root.GotFocus += OnAnyGotFocus;
        Root.GettingFocus += OnAnyGettingFocus;
    }

    private FrameworkElement? _lastRealFocus;
    private DependencyObject? _lastPointerPressTarget;

    private void OnAnyPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _lastPointerPressTarget = e.OriginalSource as DependencyObject;
    }

    // 포커스 전환이 완료되기 전에 가로챈다 — FolderBox가 실제로 클릭된 게 아니라면 전환 자체를
    // 직전 실제 포커스로 리다이렉트해서, FolderBox는 GotFocus를 아예 받지 않게 한다.
    private void OnAnyGettingFocus(UIElement sender, GettingFocusEventArgs e)
    {
        if (!ReferenceEquals(e.NewFocusedElement, FolderBox)) return;
        if (IsSameOrDescendant(FolderBox, _lastPointerPressTarget)) return;

        if (_lastRealFocus is not null)
            e.TrySetNewFocusedElement(_lastRealFocus);
        else
            e.TryCancel();
    }

    private void OnAnyGotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement el)
            _lastRealFocus = el;
    }

    private static bool IsSameOrDescendant(DependencyObject ancestor, DependencyObject? node)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= OnRootLoaded;
        _lastRealFocus = BtnStart;
        BtnStart.Focus(FocusState.Programmatic);
    }

    private static readonly int[] VideoBitratePresets = [1000, 2500, 5000, 8000, 15000, 25000, 50000];
    private static readonly int[] AudioBitratePresets = [96, 128, 192, 256, 320];

    private static int ClosestIndex(int[] presets, double value)
    {
        int best = 0;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < presets.Length; i++)
        {
            var diff = Math.Abs(presets[i] - value);
            if (diff < bestDiff) { bestDiff = diff; best = i; }
        }
        return best;
    }

    /// <summary>변환 모드 상세 설정의 초기값을 원본 파일(선택된 것 중 첫 번째, 없으면 목록 첫 항목)의
    /// 실제 코덱/비트레이트/해상도로 채운다. 비트레이트는 가장 가까운 프리셋으로 스냅.
    /// 프레임레이트는 항상 "원본 유지"부터 시작(강제 재인코딩 방지).</summary>
    private void ApplyDefaultsFromSource()
    {
        CboFrameRatePreset.SelectedIndex = 0; // 원본 유지

        var picked = VM.Segments.Where(s => s.IsPicked).ToList();
        var basis = (picked.FirstOrDefault() ?? VM.Segments.FirstOrDefault())?.Info;
        if (basis is null) return;

        var v = basis.PrimaryVideo;
        if (v is not null)
        {
            VM.VideoCodecIndex = MediaInfoFormat.VideoCodecIndex(v.CodecName);
            if (v.BitRate > 0)
            {
                int idx = ClosestIndex(VideoBitratePresets, v.BitRate / 1000);
                VM.VideoBitrateKbps = VideoBitratePresets[idx];
                CboVideoBitratePreset.SelectedIndex = idx;
            }
            if (v.Width > 0) VM.ResizeWidth = v.Width;
            if (v.Height > 0) VM.ResizeHeight = v.Height;
        }

        var a = basis.PrimaryAudio;
        if (a is not null)
        {
            VM.AudioCodecIndex = MediaInfoFormat.AudioCodecIndex(a.CodecName);
            if (a.BitRate > 0)
            {
                int idx = ClosestIndex(AudioBitratePresets, a.BitRate / 1000);
                VM.AudioBitrateKbps = AudioBitratePresets[idx];
                CboAudioBitratePreset.SelectedIndex = idx;
            }
        }
    }

    /// <summary>실제 선택된 파일/설정을 반영해 "저장될 파일명" 플레이스홀더를 동적으로 갱신.</summary>
    private void UpdateFileNamePlaceholder()
    {
        var picked = VM.Segments.Where(s => s.IsPicked).ToList();
        var basis = picked.FirstOrDefault() ?? VM.Segments.FirstOrDefault();
        if (basis is null)
        {
            FileNameBox.PlaceholderText = Loc.Get("out.file_name_ph");
            return;
        }

        var name = Path.GetFileNameWithoutExtension(basis.FilePath);
        var ext = VM.IsFastMode ? Path.GetExtension(basis.FilePath) : ExtensionFor(VM.ContainerIndex);
        if (string.IsNullOrEmpty(ext)) ext = ".mp4";

        FileNameBox.PlaceholderText = Loc.Format("out.file_name_ph_dynamic", name, ext);
    }

    private static string ExtensionFor(int containerIndex) => containerIndex switch
    {
        1 => ".mkv",
        2 => ".webm",
        3 => ".avi",
        _ => ".mp4",
    };

    private void ApplyLocale()
    {
        HeaderText.Text         = Loc.Get("out.title");
        TglFastMode.OnContent   = Loc.Get("out.fast_mode");
        TglFastMode.OffContent  = Loc.Get("out.convert_mode");
        CboFormat.Header        = Loc.Get("out.format");
        TxtSpeedLabel.Text      = Loc.Get("out.speed_short");
        ToolTipService.SetToolTip(TxtSpeedLabel, Loc.Get("out.speed"));
        TxtFolderHeader.Text    = Loc.Get("out.save_folder");
        FolderBox.PlaceholderText = Loc.Get("out.folder_ph");
        BtnBrowseFolder.Content = Loc.Get("files.browse");
        TxtFileNameHeader.Text  = Loc.Get("out.file_name");
        ToolTipService.SetToolTip(TxtFileNameHeader, Loc.Get("out.file_name_tip"));
        FileNameBox.PlaceholderText = Loc.Get("out.file_name_ph");
        TxtItemsHeader.Text     = Loc.Get("out.items_header");
        RbPickInclude.Content   = Loc.Get("out.pick_include");
        RbPickRemove.Content    = Loc.Get("out.pick_remove");
        TxtColFilename.Text     = Loc.Get("out.col_filename");
        TxtColVideo.Text        = Loc.Get("out.col_video");
        TxtColAudio.Text        = Loc.Get("out.col_audio");
        TxtColSpeed.Text        = Loc.Get("out.col_speed");
        TxtOptionsHeader.Text   = Loc.Get("out.options");
        CbMerge.Content         = Loc.Get("out.merge");
        CbSavePlayback.Content  = Loc.Get("out.save_playback");
        CbExtractAudio.Content  = Loc.Get("out.extract_audio");
        CbRemoveAudio.Content   = Loc.Get("out.remove_audio");
        BtnCancel.Content       = Loc.Get("dlg.cancel");
        BtnStart.Content        = Loc.Get("btn.start");
    }

    private async void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) FolderBox.Text = folder.Path;
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        VM.OutputDirOverride = string.IsNullOrEmpty(folder) ? null : folder;
        var fileName = FileNameBox.Text.Trim();
        VM.OutputFileNameOverride = string.IsNullOrEmpty(fileName) ? null : fileName;
        _tcs.TrySetResult(true);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(false);
        Close();
    }

    private void OnMasterSpeedLostFocus(object sender, RoutedEventArgs e)
    {
        VM.SpeedFactor = SpeedFormat.Parse(SldSpeed.Text, VM.SpeedFactor);
        SldSpeed.Text = SpeedFormat.Text(VM.SpeedFactor);
    }

    private void OnMasterSpeedKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { OnMasterSpeedLostFocus(sender, e); e.Handled = true; }
        else if (e.Key == VirtualKey.Up) { AdjustMasterSpeed(0.01); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { AdjustMasterSpeed(-0.01); e.Handled = true; }
    }

    private void OnMasterSpeedWheel(object sender, PointerRoutedEventArgs e)
    {
        AdjustMasterSpeed(e.GetCurrentPoint(SldSpeed).Properties.MouseWheelDelta > 0 ? 0.01 : -0.01);
        e.Handled = true;
    }

    private void AdjustMasterSpeed(double delta)
    {
        var cur = SpeedFormat.Parse(SldSpeed.Text, VM.SpeedFactor);
        VM.SpeedFactor = Math.Round(Math.Clamp(cur + delta, SpeedFormat.Min, SpeedFormat.Max), 2);
        SldSpeed.Text = SpeedFormat.Text(VM.SpeedFactor);
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        bool check = CbSelectAll.IsChecked == true;
        foreach (var seg in VM.Segments) seg.IsPicked = check;
    }

    private void OnEnableRowSpeed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipSegment seg })
            seg.SpeedIsCustom = true;
    }

    private void OnResetRowSpeed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipSegment seg })
        {
            seg.SpeedIsCustom = false;
            seg.Speed = VM.SpeedFactor;
        }
    }

    // 이 텍스트박스는 SpeedIsCustom == true일 때만 보이므로, 여기서는 값만 갱신하고
    // SpeedIsCustom을 다시 세우지 않는다 — 초기화 버튼 클릭 시 포커스 이동으로
    // LostFocus가 뒤따라 발생해 방금 false로 되돌린 값을 다시 true로 덮어쓰는 걸 방지.
    private void OnRowSpeedLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ClipSegment seg } tb)
        {
            seg.Speed = SpeedFormat.Parse(tb.Text, seg.Speed);
            tb.Text = SpeedFormat.Text(seg.Speed);
        }
    }

    private void OnRowSpeedKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: ClipSegment seg } tb) return;
        if (e.Key == VirtualKey.Enter)
        {
            seg.Speed = SpeedFormat.Parse(tb.Text, seg.Speed);
            tb.Text = SpeedFormat.Text(seg.Speed);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up) { AdjustRowSpeed(tb, seg, 0.01); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { AdjustRowSpeed(tb, seg, -0.01); e.Handled = true; }
    }

    private void OnRowSpeedWheel(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: ClipSegment seg } tb) return;
        AdjustRowSpeed(tb, seg, e.GetCurrentPoint(tb).Properties.MouseWheelDelta > 0 ? 0.01 : -0.01);
        e.Handled = true;
    }

    private static void AdjustRowSpeed(TextBox tb, ClipSegment seg, double delta)
    {
        var cur = SpeedFormat.Parse(tb.Text, seg.Speed);
        seg.Speed = Math.Round(Math.Clamp(cur + delta, SpeedFormat.Min, SpeedFormat.Max), 2);
        tb.Text = SpeedFormat.Text(seg.Speed);
    }

    // ════════════════ 변환 모드 상세 — 숫자 입력 공통 헬퍼 ════════════════

    private static void CommitNumeric(TextBox tb, Func<double> getter, Action<double> setter,
        double min, double max, int decimals)
    {
        var v = NumericFormat.Parse(tb.Text, getter(), min, max, decimals);
        setter(v);
        tb.Text = NumericFormat.Text(v, decimals);
    }

    private static void AdjustNumeric(TextBox tb, Func<double> getter, Action<double> setter,
        double min, double max, int decimals, double delta)
    {
        var cur = NumericFormat.Parse(tb.Text, getter(), min, max, decimals);
        var next = Math.Round(Math.Clamp(cur + delta, min, max), decimals);
        setter(next);
        tb.Text = NumericFormat.Text(next, decimals);
    }

    // ════════════════ 화면 크기 — 가로/세로 ════════════════

    private void OnResizeWidthLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumeric(TxtResizeWidth, () => VM.ResizeWidth, v => VM.ResizeWidth = v, 0, 16384, 0);

    private void OnResizeWidthKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { OnResizeWidthLostFocus(sender, e); e.Handled = true; }
        else if (e.Key == VirtualKey.Up) { AdjustNumeric(TxtResizeWidth, () => VM.ResizeWidth, v => VM.ResizeWidth = v, 0, 16384, 0, 2); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { AdjustNumeric(TxtResizeWidth, () => VM.ResizeWidth, v => VM.ResizeWidth = v, 0, 16384, 0, -2); e.Handled = true; }
    }

    private void OnResizeWidthWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(TxtResizeWidth).Properties.MouseWheelDelta > 0 ? 2 : -2;
        AdjustNumeric(TxtResizeWidth, () => VM.ResizeWidth, v => VM.ResizeWidth = v, 0, 16384, 0, delta);
        e.Handled = true;
    }

    private void OnResizeHeightLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumeric(TxtResizeHeight, () => VM.ResizeHeight, v => VM.ResizeHeight = v, 0, 16384, 0);

    private void OnResizeHeightKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { OnResizeHeightLostFocus(sender, e); e.Handled = true; }
        else if (e.Key == VirtualKey.Up) { AdjustNumeric(TxtResizeHeight, () => VM.ResizeHeight, v => VM.ResizeHeight = v, 0, 16384, 0, 2); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { AdjustNumeric(TxtResizeHeight, () => VM.ResizeHeight, v => VM.ResizeHeight = v, 0, 16384, 0, -2); e.Handled = true; }
    }

    private void OnResizeHeightWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(TxtResizeHeight).Properties.MouseWheelDelta > 0 ? 2 : -2;
        AdjustNumeric(TxtResizeHeight, () => VM.ResizeHeight, v => VM.ResizeHeight = v, 0, 16384, 0, delta);
        e.Handled = true;
    }

    // ════════════════ 비디오 비트레이트 — 프리셋/커스텀 ════════════════

    private void OnVideoBitratePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboVideoBitratePreset.SelectedIndex < 0) return;
        VM.VideoBitrateKbps = VideoBitratePresets[Math.Clamp(CboVideoBitratePreset.SelectedIndex, 0, VideoBitratePresets.Length - 1)];
    }

    private void OnEnableVideoBitrateCustom(object sender, RoutedEventArgs e) => VM.VideoBitrateIsCustom = true;

    private void OnResetVideoBitrateCustom(object sender, RoutedEventArgs e)
    {
        VM.VideoBitrateIsCustom = false;
        int idx = ClosestIndex(VideoBitratePresets, VM.VideoBitrateKbps);
        VM.VideoBitrateKbps = VideoBitratePresets[idx];
        CboVideoBitratePreset.SelectedIndex = idx;
    }

    private void OnVideoBitrateLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumeric(TxtVideoBitrate, () => VM.VideoBitrateKbps, v => VM.VideoBitrateKbps = v, 100, 200000, 0);

    private void OnVideoBitrateKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { OnVideoBitrateLostFocus(sender, e); e.Handled = true; }
        else if (e.Key == VirtualKey.Up) { AdjustNumeric(TxtVideoBitrate, () => VM.VideoBitrateKbps, v => VM.VideoBitrateKbps = v, 100, 200000, 0, 100); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { AdjustNumeric(TxtVideoBitrate, () => VM.VideoBitrateKbps, v => VM.VideoBitrateKbps = v, 100, 200000, 0, -100); e.Handled = true; }
    }

    private void OnVideoBitrateWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(TxtVideoBitrate).Properties.MouseWheelDelta > 0 ? 100 : -100;
        AdjustNumeric(TxtVideoBitrate, () => VM.VideoBitrateKbps, v => VM.VideoBitrateKbps = v, 100, 200000, 0, delta);
        e.Handled = true;
    }

    // ════════════════ 오디오 비트레이트 — 프리셋/커스텀 ════════════════

    private void OnAudioBitratePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboAudioBitratePreset.SelectedIndex < 0) return;
        VM.AudioBitrateKbps = AudioBitratePresets[Math.Clamp(CboAudioBitratePreset.SelectedIndex, 0, AudioBitratePresets.Length - 1)];
    }

    private void OnEnableAudioBitrateCustom(object sender, RoutedEventArgs e) => VM.AudioBitrateIsCustom = true;

    private void OnResetAudioBitrateCustom(object sender, RoutedEventArgs e)
    {
        VM.AudioBitrateIsCustom = false;
        int idx = ClosestIndex(AudioBitratePresets, VM.AudioBitrateKbps);
        VM.AudioBitrateKbps = AudioBitratePresets[idx];
        CboAudioBitratePreset.SelectedIndex = idx;
    }

    private void OnAudioBitrateLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumeric(TxtAudioBitrate, () => VM.AudioBitrateKbps, v => VM.AudioBitrateKbps = v, 16, 1024, 0);

    private void OnAudioBitrateKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { OnAudioBitrateLostFocus(sender, e); e.Handled = true; }
        else if (e.Key == VirtualKey.Up) { AdjustNumeric(TxtAudioBitrate, () => VM.AudioBitrateKbps, v => VM.AudioBitrateKbps = v, 16, 1024, 0, 8); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { AdjustNumeric(TxtAudioBitrate, () => VM.AudioBitrateKbps, v => VM.AudioBitrateKbps = v, 16, 1024, 0, -8); e.Handled = true; }
    }

    private void OnAudioBitrateWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(TxtAudioBitrate).Properties.MouseWheelDelta > 0 ? 8 : -8;
        AdjustNumeric(TxtAudioBitrate, () => VM.AudioBitrateKbps, v => VM.AudioBitrateKbps = v, 16, 1024, 0, delta);
        e.Handled = true;
    }

    // ════════════════ 프레임레이트 — 프리셋/커스텀 ════════════════

    private static readonly double[] FrameRatePresets = [0, 24, 25, 30, 48, 50, 60, 120];

    private void OnFrameRatePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboFrameRatePreset.SelectedIndex < 0) return;
        VM.OutputFrameRate = FrameRatePresets[Math.Clamp(CboFrameRatePreset.SelectedIndex, 0, FrameRatePresets.Length - 1)];
    }

    private void OnEnableFrameRateCustom(object sender, RoutedEventArgs e) => VM.FrameRateIsCustom = true;

    private void OnResetFrameRateCustom(object sender, RoutedEventArgs e)
    {
        VM.FrameRateIsCustom = false;
        int best = 0;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < FrameRatePresets.Length; i++)
        {
            var diff = Math.Abs(FrameRatePresets[i] - VM.OutputFrameRate);
            if (diff < bestDiff) { bestDiff = diff; best = i; }
        }
        VM.OutputFrameRate = FrameRatePresets[best];
        CboFrameRatePreset.SelectedIndex = best;
    }

    private void OnFrameRateLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumeric(TxtFrameRate, () => VM.OutputFrameRate, v => VM.OutputFrameRate = v, 0, 240, 3);

    private void OnFrameRateKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { OnFrameRateLostFocus(sender, e); e.Handled = true; }
        else if (e.Key == VirtualKey.Up) { AdjustNumeric(TxtFrameRate, () => VM.OutputFrameRate, v => VM.OutputFrameRate = v, 0, 240, 3, 1); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { AdjustNumeric(TxtFrameRate, () => VM.OutputFrameRate, v => VM.OutputFrameRate = v, 0, 240, 3, -1); e.Handled = true; }
    }

    private void OnFrameRateWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(TxtFrameRate).Properties.MouseWheelDelta > 0 ? 1 : -1;
        AdjustNumeric(TxtFrameRate, () => VM.OutputFrameRate, v => VM.OutputFrameRate = v, 0, 240, 3, delta);
        e.Handled = true;
    }
}
