using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VCut.App.Keymap;
using VCut.App.Locale;
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
        _segmentContextFlyout.Items.Add(new MenuFlyoutItem { Text = Loc.Get("ctx.remove"),        Command = VM.RemoveSelectedClipCommand, Icon = new FontIcon { Glyph = "" } });
        _segmentContextFlyout.Items.Add(new MenuFlyoutSeparator());
        _segmentContextFlyout.Items.Add(new MenuFlyoutItem { Text = Loc.Get("ctx.move_up"),   Command = VM.MoveClipUpCommand,          Icon = new FontIcon { Glyph = "" } });
        _segmentContextFlyout.Items.Add(new MenuFlyoutItem { Text = Loc.Get("ctx.move_down"), Command = VM.MoveClipDownCommand,        Icon = new FontIcon { Glyph = "" } });

        ApplyLocale();

        SetupShortcuts();
        ShowScreen("home");

        SetupMinWindowSize();

        Closed += (_, _) => _modalChild?.Close();

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
                Title = Loc.Get("dlg.unsaved_title"),
                Content = Loc.Get("dlg.unsaved_msg"),
                PrimaryButtonText = Loc.Get("dlg.save"),
                SecondaryButtonText = Loc.Get("dlg.no_save"),
                CloseButtonText = Loc.Get("dlg.cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Root.XamlRoot,
                FontFamily = AppFontFamily,
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

    private void ApplyLocale()
    {
        // Menu bar
        MenuSettings.Text    = Loc.Get("menu.settings");
        MenuAbout.Text       = Loc.Get("menu.about");
        MenuRestart.Text     = Loc.Get("menu.restart");
        MenuExit.Text        = Loc.Get("menu.exit");
        MenuNewProject.Text  = Loc.Get("project.new");
        MenuOpenProject.Text = Loc.Get("project.open");
        MenuSave.Text        = Loc.Get("project.save");
        MenuSaveAs.Text      = Loc.Get("project.save_as");

        // Rail tooltips
        ToolTipService.SetToolTip(RailHome,  Loc.Get("nav.home"));
        ToolTipService.SetToolTip(RailTrim,  Loc.Get("nav.trim"));
        ToolTipService.SetToolTip(RailSplit, Loc.Get("nav.split"));

        // Segment panel
        ListTitle.Text = Loc.Get("panel.trim_list");
        if (TotalDurText.Inlines[0] is Run runTotalDurLabel)
            runTotalDurLabel.Text = Loc.Get("panel.total_dur");
        EmptyAddLabel.Text = Loc.Get("panel.add_video");

        // Segment panel toolbar
        ToolTipService.SetToolTip(BtnDeleteSeg, Loc.Get("btn.delete"));
        ToolTipService.SetToolTip(BtnMoveUp2,   Loc.Get("btn.up"));
        ToolTipService.SetToolTip(BtnMoveDown2, Loc.Get("btn.down"));
        BtnAddVideo2.Content = Loc.Get("panel.add_video");

        // Preview header
        ToolTipService.SetToolTip(BtnResetTransform, Loc.Get("btn.reset_transform"));
        ToolTipService.SetToolTip(BtnRotate90,        Loc.Get("btn.rotate90"));
        ToolTipService.SetToolTip(BtnFlipH,           Loc.Get("btn.flip_h"));
        ToolTipService.SetToolTip(BtnFlipV,           Loc.Get("btn.flip_v"));
        ToolTipService.SetToolTip(BtnCaptureFrame,    Loc.Get("btn.capture"));

        // Trim controls
        LblSetStart.Text = Loc.Get("btn.set_start");
        ToolTipService.SetToolTip(BtnSetStart,  Loc.Get("tip.set_start"));
        LblSetEnd.Text   = Loc.Get("btn.set_end");
        ToolTipService.SetToolTip(BtnSetEnd,    Loc.Get("tip.set_end"));
        ToolTipService.SetToolTip(BtnRangePlay, Loc.Get("btn.play_range"));
        ToolTipService.SetToolTip(BtnRangeStop, Loc.Get("btn.stop_range"));

        // Split options
        RbSplitCount.Content  = Loc.Get("rb.split_count");
        RbSplitTime.Content   = Loc.Get("rb.split_time");
        BtnSplitRun.Content   = Loc.Get("btn.split_run");

        // Start / cancel
        StartButton.Content  = Loc.Get("btn.start");
        CancelButton.Content = Loc.Get("btn.cancel");

        // Home overlay
        HomeTagline.Text  = Loc.Get("home.tagline");
        TileTrimText.Text = Loc.Get("home.tile_trim");
        TileSplitText.Text = Loc.Get("home.tile_split");
        RecentTitle.Text  = Loc.Get("home.recent");
    }

    /// <summary>다이얼로그는 XamlRoot만으론 폰트를 상속받지 못하므로 명시적으로 지정.</summary>
    private static Microsoft.UI.Xaml.Media.FontFamily AppFontFamily =>
        FontService.Resolve(Settings.SettingsStore.Current);

    // ════════ 창 크기/위치 저장·복원 ════════

    private static void RestoreWindowState(Microsoft.UI.Windowing.AppWindow aw)
    {
        var s = Settings.SettingsStore.Current;
        int left, top;
        if (s.WindowPositionSet && IsOnAnyDisplay(s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight))
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

    /// <summary>저장된 창 위치가 현재 연결된 모니터 중 하나와 겹치는지 확인.
    /// 모니터 구성이 바뀌었거나(예: 보조 모니터 연결 해제) 저장 당시와 달라져
    /// 창이 화면 밖으로 완전히 벗어나는 것을 방지한다.</summary>
    private static bool IsOnAnyDisplay(int left, int top, int width, int height)
    {
        // foreach(IReadOnlyList<DisplayArea>)는 일부 WinAppSDK 버전에서 CsWinRT 프로젝션 문제로
        // InvalidCastException을 던지므로 인덱서로 순회한다.
        var displays = DisplayArea.FindAll();
        for (int i = 0; i < displays.Count; i++)
        {
            var wa = displays[i].WorkArea;
            bool overlaps = left < wa.X + wa.Width && left + width > wa.X &&
                            top  < wa.Y + wa.Height && top  + height > wa.Y;
            if (overlaps) return true;
        }
        return false;
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
            s.WindowPositionSet = true;
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
        AddRangeDivider.Visibility = screen == "trim" ? Visibility.Visible : Visibility.Collapsed;
        BtnAddRange.Visibility = screen == "trim" ? Visibility.Visible : Visibility.Collapsed;
        ListTitle.Text = screen switch
        {
            "split" => Loc.Get("panel.split_list"),
            _ => Loc.Get("panel.trim_list"),
        };
        if (!home) VM.CurrentScreen = screen;
        UpdateStartButtonVisibility();
    }

    private void OnRailHome(object s, RoutedEventArgs e) => ShowScreen("home");
    private void OnRailTrim(object s, RoutedEventArgs e) => ShowScreen("trim");
    private void OnRailSplit(object s, RoutedEventArgs e) => ShowScreen("split");

    private async void OnRailInfo(object s, RoutedEventArgs e)
    {
        var appVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var verStr = appVer is null ? "0.1.0" : $"{appVer.Major}.{appVer.Minor}.{appVer.Build}";
        await ShowMessageAsync(Loc.Get("dlg.about_title"), Loc.Format("dlg.about_msg", verStr));
    }

    private void OnTileTrim(object s, RoutedEventArgs e)  => ShowScreen("trim");
    private void OnTileSplit(object s, RoutedEventArgs e) => ShowScreen("split");

    // ════════ 단축키 ════════
    /// <summary>설정창 '단축키' 탭에서 재할당 가능한 액션 → 실행 위임 매핑.</summary>
    private Dictionary<string, Action> BuildKeymapActionMap() => new()
    {
        ["open_file"]       = () => _ = VM.OpenCommand.ExecuteAsync(null),
        ["save_project"]    = () => _ = VM.SaveProjectCommand.ExecuteAsync(null),
        ["save_project_as"] = () => _ = VM.SaveProjectAsCommand.ExecuteAsync(null),
        ["open_project"]    = () => _ = VM.OpenProjectCommand.ExecuteAsync(null),
        ["open_settings"]   = OpenSettings,
        ["remove_clip"]     = () => VM.RemoveSelectedClipCommand.Execute(null),

        ["set_start"]       = () => VM.TrimStart = Preview.Position,
        ["set_end"]         = () => VM.TrimEnd = Preview.Position,
        ["play_pause"]      = () => Preview.TogglePlayPause(),
        ["seek_backward"]   = () => Preview.Seek(-Settings.SettingsStore.Current.SeekSeconds),
        ["seek_forward"]    = () => Preview.Seek(Settings.SettingsStore.Current.SeekSeconds),
        ["prev_frame"]      = () => Preview.StepFrames(-1),
        ["next_frame"]      = () => Preview.StepFrames(1),
        ["prev_keyframe"]   = () => Preview.StepFrames(-(int)Preview.FrameRate),
        ["next_keyframe"]   = () => Preview.StepFrames((int)Preview.FrameRate),
    };

    private Dictionary<string, Action> _keymapActions = new();
    private bool _shortcutsHooked;

    /// <summary>현재 저장된 설정(기본값/사용자 재할당)에 맞춰 단축키 실행 매핑을 (재)구성.
    /// 설정창에서 단축키를 변경/저장하면 재시작 없이 다시 호출됨.</summary>
    private void SetupShortcuts()
    {
        _keymapActions = BuildKeymapActionMap();
        if (_shortcutsHooked) return;
        _shortcutsHooked = true;

        // KeyboardAccelerator는 포커스가 있는 컨트롤(Button/Slider/ListView 등)이 같은 키를
        // 자체적으로 처리(Space=클릭, 방향키=포커스 이동 등)하는 경우 무시되는 경우가 있어,
        // handledEventsToo로 항상 가로채는 라우티드 KeyDown을 사용해 신뢰성을 확보한다.
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnGlobalKeyDown), true);
    }

    private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (KeyComboText.IsModifierKey(e.Key)) return;

        var combo = KeyComboText.Format(e.Key, KeyComboText.CurrentModifiers());
        if (string.IsNullOrEmpty(combo)) return;

        // 파일 열기의 보조(레거시) 단축키. 사용자 재할당과 무관하게 항상 유지.
        if (combo.Equals("F2", StringComparison.OrdinalIgnoreCase))
        {
            _ = VM.OpenCommand.ExecuteAsync(null);
            e.Handled = true;
            return;
        }

        foreach (var def in KeymapActions.All)
        {
            if (!_keymapActions.TryGetValue(def.Id, out var handler)) continue;
            if (!string.Equals(KeymapActions.ResolveCombo(Settings.SettingsStore.Current, def.Id), combo, StringComparison.OrdinalIgnoreCase)) continue;
            handler();
            e.Handled = true;
            return;
        }
    }

    // ════════ 시작 → 출력 설정 → 실행 ════════
    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (VM.Segments.Count == 0)
        {
            await ShowMessageAsync(Loc.Get("dlg.notice"), Loc.Get("dlg.no_video_notice"));
            return;
        }
        var win = new OutputSettingsWindow(VM);
        OpenModalChild(win);
        win.Activate();
        if (await win.WaitAsync() && VM.RunComposeCommand.CanExecute(null))
            VM.RunComposeCommand.Execute(null);
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (VM.OpenCommand.CanExecute(null)) await VM.OpenCommand.ExecuteAsync(null);
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        var win = new SettingsWindow();
        OpenModalChild(win);
        win.Closed += (_, _) => { if (win.Saved) SetupShortcuts(); };
        win.Activate();
    }

    /// <summary>보조 창(시작/설정)이 열려 있는 동안 메인 화면 버튼 클릭을 막고,
    /// 메인 창이 먼저 닫히면 같이 닫히도록 함(Closed 핸들러에서 처리).</summary>
    private WindowBase? _modalChild;

    private void OpenModalChild(WindowBase win)
    {
        _modalChild = win;
        Root.IsHitTestVisible = false;
        win.Closed += (_, _) =>
        {
            _modalChild = null;
            Root.IsHitTestVisible = true;
        };
    }
    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Exit();

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); }
        catch { /* 재시작 실패 시 무시 */ }
        Application.Current.Exit();
    }

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
                Title = Loc.Get("dlg.missing_title"),
                Content = Loc.Get("dlg.missing_msg"),
                PrimaryButtonText = Loc.Get("dlg.remove_from_list"),
                CloseButtonText = Loc.Get("dlg.cancel"),
                XamlRoot = Root.XamlRoot,
                FontFamily = AppFontFamily,
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
        DispatcherQueue.TryEnqueue(() =>
        {
            _syncingListView = true;
            SegmentList.SelectedItems.Clear();
            foreach (var seg in VM.Segments.Where(s => s.IsSelected))
                SegmentList.SelectedItems.Add(seg);
            DispatcherQueue.TryEnqueue(() =>
            {
                VM.IsMovingItem = false;
                _syncingListView = false;
            });
        });
    }

    private void OnSegmentListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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
    }

    // 클릭 시 SelectionChanged는 PointerReleased 이후에 동기 발화 →
    // deferred lambda가 돌 때는 이미 SelectionChanged가 처리된 이후이므로 SelectedItems가 최신 상태
    private void OnSegmentListPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_preSelectionDrag == null || VM.IsMovingItem) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_preSelectionDrag == null || VM.IsMovingItem) return; // 드래그가 시작됐으면 DragItemsCompleted가 처리
            _preSelectionDrag = null;
            _syncingListView = true;
            foreach (var seg in VM.Segments)
                seg.IsSelected = SegmentList.SelectedItems.Contains(seg);
            _syncingListView = false;
        });
    }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        VM.IsMovingItem = true;
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var saved = _preSelectionDrag;
        _preSelectionDrag = null;

        VM.RegroupAfterDrag();
        VM.Renumber();

        if (saved is { Count: > 1 })
        {
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
            e.DragUIOverride.Caption = Loc.Get("drag.add_video");
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
        picker.FileTypeChoices.Add(Loc.Get("file.vcproj_type"), [".vcproj"]);
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
            CloseButtonText = Loc.Get("dlg.ok"),
            XamlRoot = Root.XamlRoot,
            FontFamily = AppFontFamily,
        };
        await dialog.ShowAsync();
    }

    private async Task<(bool confirmed, bool dontAskAgain)> ShowConfirmAsync(string title, string message)
    {
        var checkBox = new CheckBox
        {
            Content = Loc.Get("dlg.dont_ask"),
            Margin = new Microsoft.UI.Xaml.Thickness(0, 10, 0, 0),
        };
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(checkBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = Loc.Get("dlg.yes"),
            CloseButtonText = Loc.Get("dlg.no"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
            FontFamily = AppFontFamily,
        };
        var result = await dialog.ShowAsync();
        return (result == ContentDialogResult.Primary, checkBox.IsChecked == true);
    }
}
