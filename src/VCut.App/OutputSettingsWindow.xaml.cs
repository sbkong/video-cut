using Microsoft.UI.Xaml;
using System.IO;
using VCut.App.Locale;
using VCut.App.Settings;
using VCut.App.ViewModels;
using VCut.Core.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

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
        SldSpeed.Header         = Loc.Get("out.speed");
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
}
