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

    /// <summary>View가 주입: 미리보기에 소스 로드.</summary>
    public Action<string, double>? LoadPreview { get; set; }

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

    [ObservableProperty] private TimeSpan _trimStart;
    [ObservableProperty] private TimeSpan _trimEnd;
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
        ClipSegment? first = null;
        foreach (var path in paths)
        {
            try
            {
                var info = await _editor.ProbeAsync(path);
                var v = info.PrimaryVideo;
                var seg = new ClipSegment(path, info.Duration, v?.FrameRate ?? 30.0)
                {
                    Start = TimeSpan.Zero,
                    End = info.Duration,
                };
                Segments.Add(seg);
                seg.PropertyChanged += OnSegmentRangeChanged;
                first ??= seg;
            }
            catch (Exception ex)
            {
                await Notify("불러오기 실패", $"{Path.GetFileName(path)}\n{ex.Message}");
            }
        }
        Renumber();
        if (first is not null) SelectedSegment = first;
        StatusText = $"{Segments.Count}개 구간";
    }

    [RelayCommand]
    private void RemoveSelectedClip()
    {
        if (SelectedSegment is null) return;
        int idx = Segments.IndexOf(SelectedSegment);
        SelectedSegment.PropertyChanged -= OnSegmentRangeChanged;
        Segments.Remove(SelectedSegment);
        Renumber();
        SelectedSegment = Segments.Count > 0 ? Segments[Math.Min(idx, Segments.Count - 1)] : null;
        OnPropertyChanged(nameof(HasSegments)); OnPropertyChanged(nameof(HasNoSegments));
        OnPropertyChanged(nameof(TotalDurationText));
    }

    [RelayCommand]
    private void MoveClipUp() => Move(-1);

    [RelayCommand]
    private void MoveClipDown() => Move(1);

    private void Move(int dir)
    {
        if (SelectedSegment is null) return;
        int i = Segments.IndexOf(SelectedSegment);
        int j = i + dir;
        if (j < 0 || j >= Segments.Count) return;
        Segments.Move(i, j);
        Renumber();
    }

    private void Renumber()
    {
        for (int i = 0; i < Segments.Count; i++) Segments[i].DisplayIndex = i + 1;
        OnPropertyChanged(nameof(HasSegments)); OnPropertyChanged(nameof(HasNoSegments));
        OnPropertyChanged(nameof(TotalDurationText));
        RunComposeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>선택 구간이 바뀌면 미리보기/타임라인을 그 구간으로 전환.</summary>
    partial void OnSelectedSegmentChanged(ClipSegment? value)
    {
        if (value is null) return;
        _syncingSegment = true;
        SourcePath = value.FilePath;
        MediaDuration = value.Duration;
        FrameRate = value.FrameRate;
        TrimStart = value.Start;
        TrimEnd = value.End;
        PlayPosition = TimeSpan.Zero;
        MediaSummary = $"{value.FileName}  ·  {value.Duration:hh\\:mm\\:ss\\.fff}";
        LoadPreview?.Invoke(value.FilePath, value.FrameRate);
        _syncingSegment = false;
        NotifyCommands();
    }

    /// <summary>타임코드/타임라인으로 구간을 바꾸면 선택 구간 모델에 반영.</summary>
    partial void OnTrimStartChanged(TimeSpan value)
    {
        if (!_syncingSegment && SelectedSegment is not null) SelectedSegment.Start = value;
        OnPropertyChanged(nameof(TotalDurationText));
    }

    partial void OnTrimEndChanged(TimeSpan value)
    {
        if (!_syncingSegment && SelectedSegment is not null) SelectedSegment.End = value;
        OnPropertyChanged(nameof(TotalDurationText));
    }

    private void OnSegmentRangeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClipSegment.Start) or nameof(ClipSegment.End))
            OnPropertyChanged(nameof(TotalDurationText));
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
    private Task CaptureFrameAsync() => RunAsync(async (editor, _, _, ct) =>
        await editor.CaptureFrameAsync(SourcePath!, PlayPosition, null, ct));

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

    // ════════════════ 프로젝트 저장 / 열기 ════════════════

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (Segments.Count == 0) return;
        try
        {
            var project = new VCutProject
            {
                FastMode = IsFastMode,
                JoinSegments = MergeEnabled,
                Clips = [.. Segments.Select(s => new ProjectClip
                {
                    Path = s.FilePath,
                    Ranges = [ProjectRange.From(new MediaRange(s.Start, s.End))],
                })],
                Settings = ProjectSettings.From(BuildSettings()),
            };
            var dir = Path.GetDirectoryName(Segments[0].FilePath) ?? Directory.GetCurrentDirectory();
            var path = Path.Combine(dir,
                Path.GetFileNameWithoutExtension(Segments[0].FilePath) + ProjectFile.Extension);
            await ProjectFile.SaveAsync(project, path);
            StatusText = "프로젝트 저장: " + Path.GetFileName(path);
            await Notify("프로젝트 저장", path);
        }
        catch (Exception ex) { await Notify("프로젝트 저장 실패", ex.Message); }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (FilePicker is null) return;
        var files = await FilePicker(false);
        if (files.Count == 0) return;
        try
        {
            var project = await ProjectFile.LoadAsync(files[0]);
            IsFastMode = project.FastMode;
            MergeEnabled = project.JoinSegments;
            ApplyProjectSettings(project.Settings);

            // 기존 목록 비우고 프로젝트 클립을 로드.
            foreach (var s in Segments) s.PropertyChanged -= OnSegmentRangeChanged;
            Segments.Clear();
            foreach (var clip in project.Clips)
                await AddClipsAsync([clip.Path]);
            // 저장된 구간 범위 적용.
            for (int i = 0; i < project.Clips.Count && i < Segments.Count; i++)
            {
                if (project.Clips[i].Ranges.Count > 0)
                {
                    var r = project.Clips[i].Ranges[0].ToMediaRange();
                    Segments[i].Start = r.Start;
                    Segments[i].End = r.End;
                }
            }
            if (Segments.Count > 0) SelectedSegment = Segments[0];
            StatusText = "프로젝트 열기 완료";
        }
        catch (Exception ex) { await Notify("프로젝트 열기 실패", ex.Message); }
    }

    private void ApplyProjectSettings(ProjectSettings s)
    {
        ContainerIndex = s.Container switch
        {
            ContainerFormat.Mkv => 1,
            ContainerFormat.WebM => 2,
            ContainerFormat.Avi => 3,
            _ => 0,
        };
        Quality = s.Quality;
        ResizeWidth = s.Width;
        ResizeHeight = s.Height;
        SpeedFactor = s.Speed > 0 ? s.Speed : 1.0;
        WritePlaybackInfo = s.WritePlaybackInfo;
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
                OpenOutputFolderIfEnabled(result.OutputFiles);
                await Notify("작업 완료", string.Join('\n', result.OutputFiles));
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

    private void OpenOutputFolderIfEnabled(IReadOnlyList<string> outputs)
    {
        if (!SettingsStore.Current.OpenFolderAfterDone || outputs.Count == 0) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{outputs[0]}\"")
            { UseShellExecute = true });
        }
        catch { /* 탐색기 열기 실패 무시 */ }
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
