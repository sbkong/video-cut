using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using VCut.App.Settings;
using VCut.Core;
using VCut.Core.FFmpeg;
using VCut.Core.Models;
using VCut.Core.Operations;

namespace VCut.App.ViewModels;

/// <summary>메인 윈도우의 상태와 명령. VCut.Core의 <see cref="VideoEditor"/>를 구동.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private VideoEditor? _editor;
    private CancellationTokenSource? _cts;

    /// <summary>View가 주입: 파일 열기 다이얼로그(다중 선택 가능).</summary>
    public Func<bool, Task<IReadOnlyList<string>>>? FilePicker { get; set; }

    /// <summary>View가 주입: 메시지 표시.</summary>
    public Func<string, string, Task>? ShowMessage { get; set; }

    /// <summary>View가 주입: 예/아니요 확인 다이얼로그. (confirmed, dontAskAgain) 반환.</summary>
    public Func<string, string, Task<(bool confirmed, bool dontAskAgain)>>? ShowConfirm { get; set; }

    /// <summary>View가 주입: 미리보기에 소스 로드.</summary>
    public Action<string, double>? LoadPreview { get; set; }

    /// <summary>View가 주입: 미리보기 초기화.</summary>
    public Action? ClearPreview { get; set; }

    /// <summary>View가 주입: 프로젝트 저장 경로 선택 다이얼로그. 기본 파일명 → 선택 경로 or null.</summary>
    public Func<string?, Task<string?>>? SaveProjectPicker { get; set; }

    /// <summary>View가 주입: 프로젝트 열기 경로 선택 다이얼로그. → 선택 경로 or null.</summary>
    public Func<Task<string?>>? OpenProjectPicker { get; set; }

    /// <summary>View가 주입: 화면 전환 ("trim"/"split"/"merge").</summary>
    public Action<string>? NavigateTo { get; set; }

    /// <summary>View가 주입: 이동 후 ListView 다중 선택 재동기화.</summary>
    public Action? RequestSelectionSync { get; set; }

    /// <summary>현재 화면 이름. View에서 ShowScreen 호출 시 업데이트.</summary>
    public string CurrentScreen { get; set; } = "trim";

    public MainViewModel()
    {
        try
        {
            _editor = VideoEditor.Create();
            StatusText = "준비됨 — " + _editor.FFmpegVersion;
        }
        catch (FFmpegException ex)
        {
            StatusText = "ffmpeg를 찾을 수 없습니다: " + ex.Message;
            EngineReady = false;
        }
        Segments.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
                Renumber();
            IsModified = true;
        };
        RefreshRecentProjects();
    }

    // ──────────────── 최근 프로젝트 ────────────────

    public System.Collections.ObjectModel.ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public bool HasRecentProjects => RecentProjects.Count > 0;

    [RelayCommand]
    private async Task OpenRecentProjectAsync(string path) =>
        await LoadProjectFromFileAsync(path);

    private void RefreshRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var p in SettingsStore.Current.RecentProjects.Take(8))
            RecentProjects.Add(new RecentProjectItem(p));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    public void RemoveRecentProject(string path)
    {
        var s = SettingsStore.Current;
        s.RecentProjects.Remove(path);
        SettingsStore.Save(s);
        RefreshRecentProjects();
    }

    // ──────────────── 공통 상태 ────────────────

    [ObservableProperty] private bool _engineReady = true;
    [ObservableProperty] private string? _sourcePath;
    [ObservableProperty] private string _mediaSummary = "동영상을 불러오세요.";
    [ObservableProperty] private bool _isFastMode = true;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _statusText = "준비됨";
    [ObservableProperty] private string _etaText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    public OutputMode CurrentMode => IsFastMode ? OutputMode.Fast : OutputMode.Convert;

    /// <summary>현재 영상 전체 길이(타임라인/구간 바인딩용).</summary>
    [ObservableProperty] private TimeSpan _mediaDuration;

    /// <summary>현재 영상 프레임레이트(타임코드 위젯용).</summary>
    [ObservableProperty] private double _frameRate = 30.0;

    /// <summary>미리보기 현재 재생 위치(타임라인 재생헤드용).</summary>
    [ObservableProperty] private TimeSpan _playPosition;

    // ──────────────── 자르기 구간 목록 ────────────────

    /// <summary>자르기 구간 목록(각 항목 = 파일 + 구간).</summary>
    public ObservableCollection<ClipSegment> Segments { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSegments))]
    [NotifyPropertyChangedFor(nameof(HasNoSegments))]
    [NotifyPropertyChangedFor(nameof(TotalDurationText))]
    private ClipSegment? _selectedSegment;

    public bool HasSegments => Segments.Count > 0;

    /// <summary>구간이 없을 때(빈 상태 + 표시용).</summary>
    public bool HasNoSegments => Segments.Count == 0;

    /// <summary>전체 구간 합계 길이(목록 헤더 표시용).</summary>
    public string TotalDurationText =>
        TimeSpan.FromSeconds(Segments.Sum(s => (s.End - s.Start).TotalSeconds)).ToString(@"hh\:mm\:ss\.ff");

    /// <summary>여러 구간을 하나로 합칠지(합치기 체크박스).</summary>
    [ObservableProperty] private bool _mergeEnabled;

    private bool _syncingSegment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrimDurationText))]
    private TimeSpan _trimStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrimDurationText))]
    private TimeSpan _trimEnd;

    public string TrimDurationText =>
        TrimEnd > TrimStart
            ? (TrimEnd - TrimStart).ToString(@"hh\:mm\:ss\.ff")
            : "00:00:00.00";

    /// <summary>미리보기 회전 각도 (0/90/180/270) — FFmpeg 미처리, 시각 변환만.</summary>
    [ObservableProperty] private int _videoRotation;
    [ObservableProperty] private bool _flipH;
    [ObservableProperty] private bool _flipV;
    [ObservableProperty] private bool _joinSegments;
    [ObservableProperty] private bool _extractAudioOnly;
    [ObservableProperty] private bool _removeAudio;

    /// <summary>합치기 시 구간별 누적 재생시간을 txt로 저장(유튜브 챕터용).</summary>
    [ObservableProperty] private bool _writePlaybackInfo;

    // ──────────────── 나누기 ────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SplitByTime))]
    private bool _splitByCount = true;
    [ObservableProperty] private double _splitCount = 2;
    [ObservableProperty] private double _splitSeconds = 60;

    /// <summary>시간 단위 분할 여부(개수 분할의 반대). 시간 입력 활성화 바인딩용.</summary>
    public bool SplitByTime => !SplitByCount;

    // ──────────────── 합치기 ────────────────

    public ObservableCollection<string> MergeFiles { get; } = [];

    // ──────────────── 일괄(배치) ────────────────

    public ObservableCollection<string> BatchFiles { get; } = [];

    /// <summary>0=변환, 1=mp3 추출, 2=오디오 제거, 3=배속</summary>
    [ObservableProperty] private int _batchOpIndex;

    // ──────────────── 배속 ────────────────

    /// <summary>재생 속도 배율(1.0=원본). 변환 모드 출력 설정에서 조정.</summary>
    [ObservableProperty] private double _speedFactor = 1.0;

    // ──────────────── 변환 ────────────────

    /// <summary>0=MP4,1=MKV,2=WebM,3=AVI</summary>
    [ObservableProperty] private int _containerIndex;
    [ObservableProperty] private double _quality = 80;
    [ObservableProperty] private double _resizeWidth;
    [ObservableProperty] private double _resizeHeight;

    // ════════════════ 프로젝트 저장 / 열기 ════════════════

    private string? _currentProjectPath;

    /// <summary>현재 열려 있는 프로젝트 파일 경로. null이면 새 프로젝트.</summary>
    public string? CurrentProjectPath
    {
        get => _currentProjectPath;
        private set
        {
            if (_currentProjectPath == value) return;
            _currentProjectPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentProjectName));
        }
    }

    /// <summary>미저장 변경이 있으면 true.</summary>
    public bool IsModified
    {
        get => _isModified;
        private set
        {
            if (_isModified == value) return;
            _isModified = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentProjectName));
        }
    }
    private bool _isModified;

    /// <summary>타이틀 바 표시 이름. 미저장 변경 시 * 접두사.</summary>
    public string CurrentProjectName =>
        (IsModified ? "* " : "")
        + (_currentProjectPath is not null
            ? Path.GetFileNameWithoutExtension(_currentProjectPath)
            : "새 프로젝트");

    /// <summary>저장: 기존 파일이면 덮어쓰기, 새 프로젝트면 탐색기 열기.</summary>
    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        var savePath = CurrentProjectPath ?? await PickSavePathAsync();
        if (savePath is null) return;
        await WriteProjectAsync(savePath);
    }

    /// <summary>다른 이름으로 저장: 항상 탐색기 열기.</summary>
    [RelayCommand]
    private async Task SaveProjectAsAsync()
    {
        var savePath = await PickSavePathAsync();
        if (savePath is null) return;
        await WriteProjectAsync(savePath);
    }

    private async Task<string?> PickSavePathAsync()
    {
        if (SaveProjectPicker is null) return null;
        var defaultName = CurrentProjectPath is not null
            ? Path.GetFileName(CurrentProjectPath)
            : Segments.Count > 0
                ? Path.GetFileNameWithoutExtension(Segments[0].FilePath) + ProjectFile.Extension
                : "project" + ProjectFile.Extension;
        return await SaveProjectPicker(defaultName);
    }

    private async Task WriteProjectAsync(string savePath)
    {
        var project = new VCutProject
        {
            FastMode = IsFastMode,
            JoinSegments = MergeEnabled,
            LastScreen = CurrentScreen,
            Clips = [.. Segments.Select(s => new ProjectClip
            {
                Path = s.FilePath,
                Ranges = [new ProjectRange { StartSeconds = s.Start.TotalSeconds, EndSeconds = s.End.TotalSeconds }],
            })],
            Settings = ProjectSettings.From(BuildSettings()),
        };
        try
        {
            await ProjectFile.SaveAsync(project, savePath);
            CurrentProjectPath = savePath;
            IsModified = false;
            AddToRecentProjects(savePath);
            StatusText = "프로젝트 저장: " + Path.GetFileName(savePath);
        }
        catch (Exception ex) { await Notify("저장 실패", ex.Message); }
    }

    /// <summary>새 프로젝트: 저장 위치 선택 → 빈 프로젝트 생성 → 편집 화면 이동.</summary>
    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (SaveProjectPicker is null) return;
        var path = await SaveProjectPicker("새 프로젝트" + ProjectFile.Extension);
        if (path is null) return;

        foreach (var seg in Segments) seg.RangeChanged -= OnSegmentRangeChanged;
        Segments.Clear();
        SelectedSegment = null;
        Renumber();

        await WriteProjectAsync(path);   // CurrentProjectPath, 최근 목록, StatusText 일괄 처리
        NavigateTo?.Invoke("trim");
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (OpenProjectPicker is null) return;
        var path = await OpenProjectPicker();
        if (path is not null) await LoadProjectFromFileAsync(path);
    }

    public async Task LoadProjectFromFileAsync(string path)
    {
        if (_editor is null) return;
        VCutProject project;
        try { project = await ProjectFile.LoadAsync(path); }
        catch (Exception ex) { await Notify("프로젝트 열기 실패", ex.Message); return; }

        foreach (var seg in Segments) seg.RangeChanged -= OnSegmentRangeChanged;
        Segments.Clear();

        IsFastMode = project.FastMode;
        MergeEnabled = project.JoinSegments;
        CurrentProjectPath = path;

        int total = project.Clips.Count;
        IsBusy = true;
        ProgressValue = 0;
        StatusText = total > 1 ? $"파일 분석 중… (0 / {total})" : "파일 분석 중…";
        NotifyCommands();
        _cts = new CancellationTokenSource();

        ClipSegment? first = null;
        try
        {
            int idx = 0;
            foreach (var clip in project.Clips)
            {
                _cts.Token.ThrowIfCancellationRequested();
                idx++;
                if (total > 1) StatusText = $"파일 분석 중… ({idx} / {total})";
                try
                {
                    var info = await _editor.ProbeAsync(clip.Path, _cts.Token);
                    var v = info.PrimaryVideo;
                    foreach (var range in clip.Ranges)
                    {
                        var seg = new ClipSegment(clip.Path, info.Duration, v?.FrameRate ?? 30.0)
                        {
                            Start = TimeSpan.FromSeconds(range.StartSeconds),
                            End = TimeSpan.FromSeconds(range.EndSeconds),
                        };
                        Segments.Add(seg);
                        seg.RangeChanged += OnSegmentRangeChanged;
                        _ = seg.LoadThumbnailAsync();
                        first ??= seg;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    foreach (var range in clip.Ranges)
                        Segments.Add(new ClipSegment(clip.Path, TimeSpan.Zero, 30.0, isMissing: true)
                        {
                            Start = TimeSpan.FromSeconds(range.StartSeconds),
                            End = TimeSpan.FromSeconds(range.EndSeconds),
                        });
                }
                ProgressValue = (double)idx / total * 100;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "취소됨";
            Renumber();
            return;
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            _cts?.Dispose();
            _cts = null;
            NotifyCommands();
        }

        Renumber();
        IsModified = false;
        if (first is not null) SelectedSegment = first;
        AddToRecentProjects(path);
        StatusText = $"프로젝트 열기: {Path.GetFileName(path)}  ({Segments.Count}개 구간)";
        NavigateTo?.Invoke(project.LastScreen);
    }

    private void AddToRecentProjects(string path)
    {
        var s = SettingsStore.Current;
        s.RecentProjects.Remove(path);
        s.RecentProjects.Insert(0, path);
        if (s.RecentProjects.Count > 10)
            s.RecentProjects.RemoveRange(10, s.RecentProjects.Count - 10);
        SettingsStore.Save(s);
        RefreshRecentProjects();
    }

    // ════════════════ 파일 열기 / 구간 목록 ════════════════

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (FilePicker is null) return;
        var files = await FilePicker(true);
        await AddClipsAsync(files);
    }

    /// <summary>'구간 추가(+)' — 파일을 골라 목록에 추가.</summary>
    [RelayCommand]
    private Task AddClipsCommandAsync() => OpenAsync();

    /// <summary>파일 경로들을 probe하여 구간 목록에 추가하고 첫 항목을 선택.</summary>
    public async Task AddClipsAsync(IReadOnlyList<string> paths)
    {
        if (_editor is null || paths.Count == 0) return;

        bool multi = paths.Count > 1;
        if (multi)
        {
            IsBusy = true;
            ProgressValue = 0;
            StatusText = $"파일 분석 중… (0 / {paths.Count})";
            NotifyCommands();
            _cts = new CancellationTokenSource();
        }

        ClipSegment? first = null;
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (multi)
                {
                    if (_cts!.Token.IsCancellationRequested) break;
                    StatusText = $"파일 분석 중… ({i + 1} / {paths.Count})";
                }
                try
                {
                    var ct = multi ? _cts!.Token : CancellationToken.None;
                    var info = await _editor.ProbeAsync(paths[i], ct);
                    var v = info.PrimaryVideo;
                    var seg = new ClipSegment(paths[i], info.Duration, v?.FrameRate ?? 30.0)
                    {
                        Start = TimeSpan.Zero,
                        End = info.Duration,
                    };
                    Segments.Add(seg);
                    seg.RangeChanged += OnSegmentRangeChanged;
                    _ = seg.LoadThumbnailAsync();
                    first ??= seg;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    await Notify("불러오기 실패", $"{Path.GetFileName(paths[i])}\n{ex.Message}");
                }
                if (multi) ProgressValue = (double)(i + 1) / paths.Count * 100;
            }
        }
        finally
        {
            if (multi)
            {
                IsBusy = false;
                ProgressValue = 0;
                _cts?.Dispose();
                _cts = null;
                NotifyCommands();
            }
        }

        Renumber();
        if (first is not null) SelectedSegment = first;
        StatusText = $"{Segments.Count}개 구간";
    }

    [RelayCommand]
    private void RemoveSelectedClip()
    {
        var toRemove = Segments.Where(s => s.IsSelected).ToList();
        if (toRemove.Count == 0) return;
        int firstIdx = Segments.IndexOf(toRemove[0]);
        foreach (var seg in toRemove)
        {
            seg.RangeChanged -= OnSegmentRangeChanged;
            Segments.Remove(seg);
        }
        Renumber();
        SelectedSegment = Segments.Count > 0 ? Segments[Math.Min(firstIdx, Segments.Count - 1)] : null;
        OnPropertyChanged(nameof(HasSegments)); OnPropertyChanged(nameof(HasNoSegments));
        OnPropertyChanged(nameof(TotalDurationText));
    }

    [RelayCommand]
    private void MoveClipUp() => Move(-1);

    [RelayCommand]
    private void MoveClipDown() => Move(1);

    internal bool IsMovingItem { get; set; }

    private void Move(int dir)
    {
        var selected = Segments.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return;

        IsMovingItem = true;

        if (dir < 0) // 위로: 위에서부터 처리
        {
            var ordered = selected.OrderBy(s => Segments.IndexOf(s)).ToList();
            if (Segments.IndexOf(ordered[0]) == 0) { IsMovingItem = false; return; }
            foreach (var seg in ordered)
            {
                int i = Segments.IndexOf(seg);
                Segments.Move(i, i - 1);
            }
        }
        else // 아래로: 아래서부터 처리
        {
            var ordered = selected.OrderByDescending(s => Segments.IndexOf(s)).ToList();
            if (Segments.IndexOf(ordered[0]) == Segments.Count - 1) { IsMovingItem = false; return; }
            foreach (var seg in ordered)
            {
                int i = Segments.IndexOf(seg);
                Segments.Move(i, i + 1);
            }
        }

        // IsMovingItem은 RequestSelectionSync에서 지연 해제 — ListView의 비동기 SelectionChanged를 차단
        Renumber();
        if (RequestSelectionSync is not null)
            RequestSelectionSync();
        else
            IsMovingItem = false;
    }

    internal void Renumber()
    {
        for (int i = 0; i < Segments.Count; i++) Segments[i].DisplayIndex = i + 1;
        OnPropertyChanged(nameof(HasSegments)); OnPropertyChanged(nameof(HasNoSegments));
        OnPropertyChanged(nameof(TotalDurationText));
        RunComposeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>선택 구간이 바뀌면 미리보기/타임라인을 그 구간으로 전환.</summary>
    partial void OnSelectedSegmentChanged(ClipSegment? value)
    {
        if (value is null)
        {
            SourcePath = null;
            MediaDuration = TimeSpan.Zero;
            MediaSummary = "동영상을 불러오세요.";
            TrimStart = TimeSpan.Zero;
            TrimEnd = TimeSpan.Zero;
            ClearPreview?.Invoke();
            return;
        }
        if (value.IsMissing)
        {
            SourcePath = null;
            MediaDuration = TimeSpan.Zero;
            MediaSummary = $"{value.FileName}  ·  파일 없음";
            TrimStart = TimeSpan.Zero;
            TrimEnd = TimeSpan.Zero;
            ClearPreview?.Invoke();
            NotifyCommands();
            return;
        }
        _syncingSegment = true;
        SourcePath = value.FilePath;
        MediaDuration = value.Duration;
        FrameRate = value.FrameRate;
        TrimStart = value.Start;
        TrimEnd = value.End;
        VideoRotation = value.VideoRotation;
        FlipH = value.FlipH;
        FlipV = value.FlipV;
        PlayPosition = TimeSpan.Zero;
        MediaSummary = $"{value.FileName}  ·  {value.Duration:hh\\:mm\\:ss\\.fff}";
        LoadPreview?.Invoke(value.FilePath, value.FrameRate);
        _syncingSegment = false;
        NotifyCommands();
    }

    /// <summary>미리보기 회전/반전 — FFmpeg 없이 시각 변환만. "none"/"90"/"hflip"/"vflip".</summary>
    [RelayCommand]
    private void ApplyTransform(string? param)
    {
        switch (param)
        {
            case "none":
                VideoRotation = 0; FlipH = false; FlipV = false;
                break;
            case "90":
                VideoRotation = (VideoRotation + 90) % 360;
                break;
            case "hflip":
                FlipH = !FlipH;
                break;
            case "vflip":
                FlipV = !FlipV;
                break;
        }
        if (SelectedSegment is not null)
        {
            SelectedSegment.VideoRotation = VideoRotation;
            SelectedSegment.FlipH = FlipH;
            SelectedSegment.FlipV = FlipV;
            IsModified = true;
        }
    }

    /// <summary>타임코드/타임라인으로 구간을 바꾸면 선택 구간 모델에 반영.</summary>
    partial void OnTrimStartChanged(TimeSpan value)
    {
        if (_syncingSegment) return;
        if (SelectedSegment is not null) SelectedSegment.Start = value;
        // TotalDurationText는 OnSegmentRangeChanged에서 Start 변경 시 이미 통지됨 — 중복 제거
        else OnPropertyChanged(nameof(TotalDurationText));
    }

    partial void OnTrimEndChanged(TimeSpan value)
    {
        if (_syncingSegment) return;
        if (SelectedSegment is not null) SelectedSegment.End = value;
        else OnPropertyChanged(nameof(TotalDurationText));
    }

    private void OnSegmentRangeChanged(ClipSegment _)
    {
        OnPropertyChanged(nameof(TotalDurationText));
        IsModified = true;
    }

    // ════════════════ 시작(구간 목록 실행) ════════════════

    private bool CanCompose() => EngineReady && !IsBusy && Segments.Count > 0;

    /// <summary>하단 [시작] — 구간 목록을 (합치기 여부에 따라) 실행.</summary>
    [RelayCommand(CanExecute = nameof(CanCompose))]
    private Task RunComposeAsync() => RunAsync(async (editor, settings, progress, ct) =>
    {
        settings.ExtractAudioOnly = ExtractAudioOnly;
        settings.RemoveAudio = RemoveAudio;
        var clips = Segments.Select(s =>
            (s.FilePath, new MediaRange(s.Start, s.End <= s.Start ? s.Duration : s.End))).ToList();
        return await editor.ComposeAsync(clips, MergeEnabled, CurrentMode, settings, OutputDir, progress, ct);
    });

    // ════════════════ 자르기 / 구간 제거 ════════════════

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunTrimAsync() => RunAsync(async (editor, settings, progress, ct) =>
    {
        var range = ParseRange();
        settings.ExtractAudioOnly = ExtractAudioOnly;
        settings.RemoveAudio = RemoveAudio;
        return await editor.TrimAsync(SourcePath!, [range], JoinSegments, CurrentMode, settings, OutputDir, progress, ct);
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunRemoveSegmentAsync() => RunAsync(async (editor, settings, progress, ct) =>
    {
        var range = ParseRange();
        return await editor.RemoveSegmentsAsync(SourcePath!, [range], CurrentMode, settings, OutputDir, progress, ct);
    });

    // ════════════════ 나누기 ════════════════

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunSplitAsync() => RunAsync(async (editor, settings, progress, ct) =>
    {
        return SplitByCount
            ? await editor.SplitAsync(SourcePath!, SplitMethod.ByCount, (int)SplitCount, TimeSpan.Zero,
                CurrentMode, settings, OutputDir, progress, ct)
            : await editor.SplitAsync(SourcePath!, SplitMethod.ByDuration, 0,
                TimeSpan.FromSeconds(SplitSeconds), CurrentMode, settings, OutputDir, progress, ct);
    });

    // ════════════════ 합치기 ════════════════

    [RelayCommand]
    private async Task AddMergeFilesAsync()
    {
        if (FilePicker is null) return;
        foreach (var f in await FilePicker(true))
            if (!MergeFiles.Contains(f)) MergeFiles.Add(f);
        RunMergeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveMergeFile(string? file)
    {
        if (file is not null) MergeFiles.Remove(file);
        RunMergeCommand.NotifyCanExecuteChanged();
    }

    private bool CanMerge() => EngineReady && !IsBusy && MergeFiles.Count >= 2;

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private Task RunMergeAsync() => RunAsync(async (editor, settings, progress, ct) =>
        await editor.MergeAsync([.. MergeFiles], CurrentMode, settings, null, progress, ct));

    // ════════════════ mp3 추출 ════════════════

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunMp3Async() => RunAsync(async (editor, _, progress, ct) =>
        await editor.ExtractAudioAsync(SourcePath!, null, 192, OutputDir, progress, ct));

    // ════════════════ 배속 ════════════════

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunSpeedAsync() => RunAsync(async (editor, settings, progress, ct) =>
        await editor.ChangeSpeedAsync(SourcePath!, SpeedFactor, null, settings, OutputDir, progress, ct));

    // ════════════════ 변환 / 용량 줄이기 ════════════════

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunConvertAsync() => RunAsync(async (editor, settings, progress, ct) =>
    {
        ApplyContainer(settings);
        settings.VideoQuality = (int)Quality;
        if (ResizeWidth > 0 && ResizeHeight > 0)
        {
            settings.SizeMode = VideoSizeMode.Fixed;
            settings.Width = (int)ResizeWidth;
            settings.Height = (int)ResizeHeight;
        }
        return await editor.ConvertAsync(SourcePath!, settings, null, OutputDir, progress, ct);
    });

    // ════════════════ 회전 / 반전 ════════════════

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunRotateAsync(string? param) => RunAsync(async (editor, settings, progress, ct) =>
    {
        var rot = param switch
        {
            "90" => Rotation.R90, "180" => Rotation.R180, "270" => Rotation.R270, _ => Rotation.None
        };
        bool h = param == "hflip", v = param == "vflip";
        return await editor.RotateFlipAsync(SourcePath!, rot, h, v, settings, null, OutputDir, progress, ct);
    });

    // ════════════════ 프레임 캡처 ════════════════

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CaptureFrameAsync()
    {
        if (_editor is null) return;
        IsBusy = true;
        NotifyCommands();
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _editor.CaptureFrameAsync(
                SourcePath!, PlayPosition,
                SettingsStore.Current.ResolveCaptureDir(SourcePath!),
                _cts.Token);
            if (result.Success)
            {
                StatusText = "캡처 완료";
                if (result.OutputFiles.Count > 0)
                {
                    var s = SettingsStore.Current;
                    bool open;
                    if (s.CaptureOpenFolderMode == OpenFolderMode.AlwaysOpen)
                        open = true;
                    else if (s.CaptureOpenFolderMode == OpenFolderMode.NeverOpen)
                        open = false;
                    else if (ShowConfirm is not null)
                    {
                        var (confirmed, dontAskAgain) = await ShowConfirm("캡처 완료", "저장 폴더를 여시겠습니까?");
                        open = confirmed;
                        if (dontAskAgain)
                        {
                            s.CaptureOpenFolderMode = confirmed
                                ? OpenFolderMode.AlwaysOpen
                                : OpenFolderMode.NeverOpen;
                            SettingsStore.Save(s);
                        }
                    }
                    else open = false;

                    if (open) ExplorerHelper.OpenAndSelect(result.OutputFiles[0]);
                }
            }
            else
            {
                StatusText = "캡처 실패: " + result.ErrorMessage;
                await Notify("캡처 실패", result.ErrorMessage + "\n\n" + (result.FFmpegLog ?? ""));
            }
        }
        catch (Exception ex)
        {
            StatusText = "오류: " + ex.Message;
            await Notify("오류", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            NotifyCommands();
        }
    }

    // ════════════════ 오디오 제거(고속) / remux ════════════════

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunMuteAsync() => RunAsync(async (editor, _, progress, ct) =>
        await editor.RemoveAudioAsync(SourcePath!, OutputDir, progress, ct));

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunPrepareAsync() => RunAsync(async (editor, _, progress, ct) =>
        await editor.PrepareForEditingAsync(SourcePath!, OutputDir, progress, ct));

    // ════════════════ 일괄(배치) ════════════════

    [RelayCommand]
    private async Task AddBatchFilesAsync()
    {
        if (FilePicker is null) return;
        foreach (var f in await FilePicker(true))
            if (!BatchFiles.Contains(f)) BatchFiles.Add(f);
        RunBatchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveBatchFile(string? file)
    {
        if (file is not null) BatchFiles.Remove(file);
        RunBatchCommand.NotifyCanExecuteChanged();
    }

    private bool CanBatch() => EngineReady && !IsBusy && BatchFiles.Count >= 1;

    [RelayCommand(CanExecute = nameof(CanBatch))]
    private async Task RunBatchAsync()
    {
        if (_editor is null) return;
        IsBusy = true;
        ProgressValue = 0;
        NotifyCommands();
        RunBatchCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();

        var progress = new Progress<ProgressInfo>(p =>
        {
            ProgressValue = p.Percent;
            EtaText = $"{p.Speed:0.0}x";
            StatusText = $"일괄 처리 중… {p.Percent}%";
        });

        try
        {
            var files = BatchFiles.ToArray();
            var settings = BuildSettings();
            IReadOnlyList<EditResult> results = BatchOpIndex switch
            {
                1 => await _editor.BatchExtractAudioAsync(files, 192, null, progress, _cts.Token),
                2 => await _editor.BatchRemoveAudioAsync(files, null, progress, _cts.Token),
                3 => await _editor.BatchChangeSpeedAsync(files, SpeedFactor, settings, null, progress, _cts.Token),
                _ => await _editor.BatchConvertAsync(files, settings, null, progress, _cts.Token),
            };
            int ok = results.Count(r => r.Success);
            ProgressValue = 100;
            StatusText = $"일괄 완료 — {ok}/{results.Count} 성공";
            var failed = results.Where(r => !r.Success).Select(r => r.ErrorMessage).ToList();
            await Notify("일괄 처리 완료",
                $"{ok}/{results.Count} 성공" + (failed.Count > 0 ? "\n\n실패:\n" + string.Join('\n', failed) : ""));
        }
        catch (Exception ex)
        {
            StatusText = "오류: " + ex.Message;
            await Notify("오류", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            NotifyCommands();
            RunBatchCommand.NotifyCanExecuteChanged();
        }
    }

    // ════════════════ 취소 ════════════════

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    // ════════════════ 실행 공통 ════════════════

    private bool CanRun() => EngineReady && !IsBusy && !string.IsNullOrEmpty(SourcePath);

    private async Task RunAsync(
        Func<VideoEditor, ConversionSettings, IProgress<ProgressInfo>, CancellationToken, Task<EditResult>> op)
    {
        if (_editor is null) return;
        IsBusy = true;
        ProgressValue = 0;
        EtaText = "";
        NotifyCommands();
        _cts = new CancellationTokenSource();

        var progress = new Progress<ProgressInfo>(p =>
        {
            ProgressValue = p.Percent;
            EtaText = p.Eta is { } e ? $"남은 시간 {e:mm\\:ss}  ·  {p.Speed:0.0}x" : $"{p.Speed:0.0}x";
            StatusText = $"처리 중… {p.Percent}%";
        });

        try
        {
            var settings = BuildSettings();
            var result = await op(_editor, settings, progress, _cts.Token);
            if (result.Success)
            {
                ProgressValue = 100;
                StatusText = $"완료 — {result.OutputFiles.Count}개 파일 ({result.Elapsed.TotalSeconds:0.0}초)";
                await OpenOutputFolderIfEnabledAsync(result.OutputFiles);
            }
            else
            {
                StatusText = "실패: " + result.ErrorMessage;
                await Notify("작업 실패", result.ErrorMessage + "\n\n" + (result.FFmpegLog ?? ""));
            }
        }
        catch (Exception ex)
        {
            StatusText = "오류: " + ex.Message;
            await Notify("오류", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            NotifyCommands();
        }
    }

    private ConversionSettings BuildSettings()
    {
        var s = new ConversionSettings();
        ApplyContainer(s);
        s.VideoQuality = (int)Quality;
        s.WritePlaybackInfo = WritePlaybackInfo;
        // 변환 모드에서만 배속 적용(고속 모드는 스트림 복사).
        if (!IsFastMode && Math.Abs(SpeedFactor - 1.0) > 0.001)
        {
            s.Speed = SpeedFactor;
            // docx 규칙: 4.01배 이상이면 오디오 자동 제거.
            if (SpeedFactor >= ConversionSettings.AudioDropSpeedThreshold) s.RemoveAudio = true;
        }
        // 환경설정 기본값 반영.
        var app = SettingsStore.Current;
        s.MoovAtFront = app.MoovAtFront;
        s.HardwareAccel = app.DefaultHardwareAccel;
        return s;
    }

    /// <summary>환경설정에 따른 출력 폴더(지정 폴더 또는 원본 위치).</summary>
    private string? OutputDir =>
        SourcePath is null ? null : SettingsStore.Current.ResolveOutputDir(SourcePath);

    private async Task OpenOutputFolderIfEnabledAsync(IReadOnlyList<string> outputs)
    {
        if (outputs.Count == 0) return;
        var s = SettingsStore.Current;
        bool open;
        if (s.OutputOpenFolderMode == OpenFolderMode.AlwaysOpen)
            open = true;
        else if (s.OutputOpenFolderMode == OpenFolderMode.NeverOpen)
            open = false;
        else if (ShowConfirm is not null)
        {
            var (confirmed, dontAskAgain) = await ShowConfirm("작업 완료", "저장 폴더를 여시겠습니까?");
            open = confirmed;
            if (dontAskAgain)
            {
                s.OutputOpenFolderMode = confirmed
                    ? OpenFolderMode.AlwaysOpen
                    : OpenFolderMode.NeverOpen;
                SettingsStore.Save(s);
            }
        }
        else open = false;

        if (open) ExplorerHelper.OpenAndSelect(outputs[0]);
    }

    private void ApplyContainer(ConversionSettings s)
    {
        s.Container = ContainerIndex switch
        {
            1 => ContainerFormat.Mkv,
            2 => ContainerFormat.WebM,
            3 => ContainerFormat.Avi,
            _ => ContainerFormat.Mp4,
        };
        if (s.Container == ContainerFormat.WebM)
        {
            s.VideoCodec = VideoCodec.Vp9;
            s.AudioCodec = AudioCodec.Opus;
        }
    }

    private MediaRange ParseRange()
    {
        var start = TrimStart;
        var end = TrimEnd;
        if (end <= start) end = MediaDuration > TimeSpan.Zero ? MediaDuration : start + TimeSpan.FromSeconds(1);
        return new MediaRange(start, end);
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsIdle));
        RunTrimCommand.NotifyCanExecuteChanged();
        RunRemoveSegmentCommand.NotifyCanExecuteChanged();
        RunSplitCommand.NotifyCanExecuteChanged();
        RunMergeCommand.NotifyCanExecuteChanged();
        RunMp3Command.NotifyCanExecuteChanged();
        RunSpeedCommand.NotifyCanExecuteChanged();
        RunConvertCommand.NotifyCanExecuteChanged();
        RunRotateCommand.NotifyCanExecuteChanged();
        CaptureFrameCommand.NotifyCanExecuteChanged();
        RunMuteCommand.NotifyCanExecuteChanged();
        RunPrepareCommand.NotifyCanExecuteChanged();
        RunComposeCommand.NotifyCanExecuteChanged();
    }

    private async Task Notify(string title, string msg)
    {
        if (ShowMessage is not null) await ShowMessage(title, msg);
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnSourcePathChanged(string? value) => NotifyCommands();
}
