using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VCut.App.ViewModels;
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
        VM.LoadPreview = (path, fps) =>
        {
            Preview.FrameRate = fps;
            Preview.SetSource(path);
        };
        Preview.PositionChanged += (s, pos) => VM.PlayPosition = pos;

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

    private async void OnTileTrim(object s, RoutedEventArgs e) { ShowScreen("trim"); await EnsureClipsAsync(); }
    private async void OnTileSplit(object s, RoutedEventArgs e) { ShowScreen("split"); await EnsureClipsAsync(); }
    private async void OnTileMerge(object s, RoutedEventArgs e) { ShowScreen("merge"); await EnsureClipsAsync(); }

    private async Task EnsureClipsAsync()
    {
        if (VM.Segments.Count == 0) await VM.OpenCommand.ExecuteAsync(null);
    }

    private void OnMenuClick(object s, RoutedEventArgs e) { /* 향후 메뉴 플라이아웃 */ }

    // ════════ 단축키 ════════
    private void SetupShortcuts()
    {
        AddAccel(VirtualKey.F2, VirtualKeyModifiers.None, () => _ = VM.OpenCommand.ExecuteAsync(null));
        AddAccel(VirtualKey.F5, VirtualKeyModifiers.None, OpenSettings);
        AddAccel(VirtualKey.O, VirtualKeyModifiers.Control, () => _ = VM.OpenCommand.ExecuteAsync(null));
        AddAccel(VirtualKey.S, VirtualKeyModifiers.Control, () => _ = VM.SaveProjectCommand.ExecuteAsync(null));
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

    private void OnTimelineSeek(object? sender, TimeSpan t) => Preview.Position = t;
    private void OnSetStartToCurrent(object sender, RoutedEventArgs e) => VM.TrimStart = Preview.Position;
    private void OnSetEndToCurrent(object sender, RoutedEventArgs e) => VM.TrimEnd = Preview.Position;

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        if (VM.CaptureFrameCommand.CanExecute(null)) VM.CaptureFrameCommand.Execute(null);
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
