using System.Diagnostics;
using VCut.Core.FFmpeg;
using VCut.Core.Models;
using VCut.Core.Operations;

namespace VCut.Core;

/// <summary>
/// v-cut 동영상 편집 엔진의 진입점(파사드). docx '프로그램 사용방법'의 모든 기능을 메서드로 노출.
/// 외부 .NET 프로그램은 이 클래스만 참조하면 자르기·합치기·변환 등을 그대로 사용할 수 있음.
/// </summary>
public sealed class VideoEditor
{
    private readonly FFmpegLocator _locator;
    private readonly IFFmpegRunner _runner;
    private readonly FFprobeService _probe;
    private readonly ConcatHelper _concat;

    /// <summary>임시 작업 파일이 저장될 폴더. 기본값은 시스템 임시 폴더 하위 vcut.</summary>
    public string TempDirectory { get; set; }

    public VideoEditor(FFmpegLocator locator)
    {
        _locator = locator;
        _runner = new FFmpegRunner(locator);
        _probe = new FFprobeService(locator);
        _concat = new ConcatHelper(_runner);
        TempDirectory = Path.Combine(Path.GetTempPath(), "vcut");
    }

    /// <summary>ffmpeg/ffprobe를 자동 탐색해 생성. 실패 시 <see cref="FFmpegException"/>.</summary>
    public static VideoEditor Create(string? ffmpegDirectory = null) =>
        new(FFmpegLocator.Create(ffmpegDirectory));

    /// <summary>동영상 파일을 분석해 미디어 정보를 반환.</summary>
    public Task<MediaInfo> ProbeAsync(string filePath, CancellationToken ct = default) =>
        _probe.ProbeAsync(filePath, ct);

    public string FFmpegVersion => _locator.GetVersion();

    // ════════════════════════════ 1. 자르기 ════════════════════════════

