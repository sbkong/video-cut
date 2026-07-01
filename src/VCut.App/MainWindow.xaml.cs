using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Title = ver is null ? "v-cut" : $"v-cut {ver.Major}.{ver.Minor}.{ver.Build}";

        VM.FilePicker = PickFilesAsync;
        VM.ShowMessage = ShowMessageAsync;
        VM.ShowConfirm = (t, m) => ShowConfirmAsync(t, m);
        VM.SaveProjectPicker = PickSaveProjectAsync;
        VM.OpenProjectPicker = PickOpenProjectAsync;
        VM.NavigateTo = screen => ShowScreen(screen);
        VM.RequestSelectionSync = SyncListViewSelection;
        VM.PropertyChanged += OnVmPropertyChanged;
        VM.LoadPreview = (path, fps) =>
        {
            Preview.FrameRate = fps;
            Preview.SetSource(path, VM.MediaDuration);
            Preview.Volume = VM.SelectedSegment?.Volume ?? 1.0;
        };
        VM.ClearPreview = () => Preview.Clear();
        Preview.PositionChanged += (s, pos) => VM.PlayPosition = pos;
        Preview.VolumeChanged  += (s, v)   => { if (VM.SelectedSegment is { } seg) seg.Volume = v; };

        // PointerPressed는 SelectionChanged보다 먼저 발화 → 다중 선택 보존용
        SegmentList.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(OnSegmentListPointerPressed), handledEventsToo: true);
        // PointerReleased: 드래그 없는 클릭이면 _preSelectionDrag를 정리하고 IsSelected 재동기화
        SegmentList.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnSegmentListPointerReleased), handledEventsToo: true);

        _segmentContextFlyout = new MenuFlyout();
        _segmentContextFlyout.Items.Add(new MenuFlyoutItem { Text = "제거",        Command = VM.RemoveSelectedClipCommand, Icon = new FontIcon { Glyph = "" } });
        _segmentContextFlyout.Items.Add(new MenuFlyoutSeparator());
        _segmentContextFlyout.Items.Add(new MenuFlyoutItem { Text = "위로 이동",   Command = VM.MoveClipUpCommand,          Icon = new FontIcon { Glyph = "" } });
        _segmentContextFlyout.Items.Add(new MenuFlyoutItem { Text = "아래로 이동", Command = VM.MoveClipDownCommand,        Icon = new FontIcon { Glyph = "" } });

        SetupShortcuts();
        ShowScreen("home");

        SetupMinWindowSize();

        if (AppWindow is { } aw)
        {
            RestoreWindowState(aw);
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (System.IO.File.Exists(iconPath))
                aw.SetIcon(iconPath);
        }

        AppWindow.Closing += async (sender, args) =>
        {
            if (_confirmClose || !VM.IsModified)
            {
                SaveWindowState();
                return;
            }
            args.Cancel = true;

            var dlg = new ContentDialog
            {
                Title = "저장하지 않은 변경 사항",
                Content = "프로젝트에 저장되지 않은 변경 사항이 있습니다.\n저장하시겠습니까?",
                PrimaryButtonText = "저장",
                SecondaryButtonText = "저장 안 함",
                CloseButtonText = "취소",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Root.XamlRoot,
            };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await VM.SaveProjectCommand.ExecuteAsync(null);
                if (!VM.IsModified) { SaveWindowState(); _confirmClose = true; Close(); }
            }
            else if (result == ContentDialogResult.Secondary)
            {
                SaveWindowState();
                _confirmClose = true;
                Close();
            }
        };
    }

    private bool _confirmClose;
    private bool _isHomeScreen = true;

    // ════════ 창 크기/위치 저장·복원 ════════

    private static void RestoreWindowState(Microsoft.UI.Windowing.AppWindow aw)
    {
        var s = Settings.SettingsStore.Current;
        int left, top;
        if (s.WindowLeft >= 0 && s.WindowTop >= 0)
        {
            left = s.WindowLeft;
            top  = s.WindowTop;
        }
        else
        {
            var area = DisplayArea.GetFromWindowId(aw.Id, DisplayAreaFallback.Nearest).WorkArea;
            left = area.X + Math.Max(0, (area.Width  - s.WindowWidth)  / 2);
            top  = area.Y + Math.Max(0, (area.Height - s.WindowHeight) / 2);
        }
        aw.MoveAndResize(new Windows.Graphics.RectInt32(left, top, s.WindowWidth, s.WindowHeight));

        if (s.WindowMaximized && aw.Presenter is OverlappedPresenter op)
            op.Maximize();
    }

    private void SaveWindowState()
    {
        if (AppWindow is not { } aw) return;
        var s = Settings.SettingsStore.Current.Clone();
        bool maximized = aw.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        s.WindowMaximized = maximized;
        if (!maximized)
        {
            s.WindowWidth  = aw.Size.Width;
            s.WindowHeight = aw.Size.Height;
            s.WindowLeft   = aw.Position.X;
            s.WindowTop    = aw.Position.Y;
        }
        Settings.SettingsStore.Save(s);
    }

    // ════════ 최소 윈도우 크기 (960×540 = 1920×1080의 1/4) ════════

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate? _wndProc;
    private IntPtr _originalWndProc;

    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr val);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    private void SetupMinWindowSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        _wndProc = WndProc;
        _originalWndProc = SetWindowLongPtr(hwnd, -4, Marshal.GetFunctionPointerForDelegate(_wndProc));
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == 0x0024) // WM_GETMINMAXINFO
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            double scale = GetDpiForWindow(hwnd) / 96.0;
            mmi.ptMinTrackSize.x = (int)(960 * scale);
            mmi.ptMinTrackSize.y = (int)(540 * scale);
            Marshal.StructureToPtr(mmi, lParam, true);
        }
        return CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
    }

    // ════════ 화면 전환(홈/편집) ════════
    private void ShowScreen(string screen)
    {
        bool home = screen == "home";
        _isHomeScreen = home;
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
        if (!home) VM.CurrentScreen = screen;
        UpdateStartButtonVisibility();
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
        AddAccel(VirtualKey.S, VirtualKeyModifiers.Control, () => _ = VM.SaveProjectCommand.ExecuteAsync(null));
        AddAccel(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => _ = VM.SaveProjectAsCommand.ExecuteAsync(null));
        AddAccel(VirtualKey.P, VirtualKeyModifiers.Control, () => _ = VM.OpenProjectCommand.ExecuteAsync(null));
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
    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Exit();

    private async void OnSaveProject(object sender, RoutedEventArgs e) =>
        await VM.SaveProjectCommand.ExecuteAsync(null);

    private async void OnSaveProjectAs(object sender, RoutedEventArgs e) =>
        await VM.SaveProjectAsCommand.ExecuteAsync(null);

    private async void OnOpenProject(object sender, RoutedEventArgs e) =>
        await VM.OpenProjectCommand.ExecuteAsync(null);

    private async void OnRecentProjectClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            await HandleRecentProjectAsync(path);
    }

    private async Task HandleRecentProjectAsync(string path)
    {
        if (!File.Exists(path))
        {
            var dlg = new ContentDialog
            {
                Title = "파일 없음",
                Content = "이동되었거나 제거되었습니다.\n목록에서 지울까요?",
                PrimaryButtonText = "목록에서 제거",
                CloseButtonText = "취소",
                XamlRoot = Root.XamlRoot,
            };
            if (await dlg.ShowAsync() == ContentDialogResult.Primary)
                VM.RemoveRecentProject(path);
            return;
        }
        await VM.LoadProjectFromFileAsync(path);
    }

    // 프로젝트 드롭다운 열릴 때 최근 프로젝트 동적 추가
    private const int ProjectFlyoutBaseCount = 5; // 새, 열기, ---, 저장, 다른이름으로저장

    private void OnProjectFlyoutOpening(object? sender, object e)
    {
        while (ProjectFlyout.Items.Count > ProjectFlyoutBaseCount)
            ProjectFlyout.Items.RemoveAt(ProjectFlyoutBaseCount);

        if (VM.RecentProjects.Count == 0) return;

        ProjectFlyout.Items.Add(new MenuFlyoutSeparator());
        foreach (var recent in VM.RecentProjects)
        {
            var item = new MenuFlyoutItem
            {
                Text = recent.IsMissing ? recent.FileName + "  (없음)" : recent.FileName,
                Tag = recent.Path,
                Opacity = recent.MissingOpacity,
            };
            ToolTipService.SetToolTip(item, recent.Directory);
            item.Click += async (s, _) =>
            {
                if (s is MenuFlyoutItem { Tag: string path })
                    await HandleRecentProjectAsync(path);
            };
            ProjectFlyout.Items.Add(item);
        }
    }

    private void OnSetStartToCurrent(object sender, RoutedEventArgs e) => VM.TrimStart = Preview.Position;
    private void OnSetEndToCurrent(object sender, RoutedEventArgs e) => VM.TrimEnd = Preview.Position;
    private void OnRangePlayPause(object sender, RoutedEventArgs e)
    {
        bool wasPlaying = Preview.IsPlaying;
        Preview.ToggleRangePlay();
        RangePlayIcon.Glyph = wasPlaying ? "" : "";
    }

    private void OnRangeStop(object sender, RoutedEventArgs e)
    {
        Preview.StopRange();
        RangePlayIcon.Glyph = "";
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (VM.CaptureFrameCommand.CanExecute(null)) VM.CaptureFrameCommand.Execute(null);
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
            card.Background = (SolidColorBrush)Application.Current.Resources["BcCardHoverBrush"];
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
            card.Background = (SolidColorBrush)Application.Current.Resources["BcCardBrush"];
    }

    // ════════ 다중 선택 ════════

    // TwoWay SelectedItem 바인딩 대신 수동 동기화 — TwoWay 바인딩은 Shift+클릭 중
    // SelectedItem을 재설정해 SelectedItems 전체를 초기화하는 부작용이 있음.
    private bool _syncingListView;

    private void UpdateStartButtonVisibility()
    {
        bool show = !VM.IsBusy && !_isHomeScreen;
        StartButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsBusy))
        {
            UpdateStartButtonVisibility();
            CancelButton.Visibility = VM.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        if (e.PropertyName != nameof(MainViewModel.SelectedSegment) || _syncingListView) return;
        System.Diagnostics.Debug.WriteLine($"[SEL] OnVmPropertyChanged: target={VM.SelectedSegment?.FileName ?? "null"} syncing={_syncingListView} moving={VM.IsMovingItem}");
        _syncingListView = true;
        var target = VM.SelectedSegment;
        foreach (var seg in VM.Segments)
            seg.IsSelected = seg == target;
        SegmentList.SelectedItems.Clear();
        if (target is not null)
            SegmentList.SelectedItem = target;
        _syncingListView = false;
    }

    private void SyncListViewSelection()
    {
        var isSelected = VM.Segments.Where(s => s.IsSelected).Select(s => s.FileName).ToList();
        System.Diagnostics.Debug.WriteLine($"[SEL] SyncListViewSelection called: IsSelected=[{string.Join(",", isSelected)}] moving={VM.IsMovingItem}");
        DispatcherQueue.TryEnqueue(() =>
        {
            System.Diagnostics.Debug.WriteLine($"[SEL] outer TryEnqueue: syncing={_syncingListView} moving={VM.IsMovingItem}");
            _syncingListView = true;
            SegmentList.SelectedItems.Clear();
            foreach (var seg in VM.Segments.Where(s => s.IsSelected))
                SegmentList.SelectedItems.Add(seg);
            System.Diagnostics.Debug.WriteLine($"[SEL] outer TryEnqueue done: SelectedItems.Count={SegmentList.SelectedItems.Count}");
            DispatcherQueue.TryEnqueue(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[SEL] inner TryEnqueue: SelectedItems.Count={SegmentList.SelectedItems.Count} syncing={_syncingListView} moving={VM.IsMovingItem}");
                VM.IsMovingItem = false;
                _syncingListView = false;
            });
        });
    }

    private void OnSegmentListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[SEL] SelectionChanged: added=[{string.Join(",", e.AddedItems.OfType<ClipSegment>().Select(s => s.FileName))}] removed=[{string.Join(",", e.RemovedItems.OfType<ClipSegment>().Select(s => s.FileName))}] syncing={_syncingListView} moving={VM.IsMovingItem} saved={_preSelectionDrag?.Count ?? 0}");
        if (_syncingListView || VM.IsMovingItem) return;

        // _preSelectionDrag가 있으면 드래그 직전 WinUI 3가 발화시키는 SelectionChanged일 수 있음
        // → IsSelected 플래그를 건드리지 않고 VM.SelectedSegment만 업데이트 (DragItemsCompleted에서 복원)
        bool possibleDragInit = _preSelectionDrag is { Count: > 1 }
            && e.RemovedItems.Count > 0
            && e.RemovedItems.OfType<ClipSegment>().All(s => _preSelectionDrag!.Contains(s));

        if (!possibleDragInit)
        {
            foreach (var item in e.RemovedItems.OfType<ClipSegment>())
                item.IsSelected = false;
            foreach (var item in e.AddedItems.OfType<ClipSegment>())
                item.IsSelected = true;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[SEL] SelectionChanged: drag-init 감지, IsSelected 유지");
        }

        _syncingListView = true;
        VM.SelectedSegment = e.AddedItems.OfType<ClipSegment>().LastOrDefault()
            ?? SegmentList.SelectedItems.OfType<ClipSegment>().LastOrDefault();
        _syncingListView = false;
    }


    // ════════ 드래그 앤 드롭 ════════

    private List<ClipSegment>? _preSelectionDrag;
    private MenuFlyout _segmentContextFlyout = null!;

    // 우클릭: 선택 안 된 항목이면 단독 선택, 이미 선택된 항목이면 기존 선택 유지 후 메뉴 표시
    private void OnSegmentListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: ClipSegment tapped }
            && !SegmentList.SelectedItems.Contains(tapped))
        {
            SegmentList.SelectedItem = tapped;
        }
        if (SegmentList.SelectedItems.Count == 0) return;
        _segmentContextFlyout.ShowAt(SegmentList, new FlyoutShowOptions { Position = e.GetPosition(SegmentList) });
        e.Handled = true;
    }

    // SelectionChanged보다 먼저 발화 → 드래그 시작 직전 다중 선택 보존
    private void OnSegmentListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _preSelectionDrag = SegmentList.SelectedItems.Count > 1
            ? SegmentList.SelectedItems.OfType<ClipSegment>().ToList()
            : null;
        System.Diagnostics.Debug.WriteLine($"[SEL] PointerPressed: saved={_preSelectionDrag?.Count ?? 0} items");
    }

    // 클릭 시 SelectionChanged는 PointerReleased 이후에 동기 발화 →
    // deferred lambda가 돌 때는 이미 SelectionChanged가 처리된 이후이므로 SelectedItems가 최신 상태
    private void OnSegmentListPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_preSelectionDrag == null || VM.IsMovingItem) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_preSelectionDrag == null || VM.IsMovingItem) return; // 드래그가 시작됐으면 DragItemsCompleted가 처리
            System.Diagnostics.Debug.WriteLine("[SEL] PointerReleased deferred: 클릭 확정, IsSelected 재동기화");
            _preSelectionDrag = null;
            _syncingListView = true;
            foreach (var seg in VM.Segments)
                seg.IsSelected = SegmentList.SelectedItems.Contains(seg);
            _syncingListView = false;
        });
    }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[SEL] DragItemsStarting: SelectedItems={SegmentList.SelectedItems.Count} IsSelected={VM.Segments.Count(s => s.IsSelected)} saved={_preSelectionDrag?.Count ?? 0}");
        VM.IsMovingItem = true;
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var saved = _preSelectionDrag;
        _preSelectionDrag = null;
        System.Diagnostics.Debug.WriteLine($"[SEL] DragItemsCompleted: saved={saved?.Count ?? 0} IsSelected={VM.Segments.Count(s => s.IsSelected)}");

        VM.Renumber();

        if (saved is { Count: > 1 })
        {
            System.Diagnostics.Debug.WriteLine($"[SEL] DragItemsCompleted: IsSelected 복원 {saved.Count}개");
            foreach (var seg in VM.Segments)
                seg.IsSelected = saved.Contains(seg);
        }

        SyncListViewSelection();
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

    private async Task<string?> PickSaveProjectAsync(string? defaultName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = defaultName ?? "project",
        };
        picker.FileTypeChoices.Add("v-cut 프로젝트", [".vcproj"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private async Task<string?> PickOpenProjectAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".vcproj");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

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

    private async Task<(bool confirmed, bool dontAskAgain)> ShowConfirmAsync(string title, string message)
    {
        var checkBox = new CheckBox
        {
            Content = "다시 묻지 않기",
            Margin = new Microsoft.UI.Xaml.Thickness(0, 10, 0, 0),
        };
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(checkBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "예",
            CloseButtonText = "아니요",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        return (result == ContentDialogResult.Primary, checkBox.IsChecked == true);
    }
}
