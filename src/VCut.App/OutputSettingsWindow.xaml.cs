using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

        AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 640));

        // 창을 열 때마다 "포함 모드"로 초기화. 메인 목록에서 선택(IsSelected)해둔 항목이 있으면
        // 그것만 기본으로 체크, 없으면(아무것도 선택 안 했으면) 전체를 기본으로 체크.
        bool anySelected = vm.Segments.Any(s => s.IsSelected);
        foreach (var seg in vm.Segments) seg.IsPicked = anySelected ? seg.IsSelected : true;
        vm.RemoveSelectedEnabled = false;

        Closed += (_, _) => _tcs.TrySetResult(false);
    }

    private void ApplyLocale()
    {
        HeaderText.Text         = Loc.Get("out.title");
        TxtModeHeader.Text      = Loc.Get("out.mode");
        TglFastMode.OnContent   = Loc.Get("out.fast_mode");
        TglFastMode.OffContent  = Loc.Get("out.convert_mode");
        CboFormat.Header        = Loc.Get("out.format");
        SldQuality.Header       = Loc.Get("out.quality");
        TxtSpeedLabel.Text      = Loc.Get("out.speed_short");
        ToolTipService.SetToolTip(TxtSpeedLabel, Loc.Get("out.speed"));
        TxtFolderHeader.Text    = Loc.Get("out.save_folder");
        FolderBox.PlaceholderText = Loc.Get("out.folder_ph");
        BtnBrowseFolder.Content = Loc.Get("files.browse");
        TxtItemsHeader.Text     = Loc.Get("out.items_header");
        RbPickInclude.Content   = Loc.Get("out.pick_include");
        RbPickRemove.Content    = Loc.Get("out.pick_remove");
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
}