    /// <summary>
    /// 동영상에서 구간(들)을 잘라냄. docx '동영상 자르기' / 여러 구간 + 합치기 옵션.
    /// </summary>
    /// <param name="join">true면 모든 구간을 하나로 합쳐 단일 파일 생성(구간 합치기), false면 구간별 개별 파일.</param>
    public async Task<EditResult> TrimAsync(
        string input,
        IReadOnlyList<MediaRange> ranges,
        bool join,
        OutputMode mode,
        ConversionSettings settings,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        if (ranges.Count == 0)
            return EditResult.Fail("자를 구간이 지정되지 않았습니다.");

        var sw = Stopwatch.StartNew();
        try
        {
            // 단일 구간(또는 합치기 안 함) → 구간별로 바로 최종 파일 출력.
            if (!join || ranges.Count == 1)
            {
                var outputs = new List<string>();
                var grandTotal = TimeSpan.FromSeconds(ranges.Sum(r => r.Duration.TotalSeconds));
                double done = 0;
                for (int i = 0; i < ranges.Count; i++)
                {
                    var ext = settings.ContainerExtension;
                    var outPath = ranges.Count == 1
                        ? OutputNaming.Derive(input, "cut", ext, outputDir)
                        : OutputNaming.DeriveIndexed(input, "cut", i + 1, ranges.Count, ext, outputDir);

                    var span = ranges[i].Duration.TotalSeconds / Math.Max(grandTotal.TotalSeconds, 0.001);
                    var scaler = new ProgressScaler(progress, done, span, grandTotal,
                        done * grandTotal.TotalSeconds);
                    done += span;

                    var args = FFmpegArgsBuilder.BuildTranscode(input, ranges[i], mode, settings, outPath);
                    await _runner.RunAsync(args, ranges[i].Duration, scaler, ct).ConfigureAwait(false);
                    outputs.Add(outPath);
                }
                Report(progress, 1.0);
                return EditResult.Ok(outputs, sw.Elapsed);
            }

            // 여러 구간 합치기 → 임시 세그먼트 생성 후 concat.
            var output = OutputNaming.Derive(input, "edited", settings.ContainerExtension, outputDir);
            await TrimJoinAsync(input, ranges, mode, settings, output, progress, ct).ConfigureAwait(false);
            return EditResult.Ok(output, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed);
        }
        catch (FFmpegException ex)
        {
            return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed);
        }
    }

    private async Task TrimJoinAsync(
        string input, IReadOnlyList<MediaRange> ranges, OutputMode mode,
        ConversionSettings settings, string output,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var work = CreateWorkDir();
        try
        {
            var segments = await ProduceSegmentsAsync(
                input, ranges, mode, settings, work,
                new ProgressScaler(progress, 0.0, 0.85), ct).ConfigureAwait(false);

            var joinTotal = TimeSpan.FromSeconds(ranges.Sum(r => r.Duration.TotalSeconds));
            // 세그먼트는 동일 설정으로 생성되어 코덱이 일치 → 스트림 복사로 합치기.
            await _concat.ConcatFastAsync(
                segments, output, settings, joinTotal,
                new ProgressScaler(progress, 0.85, 0.15), ct).ConfigureAwait(false);

            // 재생 시간 정보(txt) 저장 — 각 구간을 챕터로.
            if (settings.WritePlaybackInfo)
            {
                var chapters = ranges.Select((r, i) => ($"구간 {i + 1}", r.Duration)).ToList();
                await WritePlaybackInfoAsync(chapters, Path.ChangeExtension(output, ".txt"), ct)
                    .ConfigureAwait(false);
            }
            Report(progress, 1.0);
        }
        finally { TryDeleteDir(work); }
    }

    /// <summary>각 구간을 임시 폴더에 개별 세그먼트로 생성하고 경로 목록을 반환.</summary>
    private async Task<List<string>> ProduceSegmentsAsync(
        string input, IReadOnlyList<MediaRange> ranges, OutputMode mode,
        ConversionSettings settings, string workDir,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var segments = new List<string>();
        var grandTotal = TimeSpan.FromSeconds(ranges.Sum(r => r.Duration.TotalSeconds));
        double done = 0;
        var ext = settings.ContainerExtension;

        for (int i = 0; i < ranges.Count; i++)
        {
            var seg = Path.Combine(workDir, $"seg_{i:D4}{ext}");
            var span = ranges[i].Duration.TotalSeconds / Math.Max(grandTotal.TotalSeconds, 0.001);
            var scaler = new ProgressScaler(progress, done, span);
            done += span;

            var args = FFmpegArgsBuilder.BuildTranscode(input, ranges[i], mode, settings, seg);
            await _runner.RunAsync(args, ranges[i].Duration, scaler, ct).ConfigureAwait(false);
            segments.Add(seg);
        }
        return segments;
    }

    // ════════════════════════════ 2. 구간 제거 ════════════════════════════

    /// <summary>
    /// 동영상에서 원하지 않는 구간(광고 등)을 제거. docx '구간 삭제' — 삭제할 구간이 아니라
    /// 남길 앞·뒤 구간을 합치는 방식. 제거할 구간들을 받아 그 보집합(남길 구간)을 계산 후 합침.
    /// </summary>
    public async Task<EditResult> RemoveSegmentsAsync(
        string input,
        IReadOnlyList<MediaRange> removeRanges,
        OutputMode mode,
        ConversionSettings settings,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        if (removeRanges.Count == 0)
            return EditResult.Fail("제거할 구간이 지정되지 않았습니다.");

        MediaInfo info;
        try { info = await ProbeAsync(input, ct).ConfigureAwait(false); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr); }

        var keep = ComputeKeepRanges(removeRanges, info.Duration);
        if (keep.Count == 0)
            return EditResult.Fail("제거 후 남는 구간이 없습니다.");

        // 남길 구간이 하나뿐이면 그대로 자르기, 여러 개면 합치기.
        return await TrimAsync(input, keep, join: keep.Count > 1, mode, settings, outputDir, progress, ct)
            .ConfigureAwait(false);
    }

    /// <summary>제거 구간의 보집합(남길 구간) 계산.</summary>
    internal static List<MediaRange> ComputeKeepRanges(IReadOnlyList<MediaRange> remove, TimeSpan total)
    {
        var sorted = remove.OrderBy(r => r.Start).ToList();
        var keep = new List<MediaRange>();
        var cursor = TimeSpan.Zero;
        foreach (var r in sorted)
        {
            var start = r.Start < cursor ? cursor : r.Start;
            if (start > cursor)
                keep.Add(new MediaRange(cursor, start));
            if (r.End > cursor) cursor = r.End;
        }
        if (cursor < total - TimeSpan.FromMilliseconds(50))
            keep.Add(new MediaRange(cursor, total));
        return keep;
    }

    // ════════════════════════════ 3. 나누기 ════════════════════════════

    /// <summary>
    /// 하나의 동영상을 동일한 길이의 여러 파일로 분할. docx '동영상 나누기'(개수/시간 단위).
    /// </summary>
    /// <param name="count">ByCount일 때 분할 개수(2~99).</param>
    /// <param name="segmentLength">ByDuration일 때 각 조각의 길이.</param>
    public async Task<EditResult> SplitAsync(
        string input,
        SplitMethod method,
        int count,
        TimeSpan segmentLength,
        OutputMode mode,
        ConversionSettings settings,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        MediaInfo info;
        try { info = await ProbeAsync(input, ct).ConfigureAwait(false); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr); }

        var ranges = BuildSplitRanges(method, count, segmentLength, info.Duration);
        if (ranges.Count == 0)
            return EditResult.Fail("분할 구간을 계산할 수 없습니다. 개수/시간 설정을 확인하세요.");

        try
        {
            var outputs = new List<string>();
            var grandTotal = info.Duration;
            double doneSec = 0;
            var ext = settings.ContainerExtension;
            for (int i = 0; i < ranges.Count; i++)
            {
                var outPath = OutputNaming.DeriveIndexed(input, "part", i + 1, ranges.Count, ext, outputDir);
                double span = ranges[i].Duration.TotalSeconds / Math.Max(grandTotal.TotalSeconds, 0.001);
                var scaler = new ProgressScaler(progress,
                    doneSec / grandTotal.TotalSeconds, span, grandTotal, doneSec);
                doneSec += ranges[i].Duration.TotalSeconds;

                var args = FFmpegArgsBuilder.BuildTranscode(input, ranges[i], mode, settings, outPath);
                await _runner.RunAsync(args, ranges[i].Duration, scaler, ct).ConfigureAwait(false);
                outputs.Add(outPath);
            }
            Report(progress, 1.0);
            return EditResult.Ok(outputs, sw.Elapsed);
        }
        catch (OperationCanceledException) { return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed); }
    }

    internal static List<MediaRange> BuildSplitRanges(
        SplitMethod method, int count, TimeSpan segmentLength, TimeSpan total)
    {
        var ranges = new List<MediaRange>();
        if (total <= TimeSpan.Zero) return ranges;

        if (method == SplitMethod.ByCount)
        {
            count = Math.Clamp(count, 2, 99);
            var each = TimeSpan.FromSeconds(total.TotalSeconds / count);
            for (int i = 0; i < count; i++)
            {
                var start = TimeSpan.FromSeconds(each.TotalSeconds * i);
                var end = i == count - 1 ? total : TimeSpan.FromSeconds(each.TotalSeconds * (i + 1));
                if (end > start) ranges.Add(new MediaRange(start, end));
            }
        }
        else
        {
            if (segmentLength <= TimeSpan.Zero) return ranges;
            var cursor = TimeSpan.Zero;
            while (cursor < total - TimeSpan.FromMilliseconds(100))
            {
                var end = cursor + segmentLength;
                if (end > total) end = total;
                ranges.Add(new MediaRange(cursor, end));
                cursor = end;
            }
        }
        return ranges;
    }

    // ════════════════════════════ 4. 합치기 ════════════════════════════

    /// <summary>
    /// 여러 동영상 파일을 하나로 합침. docx '동영상 합치기'.
    /// 고속 모드는 모든 파일의 코덱/해상도/FPS가 동일해야 함. 실패하거나 다르면 변환 모드 권장.
    /// </summary>
    public async Task<EditResult> MergeAsync(
        IReadOnlyList<string> inputs,
        OutputMode mode,
        ConversionSettings settings,
        string? outputPath = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        if (inputs.Count < 2)
            return EditResult.Fail("합치려면 2개 이상의 파일이 필요합니다.");

        var sw = Stopwatch.StartNew();
        TimeSpan total = TimeSpan.Zero;
        bool allHaveAudio = true;
        var chapters = new List<(string, TimeSpan)>();
        try
        {
            foreach (var f in inputs)
            {
                var mi = await ProbeAsync(f, ct).ConfigureAwait(false);
                total += mi.Duration;
                if (mi.AudioStreams.Count == 0) allHaveAudio = false;
                chapters.Add((Path.GetFileNameWithoutExtension(f), mi.Duration));
            }
        }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr); }

        var output = outputPath ?? OutputNaming.Derive(inputs[0], "merged", settings.ContainerExtension);

        async Task MaybeWriteInfoAsync()
        {
            if (settings.WritePlaybackInfo)
                await WritePlaybackInfoAsync(chapters, Path.ChangeExtension(output, ".txt"), ct)
                    .ConfigureAwait(false);
        }

        try
        {
            if (mode == OutputMode.Fast && !settings.RequiresReencode)
            {
                try
                {
                    await _concat.ConcatFastAsync(inputs, output, settings, total, progress, ct)
                        .ConfigureAwait(false);
                    await MaybeWriteInfoAsync().ConfigureAwait(false);
                    Report(progress, 1.0);
                    return EditResult.Ok(output, sw.Elapsed);
                }
                catch (FFmpegException)
                {
                    // 고속 합치기 실패 → 변환 모드로 자동 폴백(docx: 형식이 다르면 변환 모드).
                }
            }

            await _concat.ConcatReencodeAsync(inputs, output, settings, allHaveAudio, total, progress, ct)
                .ConfigureAwait(false);
            await MaybeWriteInfoAsync().ConfigureAwait(false);
            Report(progress, 1.0);
            return EditResult.Ok(output, sw.Elapsed);
        }
        catch (OperationCanceledException) { return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed); }
    }

    // ════════════════════════════ 4-b. 합성(구간 목록 실행) ════════════════════════════

    /// <summary>
    /// '자르기 구간 목록'을 실행. 각 항목(파일+구간)을 잘라서, join=true면 하나로 합치고
    /// join=false면 항목별 파일로 내보냄. 단일/다중 파일, 자르기/합치기/구간제거를 모두 포괄.
    /// </summary>
    public async Task<EditResult> ComposeAsync(
        IReadOnlyList<(string File, MediaRange Range, double Speed)> clips,
        bool join,
        OutputMode mode,
        ConversionSettings settings,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        if (clips.Count == 0)
            return EditResult.Fail("실행할 구간이 없습니다.");

        var sw = Stopwatch.StartNew();
        var ext = settings.ContainerExtension;
        var grandTotal = TimeSpan.FromSeconds(clips.Sum(c => c.Range.Duration.TotalSeconds));

        try
        {
            // 합치기 안 함 → 항목별 최종 파일.
            if (!join || clips.Count == 1)
            {
                var outputs = new List<string>();
                double done = 0;
                for (int i = 0; i < clips.Count; i++)
                {
                    var (file, range, speed) = clips[i];
                    var outPath = clips.Count == 1
                        ? OutputNaming.Derive(file, "cut", ext, outputDir)
                        : OutputNaming.DeriveIndexed(file, "cut", i + 1, clips.Count, ext, outputDir);
                    double span = range.Duration.TotalSeconds / Math.Max(grandTotal.TotalSeconds, 0.001);
                    var scaler = new ProgressScaler(progress, done, span, grandTotal, done * grandTotal.TotalSeconds);
                    done += span;
                    var args = FFmpegArgsBuilder.BuildTranscode(file, range, mode, ClipSettings(settings, speed), outPath);
                    await _runner.RunAsync(args, range.Duration, scaler, ct).ConfigureAwait(false);
                    outputs.Add(outPath);
                }
                Report(progress, 1.0);
                return EditResult.Ok(outputs, sw.Elapsed);
            }

            // 합치기 → 임시 세그먼트 생성 후 concat.
            var output = OutputNaming.Derive(clips[0].File, "edited", ext, outputDir);
            var work = CreateWorkDir();
            try
            {
                var segments = new List<string>();
                double done = 0;
                for (int i = 0; i < clips.Count; i++)
                {
                    var (file, range, speed) = clips[i];
                    var seg = Path.Combine(work, $"seg_{i:D4}{ext}");
                    double span = (range.Duration.TotalSeconds / Math.Max(grandTotal.TotalSeconds, 0.001)) * 0.85;
                    var scaler = new ProgressScaler(progress, done, span);
                    done += span;
                    var args = FFmpegArgsBuilder.BuildTranscode(file, range, mode, ClipSettings(settings, speed), seg);
                    await _runner.RunAsync(args, range.Duration, scaler, ct).ConfigureAwait(false);
                    segments.Add(seg);
                }

                await _concat.ConcatFastAsync(segments, output, settings, grandTotal,
                    new ProgressScaler(progress, 0.85, 0.15), ct).ConfigureAwait(false);

                if (settings.WritePlaybackInfo)
                {
                    var chapters = clips.Select((c, i) =>
                        ($"{i + 1}. {Path.GetFileNameWithoutExtension(c.File)}", c.Range.Duration)).ToList();
                    await WritePlaybackInfoAsync(chapters, Path.ChangeExtension(output, ".txt"), ct)
                        .ConfigureAwait(false);
                }
                Report(progress, 1.0);
                return EditResult.Ok(output, sw.Elapsed);
            }
            finally { TryDeleteDir(work); }
        }
        catch (OperationCanceledException) { return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed); }
    }

    /// <summary>클립별 배속을 반영한 설정 복제. 4.01배속 이상이면 오디오 자동 제거.</summary>
    private static ConversionSettings ClipSettings(ConversionSettings settings, double speed)
    {
        if (Math.Abs(speed - settings.Speed) < 0.001) return settings;
        var clone = settings.Clone();
        clone.Speed = speed;
        if (speed >= ConversionSettings.AudioDropSpeedThreshold) clone.RemoveAudio = true;
        return clone;
    }

    // ════════════════════════════ 5. mp3 추출 ════════════════════════════

    /// <summary>동영상에서 오디오를 mp3로 추출. 구간을 지정하면 해당 구간만. docx 'mp3 추출하기'.</summary>
    public async Task<EditResult> ExtractAudioAsync(
        string input,
        MediaRange? range = null,
        int bitrateKbps = 192,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default,
        int? audioStreamIndex = null)
    {
        var sw = Stopwatch.StartNew();
        var settings = new ConversionSettings
        {
            ExtractAudioOnly = true,
            AudioCodec = AudioCodec.Mp3,
            AudioBitrateKbps = bitrateKbps,
            SelectedAudioStreamIndex = audioStreamIndex,
        };
        var output = OutputNaming.Derive(input, "audio", ".mp3", outputDir);
        TimeSpan dur;
        try
        {
            dur = range?.Duration ?? (await ProbeAsync(input, ct).ConfigureAwait(false)).Duration;
        }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr); }

        try
        {
            // 추출은 항상 인코딩 경로 사용(변환 모드).
            var args = FFmpegArgsBuilder.BuildTranscode(input, range, OutputMode.Convert, settings, output);
            await _runner.RunAsync(args, dur, progress, ct).ConfigureAwait(false);
            Report(progress, 1.0);
            return EditResult.Ok(output, sw.Elapsed);
        }
        catch (OperationCanceledException) { return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed); }
    }

    // ════════════════════════════ 6. 배속 ════════════════════════════

    /// <summary>
    /// 재생 속도 조절(0.1~99.9배). docx '재생 속도 조절'. 4.01배속 이상이면 오디오가 자동 제거됨.
    /// </summary>
    public async Task<EditResult> ChangeSpeedAsync(
        string input,
        double speed,
        MediaRange? range,
        ConversionSettings settings,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        speed = Math.Clamp(speed, 0.1, 99.9);
        var s = settings.Clone();
        s.Speed = speed;
        // docx 규칙: 4.01배 이상이면 오디오 트랙 자동 제거.
        if (speed >= ConversionSettings.AudioDropSpeedThreshold)
            s.RemoveAudio = true;

        var sw = Stopwatch.StartNew();
        var output = OutputNaming.Derive(input, "speed", s.ContainerExtension, outputDir);
        TimeSpan srcDur;
        try { srcDur = range?.Duration ?? (await ProbeAsync(input, ct).ConfigureAwait(false)).Duration; }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr); }

        // 출력 길이는 원본/배속. 진행률 계산용.
        var outDur = TimeSpan.FromSeconds(srcDur.TotalSeconds / speed);
        try
        {
            var args = FFmpegArgsBuilder.BuildTranscode(input, range, OutputMode.Convert, s, output);
            await _runner.RunAsync(args, outDur, progress, ct).ConfigureAwait(false);
            Report(progress, 1.0);
            return EditResult.Ok(output, sw.Elapsed);
        }
        catch (OperationCanceledException) { return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed); }
    }

    // ════════════════════════════ 7. 변환 / 용량 줄이기 ════════════════════════════

    /// <summary>
    /// 포맷·코덱·해상도·비트레이트 변환. docx '동영상 변환하기' / '용량 줄이기'(해상도·품질·FPS 조정).
    /// 구간을 지정하면 해당 구간만 변환.
    /// </summary>
    public async Task<EditResult> ConvertAsync(
        string input,
        ConversionSettings settings,
        MediaRange? range = null,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var output = OutputNaming.Derive(input, "converted", settings.ContainerExtension, outputDir);
        TimeSpan dur;
        try { dur = range?.Duration ?? (await ProbeAsync(input, ct).ConfigureAwait(false)).Duration; }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr); }

        try
        {
            var args = FFmpegArgsBuilder.BuildTranscode(input, range, OutputMode.Convert, settings, output);
            await _runner.RunAsync(args, dur, progress, ct).ConfigureAwait(false);
            Report(progress, 1.0);
            return EditResult.Ok(output, sw.Elapsed);
        }
        catch (OperationCanceledException) { return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed); }
    }

    // ════════════════════════════ 8. 회전 / 반전 ════════════════════════════

    /// <summary>동영상 회전(90/180/270) 및 좌우/상하 반전. docx '회전/좌우반전'. 항상 변환 모드.</summary>
    public Task<EditResult> RotateFlipAsync(
        string input,
        Rotation rotation,
        bool flipHorizontal,
        bool flipVertical,
        ConversionSettings settings,
        MediaRange? range = null,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        var s = settings.Clone();
        s.Rotation = rotation;
        s.FlipHorizontal = flipHorizontal;
        s.FlipVertical = flipVertical;
        return ConvertAsync(input, s, range, outputDir, progress, ct);
    }

    // ════════════════════════════ 9. 프레임 캡처 ════════════════════════════

    /// <summary>지정 시점의 화면을 PNG 이미지로 저장. docx '이미지 프레임 추출/화면 캡처'.</summary>
    public async Task<EditResult> CaptureFrameAsync(
        string input,
        TimeSpan at,
        string? outputDir = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var output = OutputNaming.Derive(input, "frame", ".png", outputDir);
        var args = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-ss", FFmpegArgsBuilder.Sec(at),
            "-i", input,
            "-frames:v", "1",
            output,
        };
        try
        {
            await _runner.RunAsync(args, cancellationToken: ct).ConfigureAwait(false);
            return EditResult.Ok(output, sw.Elapsed);
        }
        catch (OperationCanceledException) { return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed); }
    }

    // ════════════════════════════ 10. 재생 시간 정보(txt) ════════════════════════════

    /// <summary>
    /// 합칠 구간들의 누적 재생 시간 정보를 txt로 저장. docx '재생 시간 정보를 텍스트 파일로 저장'
    /// (유튜브 챕터 표기용). 각 구간의 시작 누적 시점과 라벨을 기록.
    /// </summary>
    public async Task WritePlaybackInfoAsync(
        IReadOnlyList<(string label, TimeSpan duration)> segments,
        string outputTxtPath,
        CancellationToken ct = default)
    {
        var lines = new List<string>();
        var cursor = TimeSpan.Zero;
        foreach (var (label, duration) in segments)
        {
            lines.Add($"{Format(cursor)} {label}");
            cursor += duration;
        }
        await File.WriteAllLinesAsync(outputTxtPath, lines, ct).ConfigureAwait(false);

        static string Format(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // ════════════════════════════ 11. 오디오 제거(고속) ════════════════════════════

    /// <summary>동영상에서 소리를 제거(무음). 비디오는 재인코딩 없이 스트림 복사하여 빠름.</summary>
    public async Task<EditResult> RemoveAudioAsync(
        string input,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var settings = new ConversionSettings { RemoveAudio = true };
        // 컨테이너는 원본 확장자 유지.
        var ext = Path.GetExtension(input);
        var output = OutputNaming.Derive(input, "muted", ext, outputDir);
        TimeSpan dur;
        try { dur = (await ProbeAsync(input, ct).ConfigureAwait(false)).Duration; }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr); }
        try
        {
            // 고속 모드: 비디오 copy + -an.
            var args = FFmpegArgsBuilder.BuildTranscode(input, null, OutputMode.Fast, settings, output);
            await _runner.RunAsync(args, dur, progress, ct).ConfigureAwait(false);
            Report(progress, 1.0);
            return EditResult.Ok(output, sw.Elapsed);
        }
        catch (OperationCanceledException) { return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed); }
    }

    // ════════════════════════════ 12. VOB/WebM 시간정보 재설정 ════════════════════════════

    /// <summary>
    /// 시간 정보가 손상되어 편집이 불안정한 파일(*.VOB, *.WebM 등)의 타임스탬프를 재생성.
    /// docx: 'DVD(VOB)/WebM 편집' — 편집 전 한 번 remux하면 자르기/합치기가 안정적으로 동작.
    /// 원본은 그대로 두고 '[vcut]원본명' 새 파일을 생성하여 경로를 반환.
    /// </summary>
    public async Task<EditResult> PrepareForEditingAsync(
        string input,
        string? outputDir = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var dir = outputDir ?? Path.GetDirectoryName(input) ?? Directory.GetCurrentDirectory();
        var name = "[vcut]" + Path.GetFileNameWithoutExtension(input);
        var ext = Path.GetExtension(input);
        var output = Path.Combine(dir, name + ext);
        int n = 1;
        while (File.Exists(output))
            output = Path.Combine(dir, $"{name} ({n++}){ext}");

        TimeSpan dur;
        try { dur = (await ProbeAsync(input, ct).ConfigureAwait(false)).Duration; }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr); }

        // 타임스탬프 재생성 + 음수 PTS 정규화. 스트림은 복사(화질 손상 없음).
        var args = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-fflags", "+genpts",
            "-i", input,
            "-map", "0",
            "-c", "copy",
            "-avoid_negative_ts", "make_zero",
            output,
        };
        try
        {
            await _runner.RunAsync(args, dur, progress, ct).ConfigureAwait(false);
            Report(progress, 1.0);
            return EditResult.Ok(output, sw.Elapsed);
        }
        catch (OperationCanceledException) { return EditResult.Fail("작업이 취소되었습니다.", elapsed: sw.Elapsed); }
        catch (FFmpegException ex) { return EditResult.Fail(ex.Message, ex.StdErr, sw.Elapsed); }
    }

    // ════════════════════════════ 13. 일괄 처리(배치) ════════════════════════════

    /// <summary>
    /// 여러 파일에 동일 작업을 순차 적용하고 합산 진행률을 보고. docx '여러 동영상 일괄 변환/
    /// 용량 줄이기/mp3 추출/오디오 제거/속도 조절'의 공통 기반.
    /// </summary>
    public async Task<IReadOnlyList<EditResult>> BatchAsync(
        IReadOnlyList<string> files,
        Func<string, IProgress<ProgressInfo>, CancellationToken, Task<EditResult>> operation,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<EditResult>();
        if (files.Count == 0) return results;

        double span = 1.0 / files.Count;
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var scaler = new ProgressScaler(progress, i * span, span);
            results.Add(await operation(files[i], scaler, ct).ConfigureAwait(false));
        }
        Report(progress, 1.0);
        return results;
    }

    /// <summary>여러 영상 일괄 변환(용량 줄이기 포함 — settings로 해상도/품질/FPS 조정).</summary>
    public Task<IReadOnlyList<EditResult>> BatchConvertAsync(
        IReadOnlyList<string> files, ConversionSettings settings,
        string? outputDir = null, IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
        => BatchAsync(files, (f, p, c) => ConvertAsync(f, settings.Clone(), null, outputDir, p, c), progress, ct);

    /// <summary>여러 영상에서 일괄 mp3 추출.</summary>
    public Task<IReadOnlyList<EditResult>> BatchExtractAudioAsync(
        IReadOnlyList<string> files, int bitrateKbps = 192,
        string? outputDir = null, IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
        => BatchAsync(files, (f, p, c) => ExtractAudioAsync(f, null, bitrateKbps, outputDir, p, c), progress, ct);

    /// <summary>여러 영상 일괄 오디오 제거.</summary>
    public Task<IReadOnlyList<EditResult>> BatchRemoveAudioAsync(
        IReadOnlyList<string> files,
        string? outputDir = null, IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
        => BatchAsync(files, (f, p, c) => RemoveAudioAsync(f, outputDir, p, c), progress, ct);

    /// <summary>여러 영상 일괄 배속 적용.</summary>
    public Task<IReadOnlyList<EditResult>> BatchChangeSpeedAsync(
        IReadOnlyList<string> files, double speed, ConversionSettings settings,
        string? outputDir = null, IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
        => BatchAsync(files, (f, p, c) => ChangeSpeedAsync(f, speed, null, settings.Clone(), outputDir, p, c), progress, ct);

    // ════════════════════════════ 내부 헬퍼 ════════════════════════════

    private string CreateWorkDir()
    {
        var dir = Path.Combine(TempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* 무시 */ }
    }

    private static void Report(IProgress<ProgressInfo>? p, double fraction) =>
        p?.Report(new ProgressInfo { Fraction = fraction });
}
