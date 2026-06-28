using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VCut.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace VCut.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel VM { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "v-cut — 동영상 편집기";

        VM.FilePicker = PickFilesAsync;
        VM.ShowMessage = ShowMessageAsync;
        VM.PropertyChanged += OnVmPropertyChanged;
        VM.LoadPreview = (path, fps) =>
        {
            Preview.FrameRate = fps;
            Preview.SetSource(path, VM.MediaDuration);
            Preview.Volume = VM.SelectedSegment?.Volume ?? 1.0;
        };
        Preview.PositionChanged += (s, pos) => VM.PlayPosition = pos;
        Preview.VolumeChanged  += (s, v)   => { if (VM.SelectedSegment is { } seg) seg.Volume = v; };

        SetupShortcuts();
        ShowScreen("home");

        if (AppWindow is { } aw)
            aw.Resize(new Windows.Graphics.SizeInt32(1320, 880));
    }

    // ════════ 화면 전환(홈/편집) ════════
    private void ShowScreen(string screen)
    {
        bool home = screen == "home";
        HomeOverlay.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        EditGrid.Visibility = home ? Visibility.Collapsed : Visibility.Visible;

        // 모드별 옵션 표시.
        SplitOptions.Visibility = screen == "split" ? Visibility.Visible : Visibility.Collapsed;
        ListTitle.Text = screen switch
        {
            "split" => "나누기 구간 목록",
            "merge" => "합치기 구간 목록",
            _ => "자르기 구간 목록",
        };
        if (screen == "merge") VM.MergeEnabled = true;
    }

    private void OnRailHome(object s, RoutedEventArgs e) => ShowScreen("home");
    private void OnRailTrim(object s, RoutedEventArgs e) => ShowScreen("trim");
    private void OnRailSplit(object s, RoutedEventArgs e) => ShowScreen("split");
    private void OnRailMerge(object s, RoutedEventArgs e) => ShowScreen("merge");

    private async void OnRailInfo(object s, RoutedEventArgs e) =>
        await ShowMessageAsync("v-cut 정보", "v-cut 동영상 편집기\n버전 0.1.0\nFFmpeg 기반\n\nWinUI 3 / .NET 8");

    private void OnTileTrim(object s, RoutedEventArgs e)  => ShowScreen("trim");
    private void OnTileSplit(object s, RoutedEventArgs e) => ShowScreen("split");
    private void OnTileMerge(object s, RoutedEventArgs e) => ShowScreen("merge");

    // ════════ 단축키 ════════
    private void SetupShortcuts()
    {
        AddAccel(VirtualKey.F2, VirtualKeyModifiers.None, () => _ = VM.OpenCommand.ExecuteAsync(null));
        AddAccel(VirtualKey.F5, VirtualKeyModifiers.None, OpenSettings);
        AddAccel(VirtualKey.O, VirtualKeyModifiers.Control, () => _ = VM.OpenCommand.ExecuteAsync(null));
        AddAccel(VirtualKey.Delete, VirtualKeyModifiers.None, () => VM.RemoveSelectedClipCommand.Execute(null));
    }

    private void AddAccel(VirtualKey key, VirtualKeyModifiers mod, Action handler)
    {
        var accel = new KeyboardAccelerator { Key = key, Modifiers = mod };
        accel.Invoked += (s, e) => { handler(); e.Handled = true; };
        Root.KeyboardAccelerators.Add(accel);
    }

    // ════════ 시작 → 출력 설정 → 실행 ════════
    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (VM.Segments.Count == 0)
        {
            await ShowMessageAsync("알림", "먼저 편집할 영상을 추가하세요.");
            return;
        }
        var dlg = new OutputSettingsDialog(VM) { XamlRoot = Root.XamlRoot };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary && VM.RunComposeCommand.CanExecute(null))
            VM.RunComposeCommand.Execute(null);
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (VM.OpenCommand.CanExecute(null)) await VM.OpenCommand.ExecuteAsync(null);
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => OpenSettings();
    private void OpenSettings() => new SettingsWindow().Activate();

    private void OnSetStartToCurrent(object sender, RoutedEventArgs e) => VM.TrimStart = Preview.Position;
    private void OnSetEndToCurrent(object sender, RoutedEventArgs e) => VM.TrimEnd = Preview.Position;
    private void OnRangePlayPause(object sender, RoutedEventArgs e) => Preview.ToggleRangePlay();
    private void OnRangeStop(object sender, RoutedEventArgs e) => Preview.StopRange();

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (VM.CaptureFrameCommand.CanExecute(null)) VM.CaptureFrameCommand.Execute(null);
    }

    // ════════ 다중 선택 ════════

    // TwoWay SelectedItem 바인딩 대신 수동 동기화 — TwoWay 바인딩은 Shift+클릭 중
    // SelectedItem을 재설정해 SelectedItems 전체를 초기화하는 부작용이 있음.
    private bool _syncingListView;

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedSegment) || _syncingListView) return;
        _syncingListView = true;
        var target = VM.SelectedSegment;
        foreach (var seg in VM.Segments)
            seg.IsSelected = seg == target;
        SegmentList.SelectedItems.Clear();
        if (target is not null)
            SegmentList.SelectedItem = target;
        _syncingListView = false;
    }

    private void OnSegmentListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingListView) return;
        foreach (var item in e.RemovedItems.OfType<ClipSegment>())
            item.IsSelected = false;
        foreach (var item in e.AddedItems.OfType<ClipSegment>())
            item.IsSelected = true;
        _syncingListView = true;
        VM.SelectedSegment = e.AddedItems.OfType<ClipSegment>().LastOrDefault()
            ?? SegmentList.SelectedItems.OfType<ClipSegment>().LastOrDefault();
        _syncingListView = false;
    }

    private void OnSegmentListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsOnListItem(e.OriginalSource)) return;
        SegmentList.SelectedItems.Clear();
    }

    private void OnSegmentListPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingItems) return;
        if (IsOnListItem(e.OriginalSource)) return;
        SegmentList.SelectedItems.Clear();
    }

    private static bool IsOnListItem(object? source)
    {
        var el = source as DependencyObject;
        while (el != null)
        {
            if (el is ListViewItem) return true;
            el = VisualTreeHelper.GetParent(el);
        }
        return false;
    }

    // ════════ 드래그 앤 드롭 ════════

    private bool _isDraggingItems;

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _isDraggingItems = true;
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _isDraggingItems = false;
    }

    private static readonly HashSet<string> _videoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".ts", ".webm", ".vob", ".m4v", ".mpg", ".mpeg"
    };

    private void OnSegmentListDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "영상 추가";
        }
        // 내부 순서 조정 드래그는 ListView가 자체 처리하도록 AcceptedOperation 미설정
    }

    private async void OnSegmentListDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items
            .OfType<StorageFile>()
            .Where(f => _videoExts.Contains(Path.GetExtension(f.Path)))
            .Select(f => f.Path)
            .ToList();
        if (paths.Count > 0)
            await VM.AddClipsAsync(paths);
    }

    // ════════ 파일/메시지 ════════
    private async Task<IReadOnlyList<string>> PickFilesAsync(bool allowMultiple)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
        };
        foreach (var ext in new[] { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".ts", ".webm", ".vob", ".m4v", ".mpg", ".mpeg", ".vcproj" })
            picker.FileTypeFilter.Add(ext);
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        if (allowMultiple)
            return (await picker.PickMultipleFilesAsync()).Select(f => f.Path).ToList();
        var file = await picker.PickSingleFileAsync();
        return file is null ? [] : [file.Path];
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                MaxHeight = 400,
            },
            CloseButtonText = "확인",
            XamlRoot = Root.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
